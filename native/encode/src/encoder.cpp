// The encoder of vv_encode.h: slots filled by the caller, encoded on a thread of the encoder's own with
// Media Foundation's Sink Writer (H.264 or AV1 in MP4, a hardware encoder when the system has one).

#include "vv_encode.h"

#include "vv_audio_track.h"

#include <windows.h>

#include <codecapi.h>
#include <mfapi.h>
#include <mferror.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <wrl/client.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstring>
#include <deque>
#include <mutex>
#include <new>
#include <string>
#include <thread>
#include <vector>

using Microsoft::WRL::ComPtr;

namespace
{
    // Enough to ride out one slow WriteSample without dropping, few enough to keep the latency low.
    constexpr int SlotCount = 4;

    enum class SlotState
    {
        Free,
        Filling,
        Full,
    };

    struct Slot
    {
        std::vector<uint8_t> data;
        int64_t time = 0;
        int64_t qpc = 0;
        SlotState state = SlotState::Free;
    };

    // The stream tap is read in chunks of 100 ms.
    constexpr int32_t AudioChunkFrames = 4800;

    // At the end, the audio for the last frame may still be in the mix: it is waited for this long at most.
    constexpr int AudioDrainMilliseconds = 200;

    int64_t MicrosecondsSince(std::chrono::steady_clock::time_point start)
    {
        return std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now() - start).count();
    }

    const GUID& SubtypeOf(int32_t codec)
    {
        return codec == VV_ENCODE_CODEC_AV1 ? MFVideoFormat_AV1 : MFVideoFormat_H264;
    }

    // Whether any transform the Sink Writer chose for the stream is a hardware one.
    bool UsesHardware(IMFSinkWriter* writer, DWORD stream)
    {
        ComPtr<IMFSinkWriterEx> ex;
        if (FAILED(writer->QueryInterface(IID_PPV_ARGS(&ex))))
        {
            return false;
        }

        for (DWORD i = 0;; i++)
        {
            GUID category = {};
            ComPtr<IMFTransform> transform;
            if (FAILED(ex->GetTransformForStream(stream, i, &category, &transform)))
            {
                return false;
            }

            ComPtr<IMFAttributes> attributes;
            UINT32 length = 0;
            if (SUCCEEDED(transform->GetAttributes(&attributes)) && SUCCEEDED(attributes->GetStringLength(MFT_ENUM_HARDWARE_URL_Attribute, &length)))
            {
                return true;
            }
        }
    }
}

struct vv_encoder
{
    std::wstring path;
    int32_t width = 0;
    int32_t height = 0;
    int32_t fps = 0;
    int32_t bitrate = 0;
    int32_t codec = VV_ENCODE_CODEC_H264;
    int32_t audioBitrate = 0;
    vv_encode_audio_read audioRead = nullptr;
    void* audioContext = nullptr;

    // Encoder thread only.
    DWORD audioStream = 0;
    int64_t audioStartQpc = 0;    // The first video frame's QPC: the file's zero.
    bool audioStarted = false;    // A flag rather than a value, since a QPC from another base may be negative.
    int64_t audioWritten = 0;     // Frames written from the file's zero, which time each next audio sample.
    std::vector<float> audioFloats;
    std::vector<int16_t> audioPcm;

    std::mutex mutex;
    std::condition_variable wake;
    std::condition_variable opened;
    Slot slots[SlotCount];
    std::deque<int> queue;
    bool closing = false;
    bool ready = false;
    HRESULT openResult = S_OK;
    vv_encode_stats stats = {};
    std::thread worker;
    int64_t firstQpc = 0;
    bool firstSeen = false;
    int64_t qpcFrequency = 1;
    int64_t lastVideoEnd = 0;
    int64_t lastVideoQpc = 0;

    // The QPC ticks of a file time; the inverse of TicksToFileTime, for placing the file's zero.
    int64_t TicksOf(int64_t fileTime) const
    {
        return ((fileTime / 10'000'000) * qpcFrequency) + (((fileTime % 10'000'000) * qpcFrequency) / 10'000'000);
    }

    HRESULT Open(ComPtr<IMFSinkWriter>& writer, DWORD& stream)
    {
        ComPtr<IMFAttributes> attributes;
        HRESULT hr = MFCreateAttributes(&attributes, 2);
        if (SUCCEEDED(hr))
        {
            hr = attributes->SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, TRUE);
        }

        if (SUCCEEDED(hr))
        {
            hr = MFCreateSinkWriterFromURL(path.c_str(), nullptr, attributes.Get(), &writer);
        }

        ComPtr<IMFMediaType> output;
        if (SUCCEEDED(hr))
        {
            hr = MFCreateMediaType(&output);
        }

        if (SUCCEEDED(hr))
        {
            output->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
            output->SetGUID(MF_MT_SUBTYPE, SubtypeOf(codec));
            output->SetUINT32(MF_MT_AVG_BITRATE, static_cast<UINT32>(bitrate));
            output->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
            if (codec == VV_ENCODE_CODEC_H264)
            {
                // Without it the encoder picks Constrained Baseline, about 0.5 dB worse at the same bitrate (spike/av1-encoder).
                output->SetUINT32(MF_MT_MPEG2_PROFILE, eAVEncH264VProfile_High);
            }

            // Tagged so players convert the colors back as they were made; untagged files looked darker in some.
            output->SetUINT32(MF_MT_YUV_MATRIX, MFVideoTransferMatrix_BT709);
            output->SetUINT32(MF_MT_VIDEO_PRIMARIES, MFVideoPrimaries_BT709);
            output->SetUINT32(MF_MT_TRANSFER_FUNCTION, MFVideoTransFunc_709);
            output->SetUINT32(MF_MT_VIDEO_NOMINAL_RANGE, MFNominalRange_16_235);
            MFSetAttributeSize(output.Get(), MF_MT_FRAME_SIZE, static_cast<UINT32>(width), static_cast<UINT32>(height));
            MFSetAttributeRatio(output.Get(), MF_MT_FRAME_RATE, static_cast<UINT32>(fps), 1);
            MFSetAttributeRatio(output.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
            hr = writer->AddStream(output.Get(), &stream);
        }

        ComPtr<IMFMediaType> input;
        if (SUCCEEDED(hr))
        {
            hr = MFCreateMediaType(&input);
        }

        if (SUCCEEDED(hr))
        {
            input->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
            input->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32);
            input->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
            input->SetUINT32(MF_MT_VIDEO_NOMINAL_RANGE, MFNominalRange_0_255);
            input->SetUINT32(MF_MT_DEFAULT_STRIDE, static_cast<UINT32>(width * 4));
            MFSetAttributeSize(input.Get(), MF_MT_FRAME_SIZE, static_cast<UINT32>(width), static_cast<UINT32>(height));
            MFSetAttributeRatio(input.Get(), MF_MT_FRAME_RATE, static_cast<UINT32>(fps), 1);
            MFSetAttributeRatio(input.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
            hr = writer->SetInputMediaType(stream, input.Get(), nullptr);
        }

        if (SUCCEEDED(hr) && audioRead != nullptr)
        {
            hr = AddAudio(writer.Get());
        }

        if (SUCCEEDED(hr))
        {
            hr = writer->BeginWriting();
        }

        return hr;
    }

    // AAC-LC, 48 kHz stereo, from 16-bit PCM (the encoder's input).
    HRESULT AddAudio(IMFSinkWriter* writer)
    {
        ComPtr<IMFMediaType> output;
        HRESULT hr = MFCreateMediaType(&output);
        if (SUCCEEDED(hr))
        {
            output->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
            output->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_AAC);
            output->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
            output->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, vv::encode::AudioRate);
            output->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, vv::encode::AudioChannels);
            output->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, vv::encode::AacBytesPerSecond(audioBitrate));
            hr = writer->AddStream(output.Get(), &audioStream);
        }

        ComPtr<IMFMediaType> input;
        if (SUCCEEDED(hr))
        {
            hr = MFCreateMediaType(&input);
        }

        if (SUCCEEDED(hr))
        {
            input->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
            input->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM);
            input->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
            input->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, vv::encode::AudioRate);
            input->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, vv::encode::AudioChannels);
            input->SetUINT32(MF_MT_AUDIO_BLOCK_ALIGNMENT, vv::encode::AudioChannels * 2);
            input->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, vv::encode::AudioRate * vv::encode::AudioChannels * 2);
            hr = writer->SetInputMediaType(audioStream, input.Get(), nullptr);
        }

        audioFloats.resize(static_cast<size_t>(AudioChunkFrames) * vv::encode::AudioChannels);
        audioPcm.resize(static_cast<size_t>(AudioChunkFrames) * vv::encode::AudioChannels);
        return hr;
    }

    // Writes the stream bus's audio up to untilQpc, each chunk where its time puts it on the file's
    // timeline: a gap is filled with silence, and what comes before the file or overlaps what was written is
    // left out, so the audio stays with the video whatever the bus does. When videoEnd is given and the
    // audio is more than a second behind it, silence catches up, so the file writer, which waits for the
    // stream that is behind, never holds the video back. Returns the file time the audio now ends at, or -1
    // without audio.
    int64_t PumpAudio(IMFSinkWriter* writer, int64_t untilQpc, int64_t videoEnd)
    {
        if (audioRead == nullptr || !audioStarted)
        {
            return -1;
        }

        HRESULT write = S_OK;
        for (;;)
        {
            int64_t chunkQpc = 0;
            int32_t frames = audioRead(audioContext, audioFloats.data(), AudioChunkFrames, untilQpc, &chunkQpc);
            if (frames <= 0)
            {
                break;
            }

            // A chunk more than a second past the video is not this file's (a clock on another base): it
            // is left out rather than filled up to with silence.
            int64_t position = vv::encode::FrameAt(chunkQpc, audioStartQpc, qpcFrequency);
            int64_t videoFrames = vv::encode::FileTimeToFrames(videoEnd >= 0 ? videoEnd : lastVideoEnd);
            if (position > videoFrames + vv::encode::MaxAudioLagFrames)
            {
                continue;
            }

            vv::encode::Placement placement = vv::encode::Place(position, frames, audioWritten);
            write = WriteSilence(writer, placement.silence);
            int64_t kept = frames - placement.skip;
            if (SUCCEEDED(write) && kept > 0)
            {
                size_t samples = static_cast<size_t>(kept) * vv::encode::AudioChannels;
                vv::encode::ToPcm16(audioFloats.data() + (static_cast<size_t>(placement.skip) * vv::encode::AudioChannels), audioPcm.data(), samples);
                write = WriteAudio(writer, audioPcm.data(), samples * sizeof(int16_t), vv::encode::FramesToFileTime(audioWritten), vv::encode::FramesToFileTime(kept));
                audioWritten += SUCCEEDED(write) ? kept : 0;
            }

            if (FAILED(write))
            {
                break;
            }
        }

        if (SUCCEEDED(write) && videoEnd >= 0)
        {
            write = WriteSilence(writer, vv::encode::FileTimeToFrames(videoEnd) - vv::encode::MaxAudioLagFrames - audioWritten);
        }

        std::lock_guard<std::mutex> lock(mutex);
        stats.audio_frames = audioWritten;
        if (FAILED(write))
        {
            stats.last_error = write;
            return -1;
        }

        return vv::encode::FramesToFileTime(audioWritten);
    }

    // Writes frames of silence (none for zero or less) and counts them.
    HRESULT WriteSilence(IMFSinkWriter* writer, int64_t frames)
    {
        HRESULT hr = S_OK;
        while (SUCCEEDED(hr) && frames > 0)
        {
            int64_t run = frames < AudioChunkFrames ? frames : AudioChunkFrames;
            size_t samples = static_cast<size_t>(run) * vv::encode::AudioChannels;
            std::fill(audioPcm.begin(), audioPcm.begin() + static_cast<ptrdiff_t>(samples), static_cast<int16_t>(0));
            hr = WriteAudio(writer, audioPcm.data(), samples * sizeof(int16_t), vv::encode::FramesToFileTime(audioWritten), vv::encode::FramesToFileTime(run));
            if (SUCCEEDED(hr))
            {
                audioWritten += run;
                frames -= run;
                std::lock_guard<std::mutex> lock(mutex);
                stats.audio_silence_frames += run;
            }
        }

        return hr;
    }

    HRESULT WriteAudio(IMFSinkWriter* writer, const int16_t* pcm, size_t bytes, int64_t time, int64_t duration)
    {
        ComPtr<IMFMediaBuffer> buffer;
        HRESULT hr = MFCreateMemoryBuffer(static_cast<DWORD>(bytes), &buffer);
        BYTE* target = nullptr;
        if (SUCCEEDED(hr))
        {
            hr = buffer->Lock(&target, nullptr, nullptr);
        }

        if (SUCCEEDED(hr))
        {
            std::memcpy(target, pcm, bytes);
            buffer->Unlock();
            buffer->SetCurrentLength(static_cast<DWORD>(bytes));
        }

        ComPtr<IMFSample> sample;
        if (SUCCEEDED(hr))
        {
            hr = MFCreateSample(&sample);
        }

        if (SUCCEEDED(hr))
        {
            sample->AddBuffer(buffer.Get());
            sample->SetSampleTime(time);
            sample->SetSampleDuration(duration);
            hr = writer->WriteSample(audioStream, sample.Get());
        }

        return hr;
    }

    void Run()
    {
        HRESULT com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        HRESULT hr = MFStartup(MF_VERSION);
        ComPtr<IMFSinkWriter> writer;
        DWORD stream = 0;
        if (SUCCEEDED(hr))
        {
            hr = Open(writer, stream);
        }

        {
            std::lock_guard<std::mutex> lock(mutex);
            openResult = hr;
            ready = true;
            stats.hardware = SUCCEEDED(hr) && UsesHardware(writer.Get(), stream) ? 1 : 0;
        }

        opened.notify_all();
        const LONGLONG duration = 10'000'000LL / fps;
        const DWORD frameBytes = static_cast<DWORD>(width) * static_cast<DWORD>(height) * 4;
        while (SUCCEEDED(hr))
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
            ComPtr<IMFMediaBuffer> buffer;
            ComPtr<IMFSample> sample;
            HRESULT write = MFCreateMemoryBuffer(frameBytes, &buffer);
            BYTE* target = nullptr;
            if (SUCCEEDED(write))
            {
                write = buffer->Lock(&target, nullptr, nullptr);
            }

            if (SUCCEEDED(write))
            {
                std::memcpy(target, slot.data.data(), frameBytes);
                buffer->Unlock();
                buffer->SetCurrentLength(frameBytes);
                write = MFCreateSample(&sample);
            }

            // The file's zero is the first frame; audio from before it is left out.
            if (!audioStarted)
            {
                audioStarted = true;
                audioStartQpc = slot.qpc - TicksOf(slot.time);
            }

            if (SUCCEEDED(write))
            {
                sample->AddBuffer(buffer.Get());
                sample->SetSampleTime(slot.time);
                sample->SetSampleDuration(duration);
                auto start = std::chrono::steady_clock::now();
                write = writer->WriteSample(stream, sample.Get());
                int64_t took = MicrosecondsSince(start);
                std::lock_guard<std::mutex> lock(mutex);
                stats.last_write_us = took;
                stats.max_write_us = std::max(stats.max_write_us, took);
                stats.total_write_us += took;
                if (SUCCEEDED(write))
                {
                    stats.written++;
                }
            }

            int64_t frameEnd = slot.time + duration;
            int64_t frameQpc = slot.qpc;
            {
                std::lock_guard<std::mutex> lock(mutex);
                slot.state = SlotState::Free;
                if (FAILED(write))
                {
                    stats.last_error = write;
                }
            }

            // The audio up to this frame, in order with the video (recording detailed design 5.1).
            int64_t audioEnd = PumpAudio(writer.Get(), frameQpc, frameEnd);
            lastVideoEnd = frameEnd;
            lastVideoQpc = frameQpc;
            if (audioEnd >= 0)
            {
                std::lock_guard<std::mutex> lock(mutex);
                stats.av_offset_us = (audioEnd - frameEnd) / 10;
            }
        }

        // The audio for the last frame is still being mixed for a moment: wait for it, briefly.
        if (writer && audioRead != nullptr && lastVideoEnd > 0)
        {
            auto waited = std::chrono::steady_clock::now();
            int64_t untilQpc = lastVideoQpc + (qpcFrequency / fps);
            while (PumpAudio(writer.Get(), untilQpc, -1) < lastVideoEnd && MicrosecondsSince(waited) < AudioDrainMilliseconds * 1000)
            {
                std::this_thread::sleep_for(std::chrono::milliseconds(10));
            }
        }

        if (writer)
        {
            HRESULT finalize = writer->Finalize();
            bool empty = finalize == MF_E_SINK_NO_SAMPLES_PROCESSED;
            if (empty)
            {
                // Nothing was recorded: leave no unplayable file behind.
                writer.Reset();
                DeleteFileW(path.c_str());
            }
            else if (FAILED(finalize))
            {
                std::lock_guard<std::mutex> lock(mutex);
                stats.last_error = finalize;
            }

            writer.Reset();
        }

        if (FAILED(hr))
        {
            std::lock_guard<std::mutex> lock(mutex);
            stats.last_error = hr;
        }

        MFShutdown();
        if (SUCCEEDED(com))
        {
            CoUninitialize();
        }
    }
};

extern "C"
{
    int32_t vv_encode_api_version(void)
    {
        return VV_ENCODE_API_VERSION;
    }

    int32_t vv_encode_hardware_available(int32_t codec)
    {
        if (codec != VV_ENCODE_CODEC_H264 && codec != VV_ENCODE_CODEC_AV1)
        {
            return 0;
        }

        HRESULT com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        MFT_REGISTER_TYPE_INFO output = { MFMediaType_Video, SubtypeOf(codec) };
        IMFActivate** activates = nullptr;
        UINT32 count = 0;
        HRESULT hr = MFTEnumEx(MFT_CATEGORY_VIDEO_ENCODER, MFT_ENUM_FLAG_HARDWARE | MFT_ENUM_FLAG_SORTANDFILTER, nullptr, &output, &activates, &count);
        for (UINT32 i = 0; i < count; i++)
        {
            activates[i]->Release();
        }

        CoTaskMemFree(activates);
        if (SUCCEEDED(com))
        {
            CoUninitialize();
        }

        return SUCCEEDED(hr) && count > 0 ? 1 : 0;
    }

    int32_t vv_encode_open(const vv_encode_config* config, vv_encoder** encoder)
    {
        if (config == nullptr || config->path == nullptr || encoder == nullptr || config->width <= 0 || config->height <= 0 || (config->width % 2) != 0 ||
            (config->height % 2) != 0 || config->fps <= 0 || config->bitrate <= 0 || (config->codec != VV_ENCODE_CODEC_H264 && config->codec != VV_ENCODE_CODEC_AV1))
        {
            return VV_ENCODE_INVALID_ARGUMENT;
        }

        const int32_t width = config->width;
        const int32_t height = config->height;

        *encoder = nullptr;
        try
        {
            auto created = new vv_encoder();
            created->path = config->path;
            created->width = width;
            created->height = height;
            created->fps = config->fps;
            created->bitrate = config->bitrate;
            created->codec = config->codec;
            created->audioBitrate = config->audio_bitrate;
            created->audioRead = config->audio_read;
            created->audioContext = config->audio_context;
            LARGE_INTEGER frequency;
            QueryPerformanceFrequency(&frequency);
            created->qpcFrequency = frequency.QuadPart;
            for (Slot& slot : created->slots)
            {
                slot.data.resize(static_cast<size_t>(width) * static_cast<size_t>(height) * 4);
            }

            created->worker = std::thread([created] { created->Run(); });
            HRESULT result;
            {
                std::unique_lock<std::mutex> lock(created->mutex);
                created->opened.wait(lock, [created] { return created->ready; });
                result = created->openResult;
            }

            if (FAILED(result))
            {
                created->worker.join();
                delete created;
                return VV_ENCODE_SYSTEM_ERROR;
            }

            *encoder = created;
            return VV_ENCODE_OK;
        }
        catch (const std::bad_alloc&)
        {
            return VV_ENCODE_OUT_OF_MEMORY;
        }
        catch (...)
        {
            return VV_ENCODE_SYSTEM_ERROR;
        }
    }

    int32_t vv_encode_push_video(vv_encoder* encoder, const uint8_t* bgra, int32_t length, int64_t qpc)
    {
        if (encoder == nullptr || bgra == nullptr || length != encoder->width * encoder->height * 4)
        {
            return VV_ENCODE_INVALID_ARGUMENT;
        }

        int index = -1;
        {
            std::lock_guard<std::mutex> lock(encoder->mutex);
            encoder->stats.pushed++;
            for (int i = 0; i < SlotCount; i++)
            {
                if (encoder->slots[i].state == SlotState::Free)
                {
                    index = i;
                    encoder->slots[i].state = SlotState::Filling;
                    break;
                }
            }

            if (index < 0)
            {
                encoder->stats.dropped++;
                return VV_ENCODE_DROPPED;
            }
        }

        // Unity reads rows from the bottom; the encoder takes them from the top.
        Slot& slot = encoder->slots[index];
        const size_t row = static_cast<size_t>(encoder->width) * 4;
        for (int32_t y = 0; y < encoder->height; y++)
        {
            std::memcpy(slot.data.data() + (row * static_cast<size_t>(y)), bgra + (row * static_cast<size_t>(encoder->height - 1 - y)), row);
        }

        {
            std::lock_guard<std::mutex> lock(encoder->mutex);
            if (!encoder->firstSeen)
            {
                encoder->firstSeen = true;
                encoder->firstQpc = qpc;
            }

            // QPC ticks to the file's 100 ns units, from the first frame; split to keep the product in range.
            int64_t ticks = qpc - encoder->firstQpc;
            slot.time = vv::encode::TicksToFileTime(ticks, encoder->qpcFrequency);
            slot.qpc = qpc;
            slot.state = SlotState::Full;
            encoder->queue.push_back(index);
        }

        encoder->wake.notify_one();
        return VV_ENCODE_OK;
    }

    int32_t vv_encode_get_stats(vv_encoder* encoder, vv_encode_stats* stats)
    {
        if (encoder == nullptr || stats == nullptr)
        {
            return VV_ENCODE_INVALID_ARGUMENT;
        }

        std::lock_guard<std::mutex> lock(encoder->mutex);
        *stats = encoder->stats;
        return VV_ENCODE_OK;
    }

    int32_t vv_encode_close(vv_encoder* encoder)
    {
        if (encoder == nullptr)
        {
            return VV_ENCODE_INVALID_ARGUMENT;
        }

        {
            std::lock_guard<std::mutex> lock(encoder->mutex);
            encoder->closing = true;
        }

        encoder->wake.notify_all();
        encoder->worker.join();
        int32_t result = encoder->stats.last_error == 0 ? VV_ENCODE_OK : VV_ENCODE_SYSTEM_ERROR;
        delete encoder;
        return result;
    }
}
