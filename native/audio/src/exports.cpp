// The C API of vv_audio.h: the engine owns the mixer, a capture thread per source, and the mix thread.

#include "vv_audio.h"

#include "device_source.h"
#include "mixer.h"

#include <windows.h>

#include <avrt.h>
#include <mmdeviceapi.h>
#include <propsys.h>

// Defines the property keys here, in the one file that reads them.
#include <initguid.h>
#include <functiondiscoverykeys_devpkey.h>
#include <wrl/client.h>

#include <algorithm>
#include <atomic>
#include <memory>
#include <mutex>
#include <new>
#include <string>
#include <thread>

using Microsoft::WRL::ComPtr;
using vv::audio::BlockFrames;
using vv::audio::DeviceSource;
using vv::audio::Mixer;

namespace
{
    // How far the stream bus is behind the sound reaching the device: the source's prime (20 ms) and
    // about one device period (10 ms) (audio detailed design 8.4, 15).
    constexpr int64_t StreamLatencyMicroseconds = 30'000;

    int64_t QpcFrequency()
    {
        LARGE_INTEGER frequency;
        QueryPerformanceFrequency(&frequency);
        return frequency.QuadPart;
    }

    int64_t QpcNow()
    {
        LARGE_INTEGER now;
        QueryPerformanceCounter(&now);
        return now.QuadPart;
    }

    void CopyText(wchar_t* target, size_t capacity, const wchar_t* text)
    {
        wcsncpy_s(target, capacity, text != nullptr ? text : L"", _TRUNCATE);
    }
}

struct vv_audio_engine
{
    explicit vv_audio_engine(int64_t frequency)
        : mixer(frequency), frequency(frequency)
    {
    }

    Mixer mixer;
    int64_t frequency;
    std::mutex lock;
    std::unique_ptr<DeviceSource> sources[vv::audio::MaxSources];
    std::atomic<bool> stop{ false };
    std::thread mixThread;

    // Mixes a block every 10 ms of the QPC clock. Blocks are counted from the start, not from each wake,
    // so a late wake mixes the blocks it missed and the bus keeps time with the video.
    void Mix()
    {
        DWORD taskIndex = 0;
        HANDLE task = AvSetMmThreadCharacteristicsW(L"Audio", &taskIndex);
        HANDLE timer = CreateWaitableTimerExW(nullptr, nullptr, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
        const int64_t start = QpcNow();
        const int64_t latencyTicks = (StreamLatencyMicroseconds * frequency) / 1'000'000;
        uint64_t mixed = 0;
        while (!stop.load())
        {
            int64_t now = QpcNow();
            int caughtUp = 0;
            while (Mixer::FrameQpc(start, (mixed + 1) * BlockFrames, frequency) <= now && caughtUp < 10)
            {
                std::lock_guard<std::mutex> guard(lock);
                mixer.MixBlock(Mixer::FrameQpc(start - latencyTicks, mixed * BlockFrames, frequency));
                mixed++;
                caughtUp++;
            }

            // After a long stall, the blocks it would take too long to catch up are skipped.
            if (caughtUp == 10)
            {
                uint64_t due = static_cast<uint64_t>(((now - start) * vv::audio::SampleRate) / frequency) / BlockFrames;
                mixed = due > mixed ? due : mixed;
            }

            int64_t next = Mixer::FrameQpc(start, (mixed + 1) * BlockFrames, frequency);
            int64_t waitTicks = next - QpcNow();
            LARGE_INTEGER due;
            due.QuadPart = waitTicks > 0 ? -((waitTicks * 10'000'000) / frequency) : -1;
            if (timer != nullptr && SetWaitableTimer(timer, &due, 0, nullptr, nullptr, FALSE))
            {
                WaitForSingleObject(timer, 50);
            }
            else
            {
                Sleep(1);
            }
        }

        if (timer != nullptr)
        {
            CloseHandle(timer);
        }

        if (task != nullptr)
        {
            AvRevertMmThreadCharacteristics(task);
        }
    }
};

extern "C"
{
    int32_t vv_audio_api_version(void)
    {
        return VV_AUDIO_API_VERSION;
    }

    int32_t vv_audio_list_inputs(vv_audio_device* devices, int32_t capacity, int32_t* count)
    {
        if (count == nullptr || capacity < 0 || (devices == nullptr && capacity > 0))
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        *count = 0;
        HRESULT com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        HRESULT hr = S_OK;

        // The COM objects are released in this block, before COM is uninitialized below; released after it,
        // they crashed on a system where this was the last use of COM (CI's runner).
        {
            ComPtr<IMMDeviceEnumerator> enumerator;
            hr = CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&enumerator));
            ComPtr<IMMDeviceCollection> collection;
            // Without an audio service (a server), calls can succeed and still give nothing: every result is checked.
            if (SUCCEEDED(hr) && !enumerator)
            {
                hr = E_POINTER;
            }

            if (SUCCEEDED(hr))
            {
                hr = enumerator->EnumAudioEndpoints(eCapture, DEVICE_STATE_ACTIVE, &collection);
            }

            if (SUCCEEDED(hr) && !collection)
            {
                hr = E_POINTER;
            }

            std::wstring defaultId;
            ComPtr<IMMDevice> defaultDevice;
            if (SUCCEEDED(hr) && SUCCEEDED(enumerator->GetDefaultAudioEndpoint(eCapture, eConsole, &defaultDevice)) && defaultDevice)
            {
                LPWSTR id = nullptr;
                if (SUCCEEDED(defaultDevice->GetId(&id)) && id != nullptr)
                {
                    defaultId = id;
                }

                CoTaskMemFree(id);
            }

            UINT total = 0;
            if (SUCCEEDED(hr))
            {
                hr = collection->GetCount(&total);
            }

            for (UINT i = 0; SUCCEEDED(hr) && i < total; i++)
            {
                ComPtr<IMMDevice> device;
                LPWSTR id = nullptr;
                if (FAILED(collection->Item(i, &device)) || !device || FAILED(device->GetId(&id)) || id == nullptr)
                {
                    CoTaskMemFree(id);
                    continue;
                }

                if (*count < capacity)
                {
                    vv_audio_device& entry = devices[*count];
                    CopyText(entry.id, _countof(entry.id), id);
                    entry.is_default = defaultId == id ? 1 : 0;
                    CopyText(entry.name, _countof(entry.name), id);
                    ComPtr<IPropertyStore> properties;
                    PROPVARIANT name;
                    PropVariantInit(&name);
                    if (SUCCEEDED(device->OpenPropertyStore(STGM_READ, &properties)) && properties && SUCCEEDED(properties->GetValue(PKEY_Device_FriendlyName, &name)) && name.vt == VT_LPWSTR && name.pwszVal != nullptr)
                    {
                        CopyText(entry.name, _countof(entry.name), name.pwszVal);
                    }

                    PropVariantClear(&name);
                }

                CoTaskMemFree(id);
                (*count)++;
            }
        }

        if (SUCCEEDED(com))
        {
            CoUninitialize();
        }

        return SUCCEEDED(hr) ? VV_AUDIO_OK : VV_AUDIO_SYSTEM_ERROR;
    }

    int32_t vv_audio_create(vv_audio_engine** engine)
    {
        if (engine == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        *engine = nullptr;
        try
        {
            auto created = new vv_audio_engine(QpcFrequency());
            created->mixThread = std::thread([created] { created->Mix(); });
            *engine = created;
            return VV_AUDIO_OK;
        }
        catch (const std::bad_alloc&)
        {
            return VV_AUDIO_OUT_OF_MEMORY;
        }
        catch (...)
        {
            return VV_AUDIO_SYSTEM_ERROR;
        }
    }

    void vv_audio_destroy(vv_audio_engine* engine)
    {
        if (engine == nullptr)
        {
            return;
        }

        engine->stop = true;
        if (engine->mixThread.joinable())
        {
            engine->mixThread.join();
        }

        for (auto& source : engine->sources)
        {
            source.reset();
        }

        delete engine;
    }

    int32_t vv_audio_add_source(vv_audio_engine* engine, int32_t* source)
    {
        if (engine == nullptr || source == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        try
        {
            int id = -1;
            {
                std::lock_guard<std::mutex> guard(engine->lock);
                if (!engine->mixer.Add(&id))
                {
                    return VV_AUDIO_OUT_OF_MEMORY;
                }
            }

            engine->sources[id] = std::make_unique<DeviceSource>(engine->mixer, id);
            *source = id;
            return VV_AUDIO_OK;
        }
        catch (const std::bad_alloc&)
        {
            return VV_AUDIO_OUT_OF_MEMORY;
        }
        catch (...)
        {
            return VV_AUDIO_SYSTEM_ERROR;
        }
    }

    int32_t vv_audio_remove_source(vv_audio_engine* engine, int32_t source)
    {
        if (engine == nullptr || !engine->mixer.Used(source))
        {
            return VV_AUDIO_NOT_FOUND;
        }

        // The capture thread stops before the source's input goes away.
        engine->sources[source].reset();
        std::lock_guard<std::mutex> guard(engine->lock);
        engine->mixer.Remove(source);
        return VV_AUDIO_OK;
    }

    int32_t vv_audio_set_device(vv_audio_engine* engine, int32_t source, const wchar_t* device_id)
    {
        if (engine == nullptr || !engine->mixer.Used(source) || !engine->sources[source])
        {
            return VV_AUDIO_NOT_FOUND;
        }

        try
        {
            engine->sources[source]->SetDevice(device_id != nullptr ? device_id : L"");
            return VV_AUDIO_OK;
        }
        catch (...)
        {
            return VV_AUDIO_OUT_OF_MEMORY;
        }
    }

    int32_t vv_audio_set_gain(vv_audio_engine* engine, int32_t source, float gain_db, int32_t muted)
    {
        if (engine == nullptr || !engine->mixer.Used(source))
        {
            return VV_AUDIO_NOT_FOUND;
        }

        engine->mixer.SetGain(source, gain_db, muted != 0);
        return VV_AUDIO_OK;
    }

    int32_t vv_audio_set_send(vv_audio_engine* engine, int32_t source, int32_t bus, float send_db)
    {
        if (engine == nullptr || !engine->mixer.Used(source))
        {
            return VV_AUDIO_NOT_FOUND;
        }

        if (bus != VV_AUDIO_BUS_STREAM)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        engine->mixer.SetSend(source, bus, send_db);
        return VV_AUDIO_OK;
    }

    int32_t vv_audio_get_source_status(vv_audio_engine* engine, int32_t source, vv_audio_source_status* status)
    {
        if (engine == nullptr || status == nullptr || !engine->mixer.Used(source) || !engine->sources[source])
        {
            return VV_AUDIO_NOT_FOUND;
        }

        const DeviceSource& device = *engine->sources[source];
        vv::audio::SourceCounters counters = engine->mixer.TakeCounters(source);
        status->state = device.State();
        status->problem = device.Problem();
        status->last_error = device.LastError();
        status->peak_db = vv::audio::LinearToDb(counters.peak);
        status->overruns = counters.overruns;
        status->underruns = counters.underruns;
        status->drift_corrections = counters.driftCorrections;
        return VV_AUDIO_OK;
    }

    int32_t vv_audio_get_bus_peak(vv_audio_engine* engine, int32_t bus, float* peak_db)
    {
        if (engine == nullptr || peak_db == nullptr || bus != VV_AUDIO_BUS_STREAM)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        *peak_db = vv::audio::LinearToDb(engine->mixer.TakeBusPeak());
        return VV_AUDIO_OK;
    }

    int64_t vv_audio_stream_latency_us(vv_audio_engine* engine)
    {
        return engine != nullptr ? StreamLatencyMicroseconds : 0;
    }

    int32_t vv_audio_get_stream_tap(vv_audio_engine* engine, vv_audio_stream_tap* tap)
    {
        if (engine == nullptr || tap == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        tap->context = &engine->mixer;
        tap->read = [](void* context, float* out, int32_t frames, int64_t until_qpc, int64_t* first_qpc) -> int32_t
        {
            return static_cast<Mixer*>(context)->ReadStream(out, frames, until_qpc, first_qpc);
        };
        return VV_AUDIO_OK;
    }
}
