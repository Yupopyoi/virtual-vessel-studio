// The C API of vv_stream.h: the stream owns the frame slots, the encoding thread, the send queue, and the
// sending thread (streaming detailed design 8, 9).

#include "vv_stream.h"

#include "flv.h"
#include "mft_encoders.h"
#include "reconnect.h"
#include "rtmp_client.h"
#include "send_queue.h"
#include "vv_audio_track.h"

#include <windows.h>

#include <mfapi.h>

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <deque>
#include <mutex>
#include <new>
#include <string>
#include <thread>
#include <vector>

using namespace vv::stream;

namespace
{
    // As for recording: enough to ride out a slow encode, few enough to keep the delay low.
    constexpr int SlotCount = 4;

    // The send queue's limit before video is dropped to the next keyframe (streaming detailed design 8.2).
    constexpr uint32_t QueueLimitMs = 3'000;

    // Connecting and each RTMP step may take this long.
    constexpr int ConnectTimeoutMs = 10'000;

    // At the end, what is queued is sent for at most this long.
    constexpr int CloseFlushMs = 2'000;

    constexpr int32_t AudioChunkFrames = 4800;

    int64_t NowMs()
    {
        return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now().time_since_epoch()).count();
    }

    enum class SlotState
    {
        Free,
        Filling,
        Full,
    };

    struct Slot
    {
        std::vector<uint8_t> data;
        int64_t qpc = 0;
        SlotState state = SlotState::Free;
    };
}

struct vv_stream
{
    RtmpUrl url;
    std::string key;
    VideoEncoderConfig video;
    int32_t audioBitrate = 0;
    vv_stream_audio_read audioRead = nullptr;
    void* audioContext = nullptr;
    int64_t qpcFrequency = 1;

    std::mutex mutex;
    std::condition_variable wake;
    Slot slots[SlotCount];
    std::deque<int> queue;
    bool closing = false;
    vv_stream_stats stats = {};

    SendQueue sendQueue{ QueueLimitMs };
    std::atomic<bool> stopSending{ false };
    std::atomic<bool> requestKeyframe{ false };

    // The sequence headers, kept so each new connection can send them again.
    std::mutex headersMutex;
    Bytes videoHeader;
    Bytes audioHeader;

    std::thread encoder;
    std::thread sender;

    // Encoding thread only.
    bool started = false;
    int64_t startQpc = 0;
    int64_t audioWritten = 0;
    std::vector<float> audioFloats;
    std::vector<int16_t> audioPcm;

    ~vv_stream()
    {
        // The key is not left in freed memory.
        SecureZeroMemory(key.data(), key.size());
    }

    void SetState(int32_t state, int32_t problem)
    {
        std::lock_guard<std::mutex> lock(mutex);
        stats.state = state;
        stats.problem = problem;
    }

    int32_t State()
    {
        std::lock_guard<std::mutex> lock(mutex);
        return stats.state;
    }

    // ---- Encoding

    void Encode()
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
        }

        MFShutdown();
        if (SUCCEEDED(com))
        {
            CoUninitialize();
        }
    }

    void QueueVideo(const EncodedFrame& frame)
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

        sendQueue.Push(std::move(packet));
    }

    // The stream bus up to this frame, placed by its time as for recording (recording detailed design 8.1),
    // encoded to AAC and queued.
    bool PumpAudio(AacEncoder& aac, int64_t untilQpc, int64_t videoEnd, std::string& error)
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

        for (const EncodedFrame& frame : frames)
        {
            Packet packet;
            packet.type = rtmp::Audio;
            packet.timestamp = static_cast<uint32_t>(frame.time / 10'000);
            packet.body = flv::AacFrame(frame.data.data(), frame.data.size());
            sendQueue.Push(std::move(packet));
        }

        std::lock_guard<std::mutex> lock(mutex);
        stats.av_offset_us = (vv::encode::FramesToFileTime(audioWritten) - videoEnd) / 10;
        return true;
    }

    bool Silence(AacEncoder& aac, int64_t frames, std::vector<EncodedFrame>& out, std::string& error)
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

    // ---- Sending

    void Send()
    {
        bool everLive = false;
        int attempt = 0;
        int64_t lostAt = 0;
        while (!stopSending.load())
        {
            RtmpClient client(CreateTransport(url.secure));
            std::string error;
            PublishFailure failure = client.Publish(url, key, video.codec == VideoCodec::Av1, ConnectTimeoutMs, error);
            if (failure != PublishFailure::None)
            {
                // Before the first success the stream fails at once, so the user can fix the URL or the key.
                if (!everLive || failure == PublishFailure::Rejected || NowMs() - lostAt > GiveUpAfterMs)
                {
                    int32_t problem = failure == PublishFailure::Rejected ? VV_STREAM_PROBLEM_REJECTED
                        : everLive                                       ? VV_STREAM_PROBLEM_CONNECTION_LOST
                                                                         : VV_STREAM_PROBLEM_UNREACHABLE;
                    SetState(VV_STREAM_STATE_FAILED, problem);
                    return;
                }

                WaitBeforeRetry(attempt++);
                continue;
            }

            everLive = true;
            attempt = 0;
            SetState(VV_STREAM_STATE_LIVE, VV_STREAM_PROBLEM_NONE);
            bool lost = !Publish(client, error);
            client.Close(key);
            if (!lost)
            {
                break;
            }

            lostAt = NowMs();
            {
                std::lock_guard<std::mutex> lock(mutex);
                stats.reconnects++;
            }

            SetState(VV_STREAM_STATE_RECONNECTING, VV_STREAM_PROBLEM_CONNECTION_LOST);
            WaitBeforeRetry(attempt++);
        }

        if (State() != VV_STREAM_STATE_FAILED)
        {
            SetState(VV_STREAM_STATE_CLOSED, VV_STREAM_PROBLEM_NONE);
        }
    }

    void WaitBeforeRetry(int attempt)
    {
        int64_t until = NowMs() + ReconnectDelayMs(attempt);
        while (!stopSending.load() && NowMs() < until)
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(50));
        }

        // What waited meanwhile is stale; the next connection starts from a new keyframe.
        sendQueue.Clear();
    }

    // Sends the stream on a published connection until it is asked to stop (true) or the connection is lost (false).
    bool Publish(RtmpClient& client, std::string& error)
    {
        flv::StreamInfo info;
        info.width = video.width;
        info.height = video.height;
        info.frameRate = video.fps;
        info.videoKbps = video.bitrate / 1000;
        info.audioKbps = audioBitrate / 1000;
        info.av1 = video.codec == VideoCodec::Av1;
        rtmp::Message metadata;
        metadata.type = rtmp::DataAmf0;
        metadata.payload = flv::MetaData(info);
        if (!client.Send(rtmp::CommandChunks, metadata, error))
        {
            return false;
        }

        // Each connection starts at a keyframe, with the sequence headers before it, and its own zero time.
        sendQueue.Clear();
        requestKeyframe = true;
        bool waitingForKeyframe = true;
        uint32_t zero = 0;
        int64_t flushUntil = 0;
        for (;;)
        {
            if (stopSending.load() && flushUntil == 0)
            {
                flushUntil = NowMs() + CloseFlushMs;
            }

            if (flushUntil != 0 && (NowMs() > flushUntil || sendQueue.DurationMs() == 0))
            {
                Packet last;
                while (NowMs() <= flushUntil && sendQueue.Pop(last, 0))
                {
                    if (!waitingForKeyframe && !SendPacket(client, last, zero, error))
                    {
                        return true;
                    }
                }

                return true;
            }

            Packet packet;
            if (!sendQueue.Pop(packet, 100))
            {
                if (!client.Poll(error))
                {
                    return false;
                }

                continue;
            }

            if (waitingForKeyframe)
            {
                if (packet.type != rtmp::Video || !packet.keyframe)
                {
                    continue;
                }

                waitingForKeyframe = false;
                zero = packet.timestamp;
                Bytes videoHeaderCopy;
                Bytes audioHeaderCopy;
                {
                    std::lock_guard<std::mutex> lock(headersMutex);
                    videoHeaderCopy = videoHeader;
                    audioHeaderCopy = audioHeader;
                }

                if (!SendBody(client, rtmp::Video, 0, videoHeaderCopy, error) || (!audioHeaderCopy.empty() && !SendBody(client, rtmp::Audio, 0, audioHeaderCopy, error)))
                {
                    return false;
                }
            }

            if (!SendPacket(client, packet, zero, error) || !client.Poll(error))
            {
                return false;
            }

            std::lock_guard<std::mutex> lock(mutex);
            stats.bytes_sent = static_cast<int64_t>(client.BytesSent());
            stats.frames_sent += packet.type == rtmp::Video ? 1 : 0;
        }
    }

    bool SendPacket(RtmpClient& client, const Packet& packet, uint32_t zero, std::string& error)
    {
        uint32_t timestamp = packet.timestamp >= zero ? packet.timestamp - zero : 0;
        return SendBody(client, packet.type, timestamp, packet.body, error);
    }

    static bool SendBody(RtmpClient& client, uint8_t type, uint32_t timestamp, const Bytes& body, std::string& error)
    {
        if (body.empty())
        {
            return true;
        }

        rtmp::Message message;
        message.type = type;
        message.timestamp = timestamp;
        message.payload = body;
        return client.Send(type == rtmp::Audio ? rtmp::AudioChunks : rtmp::VideoChunks, message, error);
    }
};

extern "C"
{
    int32_t vv_stream_api_version(void)
    {
        return VV_STREAM_API_VERSION;
    }

    int32_t vv_stream_open(const vv_stream_config* config, vv_stream** stream)
    {
        if (config == nullptr || stream == nullptr || config->url == nullptr || config->key == nullptr || config->key[0] == '\0' || config->width <= 0 ||
            config->height <= 0 || (config->width % 2) != 0 || (config->height % 2) != 0 || config->fps <= 0 || config->video_bitrate <= 0 ||
            (config->codec != VV_STREAM_CODEC_H264 && config->codec != VV_STREAM_CODEC_AV1))
        {
            return VV_STREAM_INVALID_ARGUMENT;
        }

        RtmpUrl url;
        if (!ParseRtmpUrl(config->url, url))
        {
            return VV_STREAM_INVALID_ARGUMENT;
        }

        *stream = nullptr;
        try
        {
            auto created = new vv_stream();
            created->url = url;
            created->key = config->key;
            created->video.codec = config->codec == VV_STREAM_CODEC_AV1 ? VideoCodec::Av1 : VideoCodec::H264;
            created->video.width = config->width;
            created->video.height = config->height;
            created->video.fps = config->fps;
            created->video.bitrate = config->video_bitrate;
            created->video.keyframeSeconds = config->keyframe_seconds > 0 ? config->keyframe_seconds : 2;
            created->audioBitrate = config->audio_bitrate;
            created->audioRead = config->audio_read;
            created->audioContext = config->audio_context;
            LARGE_INTEGER frequency;
            QueryPerformanceFrequency(&frequency);
            created->qpcFrequency = frequency.QuadPart;
            created->stats.state = VV_STREAM_STATE_CONNECTING;
            for (Slot& slot : created->slots)
            {
                slot.data.resize(static_cast<size_t>(config->width) * static_cast<size_t>(config->height) * 3 / 2);
            }

            created->encoder = std::thread([created] { created->Encode(); });
            created->sender = std::thread([created] { created->Send(); });
            *stream = created;
            return VV_STREAM_OK;
        }
        catch (const std::bad_alloc&)
        {
            return VV_STREAM_OUT_OF_MEMORY;
        }
        catch (...)
        {
            return VV_STREAM_SYSTEM_ERROR;
        }
    }

    int32_t vv_stream_push_video(vv_stream* stream, const uint8_t* nv12, int32_t length, int64_t qpc)
    {
        if (stream == nullptr || nv12 == nullptr || static_cast<size_t>(length) != stream->slots[0].data.size())
        {
            return VV_STREAM_INVALID_ARGUMENT;
        }

        int index = -1;
        {
            std::lock_guard<std::mutex> lock(stream->mutex);
            stream->stats.frames_pushed++;
            for (int i = 0; i < SlotCount; i++)
            {
                if (stream->slots[i].state == SlotState::Free)
                {
                    index = i;
                    stream->slots[i].state = SlotState::Filling;
                    break;
                }
            }

            if (index < 0)
            {
                stream->stats.frames_dropped++;
                return VV_STREAM_DROPPED;
            }
        }

        Slot& slot = stream->slots[index];
        std::memcpy(slot.data.data(), nv12, slot.data.size());
        {
            std::lock_guard<std::mutex> lock(stream->mutex);
            slot.qpc = qpc;
            slot.state = SlotState::Full;
            stream->queue.push_back(index);
        }

        stream->wake.notify_one();
        return VV_STREAM_OK;
    }

    int32_t vv_stream_get_stats(vv_stream* stream, vv_stream_stats* stats)
    {
        if (stream == nullptr || stats == nullptr)
        {
            return VV_STREAM_INVALID_ARGUMENT;
        }

        int64_t queueMs = stream->sendQueue.DurationMs();
        int64_t queueDropped = stream->sendQueue.Dropped();
        std::lock_guard<std::mutex> lock(stream->mutex);
        *stats = stream->stats;
        stats->queue_ms = queueMs;
        stats->frames_dropped += queueDropped;
        stats->frames_dropped_queue = queueDropped;
        return VV_STREAM_OK;
    }

    int32_t vv_stream_close(vv_stream* stream)
    {
        if (stream == nullptr)
        {
            return VV_STREAM_INVALID_ARGUMENT;
        }

        {
            std::lock_guard<std::mutex> lock(stream->mutex);
            stream->closing = true;
        }

        stream->wake.notify_all();
        stream->encoder.join();
        stream->stopSending = true;
        stream->sendQueue.Wake();
        stream->sender.join();
        int32_t result = stream->stats.problem == VV_STREAM_PROBLEM_NONE ? VV_STREAM_OK : VV_STREAM_SYSTEM_ERROR;
        delete stream;
        return result;
    }

    int32_t vv_stream_test_connection(const char* url, int32_t timeout_ms)
    {
        RtmpUrl parsed;
        if (url == nullptr || !ParseRtmpUrl(url, parsed))
        {
            return VV_STREAM_PROBLEM_UNREACHABLE;
        }

        RtmpClient client(CreateTransport(parsed.secure));
        std::string error;
        PublishFailure failure = client.TestConnect(parsed, timeout_ms > 0 ? timeout_ms : ConnectTimeoutMs, error);
        return failure == PublishFailure::None ? VV_STREAM_PROBLEM_NONE : failure == PublishFailure::Rejected ? VV_STREAM_PROBLEM_REJECTED : VV_STREAM_PROBLEM_UNREACHABLE;
    }
}
