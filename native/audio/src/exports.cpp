// The C API of vv_audio.h: the engine owns the mixer, a capture thread per source, and the mix thread.

#include "vv_audio.h"

#include "audio_file.h"
#include "bgm_crossfade.h"
#include "clip_source.h"
#include "sound_effects.h"
#include "device_output.h"
#include "device_source.h"
#include "denoiser.h"
#include "mixer.h"

#include <windows.h>

#include <avrt.h>
#include <mferror.h>
#include <mmdeviceapi.h>
#include <propsys.h>

// Defines the property keys here, in the one file that reads them.
#include <initguid.h>
#include <functiondiscoverykeys_devpkey.h>
#include <wrl/client.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <memory>
#include <mutex>
#include <new>
#include <string>
#include <thread>

using Microsoft::WRL::ComPtr;
using vv::audio::BlockFrames;
using vv::audio::DeviceOutput;
using vv::audio::DeviceSource;
using vv::audio::MaxReaders;
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

namespace
{
    // What a tap's context points at: the bus, and whose place on it.
    struct TapReader
    {
        Mixer* mixer = nullptr;
        int reader = 0;
    };
}

struct vv_audio_clip
{
    vv::audio::DecodedFile decoded;
};

struct vv_audio_engine
{
    explicit vv_audio_engine(int64_t frequency)
        : mixer(frequency), frequency(frequency), monitor(mixer)
    {
    }

    Mixer mixer;
    int64_t frequency;
    DeviceOutput monitor;
    TapReader readers[MaxReaders];
    vv::audio::Denoiser denoiser;
    std::mutex lock;
    std::unique_ptr<DeviceSource> sources[vv::audio::MaxSources];

    // The players (BGM and SE detailed design). The decks are made with the first BGM source; their threads stop
    // with the engine, after the mix. The crossfade's length is kept here, as it may be set before that.
    std::unique_ptr<vv::audio::BgmCrossfader> bgm;
    std::atomic<int> crossfadeFrames{ 0 };
    vv::audio::SoundEffects effects;
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

namespace
{
    // An audio file's failure as the API's code.
    int32_t FileError(HRESULT hr)
    {
        return hr == vv::audio::FileTooLong ? VV_AUDIO_TOO_LONG : hr == E_OUTOFMEMORY ? VV_AUDIO_OUT_OF_MEMORY : VV_AUDIO_UNREADABLE;
    }

    int32_t ListDevices(EDataFlow flow, vv_audio_device* devices, int32_t capacity, int32_t* count)
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
                hr = enumerator->EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE, &collection);
            }

            if (SUCCEEDED(hr) && !collection)
            {
                hr = E_POINTER;
            }

            std::wstring defaultId;
            ComPtr<IMMDevice> defaultDevice;
            if (SUCCEEDED(hr) && SUCCEEDED(enumerator->GetDefaultAudioEndpoint(flow, eConsole, &defaultDevice)) && defaultDevice)
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
}

extern "C"
{
    int32_t vv_audio_api_version(void)
    {
        return VV_AUDIO_API_VERSION;
    }

    int32_t vv_audio_list_inputs(vv_audio_device* devices, int32_t capacity, int32_t* count)
    {
        return ListDevices(eCapture, devices, capacity, count);
    }

    int32_t vv_audio_list_outputs(vv_audio_device* devices, int32_t capacity, int32_t* count)
    {
        return ListDevices(eRender, devices, capacity, count);
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

    int32_t vv_audio_denoise(vv_audio_engine* engine, float* samples, int32_t count)
    {
        if (engine == nullptr || samples == nullptr || count < 0 || count % VV_AUDIO_DENOISE_FRAMES != 0)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        return engine->denoiser.Process(samples, count) ? VV_AUDIO_OK : VV_AUDIO_OUT_OF_MEMORY;
    }

    int32_t vv_audio_set_denoise(vv_audio_engine* engine, int32_t source, int32_t enabled)
    {
        if (engine == nullptr)
        {
            return VV_AUDIO_NOT_FOUND;
        }

        std::lock_guard<std::mutex> guard(engine->lock);
        if (!engine->mixer.Used(source))
        {
            return VV_AUDIO_NOT_FOUND;
        }

        return engine->mixer.SetDenoise(source, enabled != 0) ? VV_AUDIO_OK : VV_AUDIO_OUT_OF_MEMORY;
    }

    int32_t vv_audio_set_stage(vv_audio_engine* engine, int32_t source, int32_t enabled)
    {
        if (engine == nullptr || !engine->mixer.Used(source))
        {
            return VV_AUDIO_NOT_FOUND;
        }

        engine->mixer.SetStage(source, enabled != 0);
        return VV_AUDIO_OK;
    }

    int32_t vv_audio_stage_read(vv_audio_engine* engine, int32_t source, float* frames, int32_t capacity, int32_t* read)
    {
        if (engine == nullptr || !engine->mixer.Used(source))
        {
            return VV_AUDIO_NOT_FOUND;
        }

        if (frames == nullptr || capacity < 0 || read == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        *read = static_cast<int32_t>(engine->mixer.ReadStage(source, frames, static_cast<size_t>(capacity)));
        return VV_AUDIO_OK;
    }

    int32_t vv_audio_stage_write(vv_audio_engine* engine, int32_t source, const float* frames, int32_t count)
    {
        if (engine == nullptr || !engine->mixer.Used(source))
        {
            return VV_AUDIO_NOT_FOUND;
        }

        if (frames == nullptr || count < 0)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        engine->mixer.WriteStage(source, frames, static_cast<size_t>(count));
        return VV_AUDIO_OK;
    }

    int32_t vv_audio_get_stage_delay(vv_audio_engine* engine, int32_t source, vv_audio_stage_delay* delay)
    {
        if (engine == nullptr || delay == nullptr || !engine->mixer.Used(source) || !engine->sources[source])
        {
            return VV_AUDIO_NOT_FOUND;
        }

        vv::audio::StageDelay rings = engine->mixer.GetStageDelay(source);
        delay->capture_device = static_cast<float>(engine->sources[source]->DeviceLatency());
        delay->stage_input = rings.stageInput;
        delay->stage_output = rings.stageOutput;
        delay->monitor = rings.monitor;
        delay->output_buffer = engine->monitor.BufferWait();
        delay->output_device = static_cast<float>(engine->monitor.DeviceLatency());
        return VV_AUDIO_OK;
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

        if (bus != VV_AUDIO_BUS_STREAM && bus != VV_AUDIO_BUS_MONITOR)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        engine->mixer.SetSend(source, bus, send_db);
        return VV_AUDIO_OK;
    }

    int32_t vv_audio_get_source_status(vv_audio_engine* engine, int32_t source, vv_audio_source_status* status)
    {
        if (engine == nullptr || status == nullptr || !engine->mixer.Used(source))
        {
            return VV_AUDIO_NOT_FOUND;
        }

        // A player has no device to open or lose: it is always running, playing or not.
        vv::audio::SourceCounters counters = engine->mixer.TakeCounters(source);
        if (engine->sources[source])
        {
            const vv::audio::DeviceLoop& device = engine->sources[source]->Loop();
            status->state = device.State();
            status->problem = device.Problem();
            status->last_error = device.LastError();
        }
        else
        {
            status->state = VV_AUDIO_SOURCE_RUNNING;
            status->problem = VV_AUDIO_PROBLEM_NONE;
            status->last_error = 0;
        }

        status->peak_db = vv::audio::LinearToDb(counters.peak);
        status->overruns = counters.overruns;
        status->underruns = counters.underruns;
        status->drift_corrections = counters.driftCorrections;
        return VV_AUDIO_OK;
    }

    int32_t vv_audio_set_monitor_device(vv_audio_engine* engine, const wchar_t* device_id)
    {
        if (engine == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        try
        {
            engine->monitor.SetDevice(device_id != nullptr ? device_id : L"");
            return VV_AUDIO_OK;
        }
        catch (...)
        {
            return VV_AUDIO_OUT_OF_MEMORY;
        }
    }

    int32_t vv_audio_set_monitor_gain(vv_audio_engine* engine, float gain_db, int32_t muted)
    {
        if (engine == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        engine->mixer.SetMonitor(gain_db, muted != 0);
        return VV_AUDIO_OK;
    }

    int32_t vv_audio_get_monitor_status(vv_audio_engine* engine, vv_audio_monitor_status* status)
    {
        if (engine == nullptr || status == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        const vv::audio::DeviceLoop& device = engine->monitor.Loop();
        status->state = device.State();
        status->problem = device.Problem();
        status->last_error = device.LastError();
        status->peak_db = vv::audio::LinearToDb(engine->mixer.TakeMonitorPeak());
        status->underruns = engine->monitor.Underruns();
        status->drift_corrections = engine->monitor.DriftCorrections();
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

    int32_t vv_audio_set_stream_delay(vv_audio_engine* engine, int32_t delay_ms)
    {
        if (engine == nullptr || delay_ms < 0 || delay_ms > VV_AUDIO_MAX_STREAM_DELAY_MS)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        engine->mixer.SetStreamDelay((static_cast<int64_t>(delay_ms) * engine->frequency) / 1000);
        return VV_AUDIO_OK;
    }

    int32_t vv_audio_decode_file(const wchar_t* path, vv_audio_clip** clip, int32_t* rate, int64_t* count, int32_t* error)
    {
        if (path == nullptr || clip == nullptr || rate == nullptr || count == nullptr || error == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        *clip = nullptr;
        *rate = 0;
        *count = 0;
        *error = 0;
        auto created = new (std::nothrow) vv_audio_clip();
        if (created == nullptr)
        {
            return VV_AUDIO_OUT_OF_MEMORY;
        }

        HRESULT hr = vv::audio::DecodeFile(path, created->decoded);
        if (FAILED(hr))
        {
            delete created;
            *error = static_cast<int32_t>(hr);
            return hr == vv::audio::FileTooLong ? VV_AUDIO_TOO_LONG : hr == E_OUTOFMEMORY ? VV_AUDIO_OUT_OF_MEMORY : VV_AUDIO_UNREADABLE;
        }

        *clip = created;
        *rate = created->decoded.rate;
        *count = static_cast<int64_t>(created->decoded.samples.size());
        return VV_AUDIO_OK;
    }

    int32_t vv_audio_clip_read(const vv_audio_clip* clip, float* samples, int64_t capacity)
    {
        if (clip == nullptr || capacity < 0 || (samples == nullptr && capacity > 0))
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        const std::vector<float>& decoded = clip->decoded.samples;
        std::copy_n(decoded.begin(), std::min(static_cast<size_t>(capacity), decoded.size()), samples);
        return VV_AUDIO_OK;
    }

    void vv_audio_clip_free(vv_audio_clip* clip)
    {
        delete clip;
    }

    int32_t vv_audio_preview_play(vv_audio_engine* engine, const float* samples, int64_t count, int32_t rate)
    {
        if (engine == nullptr || samples == nullptr || count <= 0 || rate <= 0 || count > static_cast<int64_t>(rate) * VV_AUDIO_MAX_FILE_SECONDS)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        try
        {
            // Resampled before taking the lock, so the mix waits only for the swap.
            std::vector<float> resampled = vv::audio::ResampleLinear(samples, static_cast<size_t>(count), rate, vv::audio::SampleRate);
            std::lock_guard<std::mutex> guard(engine->lock);
            engine->mixer.PlayPreview(std::move(resampled));
            return VV_AUDIO_OK;
        }
        catch (const std::bad_alloc&)
        {
            return VV_AUDIO_OUT_OF_MEMORY;
        }
    }

    int32_t vv_audio_preview_stop(vv_audio_engine* engine)
    {
        if (engine == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        std::lock_guard<std::mutex> guard(engine->lock);
        engine->mixer.StopPreview();
        return VV_AUDIO_OK;
    }

    int32_t vv_audio_preview_position(vv_audio_engine* engine, int64_t* played, int64_t* length)
    {
        if (engine == nullptr || played == nullptr || length == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        vv::audio::PreviewPosition position = engine->mixer.GetPreviewPosition();
        *played = position.played;
        *length = position.length;
        return VV_AUDIO_OK;
    }

    int32_t vv_audio_get_stream_tap(vv_audio_engine* engine, int32_t reader, vv_audio_stream_tap* tap)
    {
        if (engine == nullptr || tap == nullptr || reader < 0 || reader >= MaxReaders)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        engine->readers[reader] = TapReader{ &engine->mixer, reader };
        tap->context = &engine->readers[reader];
        tap->read = [](void* context, float* out, int32_t frames, int64_t until_qpc, int64_t* first_qpc) -> int32_t
        {
            auto tapReader = static_cast<TapReader*>(context);
            return tapReader->mixer->ReadStream(out, frames, until_qpc, first_qpc, tapReader->reader);
        };
        return VV_AUDIO_OK;
    }

    int32_t vv_audio_add_player_source(vv_audio_engine* engine, int32_t player, int32_t* source)
    {
        if (engine == nullptr || source == nullptr || (player != VV_AUDIO_PLAYER_BGM && player != VV_AUDIO_PLAYER_SOUND_EFFECTS))
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        try
        {
            // Made outside the lock (it starts the decks' threads), then put in place under it, where the crossfade's
            // setter looks for it.
            std::unique_ptr<vv::audio::BgmCrossfader> decks;
            if (player == VV_AUDIO_PLAYER_BGM && !engine->bgm)
            {
                decks = std::make_unique<vv::audio::BgmCrossfader>();
            }

            std::lock_guard<std::mutex> guard(engine->lock);
            if (decks)
            {
                engine->bgm = std::move(decks);
                engine->bgm->SetCrossfade(engine->crossfadeFrames.load());
            }

            int id = -1;
            if (!engine->mixer.Add(&id))
            {
                return VV_AUDIO_OUT_OF_MEMORY;
            }

            vv::audio::BlockProvider* provider = player == VV_AUDIO_PLAYER_BGM ? static_cast<vv::audio::BlockProvider*>(engine->bgm.get()) : &engine->effects;
            engine->mixer.SetProvider(id, provider);
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

    int32_t vv_audio_probe_file(const wchar_t* path, vv_audio_file_info* info, int32_t* error)
    {
        if (path == nullptr || info == nullptr || error == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        *info = {};
        *error = 0;
        HRESULT hr = S_OK;
        std::unique_ptr<vv::audio::ClipSource> source = vv::audio::OpenMediaFile(path, hr);
        if (source)
        {
            // Opening reads the header only: the first frames are read too, so a file that cannot be decoded fails here.
            std::vector<float> first;
            hr = source->Read(first);
            if (SUCCEEDED(hr) && first.empty())
            {
                hr = MF_E_INVALIDMEDIATYPE;
            }
        }

        if (FAILED(hr))
        {
            *error = static_cast<int32_t>(hr);
            return FileError(hr);
        }

        vv::audio::ClipFormat format = source->Format();
        info->rate = format.rate;
        info->channels = format.channels;
        info->frames = format.rate > 0 ? (format.frames * vv::audio::SampleRate) / format.rate : 0;
        return VV_AUDIO_OK;
    }

    int32_t vv_audio_bgm_play(vv_audio_engine* engine, const wchar_t* path, int32_t loop, float gain_db, int64_t* generation)
    {
        if (engine == nullptr || path == nullptr || generation == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        if (!engine->bgm)
        {
            return VV_AUDIO_NOT_FOUND;
        }

        try
        {
            *generation = engine->bgm->Play(path, loop != 0, gain_db);
            return VV_AUDIO_OK;
        }
        catch (const std::bad_alloc&)
        {
            return VV_AUDIO_OUT_OF_MEMORY;
        }
    }

    int32_t vv_audio_bgm_stop(vv_audio_engine* engine)
    {
        if (engine == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        if (engine->bgm)
        {
            engine->bgm->Stop();
        }

        return VV_AUDIO_OK;
    }

    int32_t vv_audio_bgm_set_gain(vv_audio_engine* engine, float gain_db)
    {
        if (engine == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        if (engine->bgm)
        {
            engine->bgm->SetGain(gain_db);
        }

        return VV_AUDIO_OK;
    }

    int32_t vv_audio_bgm_set_crossfade(vv_audio_engine* engine, int32_t milliseconds)
    {
        if (engine == nullptr || milliseconds < 0)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        int frames = static_cast<int>(std::min<int64_t>((static_cast<int64_t>(milliseconds) * vv::audio::SampleRate) / 1000, vv::audio::BgmCrossfader::MaxCrossfadeFrames));
        engine->crossfadeFrames = frames;
        std::lock_guard<std::mutex> guard(engine->lock);
        if (engine->bgm)
        {
            engine->bgm->SetCrossfade(frames);
        }

        return VV_AUDIO_OK;
    }

    int32_t vv_audio_set_ducking(vv_audio_engine* engine, int32_t key_source, int32_t ducked_source, int32_t enabled, float depth_db, float threshold_db)
    {
        if (engine == nullptr || std::isnan(depth_db) || std::isnan(threshold_db))
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        if (enabled != 0 && (!engine->mixer.Used(key_source) || !engine->mixer.Used(ducked_source)))
        {
            return VV_AUDIO_NOT_FOUND;
        }

        engine->mixer.SetDucking(key_source, ducked_source, enabled != 0, depth_db, threshold_db);
        return VV_AUDIO_OK;
    }

    int32_t vv_audio_get_ducking(vv_audio_engine* engine, float* gain_db)
    {
        if (engine == nullptr || gain_db == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        *gain_db = engine->mixer.DuckGainDb();
        return VV_AUDIO_OK;
    }

    int32_t vv_audio_bgm_get_status(vv_audio_engine* engine, vv_audio_bgm_status* status)
    {
        if (engine == nullptr || status == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        *status = {};
        if (engine->bgm)
        {
            vv::audio::BgmStatus value = engine->bgm->Status();
            status->state = static_cast<int32_t>(value.state);
            status->error = value.error;
            status->generation = value.generation;
            status->finished_generation = value.finishedGeneration;
            status->played_frames = value.playedFrames;
            status->length_frames = value.lengthFrames;
            status->underruns = value.underruns;
        }

        return VV_AUDIO_OK;
    }

    int32_t vv_audio_effect_load(vv_audio_engine* engine, const wchar_t* path, int32_t* effect, int64_t* frames, int32_t* error)
    {
        if (engine == nullptr || path == nullptr || effect == nullptr || frames == nullptr || error == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        *effect = -1;
        *frames = 0;
        *error = 0;
        try
        {
            // Decoded before taking the lock, so the mix waits only for the effect to be put in place.
            auto decoded = std::make_shared<std::vector<float>>();
            HRESULT hr = vv::audio::DecodeStereo(path, VV_AUDIO_MAX_EFFECT_SECONDS, *decoded);
            if (FAILED(hr))
            {
                *error = static_cast<int32_t>(hr);
                return FileError(hr);
            }

            int64_t count = static_cast<int64_t>(decoded->size() / vv::audio::Channels);
            std::lock_guard<std::mutex> guard(engine->lock);
            int id = -1;
            if (!engine->effects.Load(std::move(decoded), &id))
            {
                return VV_AUDIO_OUT_OF_MEMORY;
            }

            *effect = id;
            *frames = count;
            return VV_AUDIO_OK;
        }
        catch (const std::bad_alloc&)
        {
            return VV_AUDIO_OUT_OF_MEMORY;
        }
    }

    int32_t vv_audio_effect_unload(vv_audio_engine* engine, int32_t effect)
    {
        if (engine == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        std::shared_ptr<const std::vector<float>> released;
        {
            std::lock_guard<std::mutex> guard(engine->lock);
            released = engine->effects.Unload(effect);
        }

        // Freed here, outside the lock, so the mix does not wait for it.
        return released ? VV_AUDIO_OK : VV_AUDIO_NOT_FOUND;
    }

    int32_t vv_audio_effect_play(vv_audio_engine* engine, int32_t effect, float gain_db)
    {
        if (engine == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        std::lock_guard<std::mutex> guard(engine->lock);
        return engine->effects.Play(effect, gain_db) ? VV_AUDIO_OK : VV_AUDIO_NOT_FOUND;
    }

    int32_t vv_audio_effect_stop_all(vv_audio_engine* engine)
    {
        if (engine == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        std::lock_guard<std::mutex> guard(engine->lock);
        engine->effects.StopAll();
        return VV_AUDIO_OK;
    }

    int32_t vv_audio_effect_stolen(vv_audio_engine* engine, int64_t* stolen)
    {
        if (engine == nullptr || stolen == nullptr)
        {
            return VV_AUDIO_INVALID_ARGUMENT;
        }

        std::lock_guard<std::mutex> guard(engine->lock);
        *stolen = engine->effects.Stolen();
        return VV_AUDIO_OK;
    }
}
