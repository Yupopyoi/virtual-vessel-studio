// The encoder of vv_encode.h: slots filled by the caller, encoded on a thread of the encoder's own with
// Media Foundation's Sink Writer (H.264 in MP4, a hardware encoder when the system has one).

#include "vv_encode.h"

#include <windows.h>

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
        SlotState state = SlotState::Free;
    };

    int64_t MicrosecondsSince(std::chrono::steady_clock::time_point start)
    {
        return std::chrono::duration_cast<std::chrono::microseconds>(std::chrono::steady_clock::now() - start).count();
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
    int64_t firstQpc = -1;
    int64_t qpcFrequency = 1;

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
            output->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264);
            output->SetUINT32(MF_MT_AVG_BITRATE, static_cast<UINT32>(bitrate));
            output->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
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
            input->SetUINT32(MF_MT_DEFAULT_STRIDE, static_cast<UINT32>(width * 4));
            MFSetAttributeSize(input.Get(), MF_MT_FRAME_SIZE, static_cast<UINT32>(width), static_cast<UINT32>(height));
            MFSetAttributeRatio(input.Get(), MF_MT_FRAME_RATE, static_cast<UINT32>(fps), 1);
            MFSetAttributeRatio(input.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
            hr = writer->SetInputMediaType(stream, input.Get(), nullptr);
        }

        if (SUCCEEDED(hr))
        {
            hr = writer->BeginWriting();
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

            std::lock_guard<std::mutex> lock(mutex);
            slot.state = SlotState::Free;
            if (FAILED(write))
            {
                stats.last_error = write;
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

    int32_t vv_encode_open(const wchar_t* path, int32_t width, int32_t height, int32_t fps, int32_t bitrate, vv_encoder** encoder)
    {
        if (path == nullptr || encoder == nullptr || width <= 0 || height <= 0 || (width % 2) != 0 || (height % 2) != 0 || fps <= 0 || bitrate <= 0)
        {
            return VV_ENCODE_INVALID_ARGUMENT;
        }

        *encoder = nullptr;
        try
        {
            auto created = new vv_encoder();
            created->path = path;
            created->width = width;
            created->height = height;
            created->fps = fps;
            created->bitrate = bitrate;
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
            if (encoder->firstQpc < 0)
            {
                encoder->firstQpc = qpc;
            }

            // QPC ticks to the file's 100 ns units, from the first frame; split to keep the product in range.
            int64_t ticks = qpc - encoder->firstQpc;
            slot.time = ((ticks / encoder->qpcFrequency) * 10'000'000) + (((ticks % encoder->qpcFrequency) * 10'000'000) / encoder->qpcFrequency);
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
