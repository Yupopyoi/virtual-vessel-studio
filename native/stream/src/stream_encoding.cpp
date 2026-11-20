// The stream's encoding thread (streaming detailed design 8.1): video and audio encoded and queued as FLV tags.

#include "stream_session.h"

#include "vv_audio_track.h"

#include <windows.h>

#include <mfapi.h>

#include <algorithm>

using namespace vv::stream;
using namespace vv::stream::session;

void vv_stream::Encode()
{
    HRESULT com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    MFStartup(MF_VERSION);
    {
        VideoEncoder videoEncoder;
        AacEncoder aacEncoder;
        std::string error;
        bool ok = videoEncoder.Open(video, error) && (audioRead == nullptr || aacEncoder.Open(audioBitrate, error));
        {
            std::lock_guard<std::mutex> lock(mutex);
            stats.hardware = videoEncoder.Hardware() ? 1 : 0;
        }

        if (!ok)
        {
            SetState(VV_STREAM_STATE_FAILED, VV_STREAM_PROBLEM_ENCODER_FAILED);
            stopSending = true;
            sendQueue.Wake();
        }

        if (audioRead != nullptr)
        {
            std::lock_guard<std::mutex> lock(headersMutex);
            Bytes config = flv::AacLc48kStereoConfig();
            audioHeader = flv::AacSequenceHeader(config.data(), config.size());
            audioFloats.resize(static_cast<size_t>(AudioChunkFrames) * vv::encode::AudioChannels);
            audioPcm.resize(static_cast<size_t>(AudioChunkFrames) * vv::encode::AudioChannels);
        }

        const size_t frameBytes = static_cast<size_t>(video.width) * static_cast<size_t>(video.height) * 3 / 2;
        const int64_t duration = 10'000'000LL / video.fps;
        while (ok)
        {
            int index = -1;
            {
                std::unique_lock<std::mutex> lock(mutex);
                wake.wait(lock, [this] { return closing || !queue.empty(); });
                if (queue.empty())
                {
                    break;
                }

                index = queue.front();
                queue.pop_front();
            }

            Slot& slot = slots[index];
            if (!started)
            {
                started = true;
                startQpc = slot.qpc;
            }

            int64_t time = vv::encode::TicksToFileTime(slot.qpc - startQpc, qpcFrequency);
            int64_t frameQpc = slot.qpc;
            std::vector<EncodedFrame> frames;
            auto begin = std::chrono::steady_clock::now();
            ok = videoEncoder.Encode(slot.data.data(), frameBytes, time, requestKeyframe.exchange(false), frames, error);
            int64_t took = std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now() - begin).count();
            {
                std::lock_guard<std::mutex> lock(mutex);
                slot.state = SlotState::Free;
                stats.encode_us_total += took;
            }

            for (const EncodedFrame& frame : frames)
            {
                QueueVideo(frame);
            }

            if (ok && audioRead != nullptr)
            {
                ok = PumpAudio(aacEncoder, frameQpc, time + duration, error);
            }

            if (!ok)
            {
                SetState(VV_STREAM_STATE_FAILED, VV_STREAM_PROBLEM_ENCODER_FAILED);
                stopSending = true;
                sendQueue.Wake();
            }
        }

        if (ok)
        {
            DrainEncoders(videoEncoder, aacEncoder);
        }
    }

    MFShutdown();
    if (SUCCEEDED(com))
    {
        CoUninitialize();
    }
}

void vv_stream::DrainEncoders(VideoEncoder& videoEncoder, AacEncoder& aacEncoder)
{
    std::string error;
    std::vector<EncodedFrame> frames;
    videoEncoder.Drain(CloseDrainMs, frames, error);
    for (const EncodedFrame& frame : frames)
    {
        QueueVideo(frame);
    }

    if (audioRead != nullptr)
    {
        frames.clear();
        aacEncoder.Drain(frames, error);
        QueueAudio(frames);
    }
}

void vv_stream::QueueVideo(const EncodedFrame& frame)
{
    Packet packet;
    packet.type = rtmp::Video;
    packet.timestamp = static_cast<uint32_t>(frame.time / 10'000);
    packet.keyframe = frame.keyframe;
    if (video.codec == VideoCodec::H264)
    {
        std::vector<flv::Span> nals = flv::SplitAnnexB(frame.data.data(), frame.data.size());
        const flv::Span* sps = nullptr;
        const flv::Span* pps = nullptr;
        for (const flv::Span& nal : nals)
        {
            int type = flv::NalType(nal);
            sps = type == 7 ? &nal : sps;
            pps = type == 8 ? &nal : pps;
            packet.keyframe = packet.keyframe || type == 5;
        }

        if (sps != nullptr && pps != nullptr)
        {
            std::lock_guard<std::mutex> lock(headersMutex);
            videoHeader = flv::AvcSequenceHeader(*sps, *pps);
        }

        packet.body = flv::AvcFrame(nals, packet.keyframe);
    }
    else
    {
        for (const auto& obu : flv::SplitObus(frame.data.data(), frame.data.size()))
        {
            if (obu.first == flv::ObuSequenceHeader)
            {
                // The OBU's header and size field come before the payload the parser reads.
                const uint8_t* bytes = obu.second.data;
                size_t at = 1 + ((bytes[0] & 0x04) != 0 ? 1 : 0);
                while (at < obu.second.length && (bytes[at] & 0x80) != 0)
                {
                    at++;
                }

                at++;
                flv::Av1Info info;
                if (at < obu.second.length && flv::ParseSequenceHeader(bytes + at, obu.second.length - at, info))
                {
                    std::lock_guard<std::mutex> lock(headersMutex);
                    videoHeader = flv::Av1SequenceStart(info, obu.second);
                }

                packet.keyframe = true;
            }
        }

        packet.body = flv::Av1Frames(frame.data.data(), frame.data.size(), packet.keyframe);
    }

    // The sequence headers above are kept even when not publishing, for the next connection.
    if (publishing.load())
    {
        sendQueue.Push(std::move(packet));
    }
}

bool vv_stream::PumpAudio(AacEncoder& aac, int64_t untilQpc, int64_t videoEnd, std::string& error)
{
    std::vector<EncodedFrame> frames;
    for (;;)
    {
        int64_t chunkQpc = 0;
        int32_t count = audioRead(audioContext, audioFloats.data(), AudioChunkFrames, untilQpc, &chunkQpc);
        if (count <= 0)
        {
            break;
        }

        int64_t position = vv::encode::FrameAt(chunkQpc, startQpc, qpcFrequency);
        if (position > vv::encode::FileTimeToFrames(videoEnd) + vv::encode::MaxAudioLagFrames)
        {
            continue;
        }

        vv::encode::Placement placement = vv::encode::Place(position, count, audioWritten);
        if (!Silence(aac, placement.silence, frames, error))
        {
            return false;
        }

        int64_t kept = count - placement.skip;
        if (kept > 0)
        {
            size_t samples = static_cast<size_t>(kept) * vv::encode::AudioChannels;
            vv::encode::ToPcm16(audioFloats.data() + (static_cast<size_t>(placement.skip) * vv::encode::AudioChannels), audioPcm.data(), samples);
            if (!aac.Encode(audioPcm.data(), static_cast<size_t>(kept), vv::encode::FramesToFileTime(audioWritten), frames, error))
            {
                return false;
            }

            audioWritten += kept;
        }
    }

    // Never more than a second behind the video, so the stream's audio does not lag behind it.
    if (!Silence(aac, vv::encode::FileTimeToFrames(videoEnd) - vv::encode::MaxAudioLagFrames - audioWritten, frames, error))
    {
        return false;
    }

    QueueAudio(frames);
    std::lock_guard<std::mutex> lock(mutex);
    stats.av_offset_us = (vv::encode::FramesToFileTime(audioWritten) - videoEnd) / 10;
    return true;
}

void vv_stream::QueueAudio(const std::vector<EncodedFrame>& frames)
{
    if (!publishing.load())
    {
        return;
    }

    for (const EncodedFrame& frame : frames)
    {
        Packet packet;
        packet.type = rtmp::Audio;
        packet.timestamp = static_cast<uint32_t>(frame.time / 10'000);
        packet.body = flv::AacFrame(frame.data.data(), frame.data.size());
        sendQueue.Push(std::move(packet));
    }
}

bool vv_stream::Silence(AacEncoder& aac, int64_t frames, std::vector<EncodedFrame>& out, std::string& error)
{
    while (frames > 0)
    {
        int64_t run = frames < AudioChunkFrames ? frames : AudioChunkFrames;
        std::fill(audioPcm.begin(), audioPcm.begin() + static_cast<ptrdiff_t>(run * vv::encode::AudioChannels), static_cast<int16_t>(0));
        if (!aac.Encode(audioPcm.data(), static_cast<size_t>(run), vv::encode::FramesToFileTime(audioWritten), out, error))
        {
            return false;
        }

        audioWritten += run;
        frames -= run;
    }

    return true;
}
