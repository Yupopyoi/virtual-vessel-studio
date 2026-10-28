#include "device_source.h"

#include "vv_audio.h"

#include <audioclient.h>
#include <avrt.h>
#include <mmdeviceapi.h>
#include <mmreg.h>
#include <wrl/client.h>

#include <vector>

using Microsoft::WRL::ComPtr;

namespace vv::audio
{
    namespace
    {
        // The device's buffer: a few of its 10 ms periods, enough to ride out a late wake of the thread.
        constexpr REFERENCE_TIME BufferDuration = 500'000; // 50 ms in 100 ns units
        constexpr DWORD RetryMilliseconds = 1000;
        constexpr DWORD PacketWaitMilliseconds = 200;

        WAVEFORMATEXTENSIBLE FloatStereo()
        {
            WAVEFORMATEXTENSIBLE format = {};
            format.Format.wFormatTag = WAVE_FORMAT_EXTENSIBLE;
            format.Format.nChannels = Channels;
            format.Format.nSamplesPerSec = SampleRate;
            format.Format.wBitsPerSample = 32;
            format.Format.nBlockAlign = Channels * sizeof(float);
            format.Format.nAvgBytesPerSec = SampleRate * format.Format.nBlockAlign;
            format.Format.cbSize = sizeof(WAVEFORMATEXTENSIBLE) - sizeof(WAVEFORMATEX);
            format.Samples.wValidBitsPerSample = 32;
            format.dwChannelMask = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT;
            format.SubFormat = KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
            return format;
        }
    }

    DeviceSource::DeviceSource(Mixer& mixer, int id)
        : _mixer(mixer), _id(id), _state(VV_AUDIO_SOURCE_OFF), _problem(VV_AUDIO_PROBLEM_NONE)
    {
        _wake = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        _thread = std::thread([this] { Run(); });
    }

    DeviceSource::~DeviceSource()
    {
        _stop = true;
        SetEvent(_wake);
        if (_thread.joinable())
        {
            _thread.join();
        }

        CloseHandle(_wake);
    }

    void DeviceSource::SetDevice(const std::wstring& deviceId)
    {
        {
            std::lock_guard<std::mutex> lock(_deviceLock);
            _deviceId = deviceId;
        }

        _version++;
        SetEvent(_wake);
    }

    void DeviceSource::Classify(HRESULT hr, int& state, int& problem)
    {
        if (hr == AUDCLNT_E_DEVICE_INVALIDATED || hr == HRESULT_FROM_WIN32(ERROR_NOT_FOUND) || hr == E_NOTFOUND || hr == AUDCLNT_E_ENDPOINT_CREATE_FAILED)
        {
            state = VV_AUDIO_SOURCE_UNAVAILABLE;
            problem = VV_AUDIO_PROBLEM_NONE;
        }
        else if (hr == AUDCLNT_E_DEVICE_IN_USE)
        {
            state = VV_AUDIO_SOURCE_ERROR;
            problem = VV_AUDIO_PROBLEM_IN_USE;
        }
        else if (hr == E_ACCESSDENIED)
        {
            state = VV_AUDIO_SOURCE_ERROR;
            problem = VV_AUDIO_PROBLEM_ACCESS_DENIED;
        }
        else
        {
            state = VV_AUDIO_SOURCE_ERROR;
            problem = VV_AUDIO_PROBLEM_FAILED;
        }
    }

    bool DeviceSource::WaitForChange(DWORD milliseconds)
    {
        WaitForSingleObject(_wake, milliseconds);
        return !_stop.load();
    }

    void DeviceSource::Run()
    {
        HRESULT com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        DWORD taskIndex = 0;
        HANDLE task = AvSetMmThreadCharacteristicsW(L"Audio", &taskIndex);
        while (!_stop.load())
        {
            uint64_t version = _version.load();
            std::wstring deviceId;
            {
                std::lock_guard<std::mutex> lock(_deviceLock);
                deviceId = _deviceId;
            }

            if (deviceId.empty())
            {
                _state = VV_AUDIO_SOURCE_OFF;
                _problem = VV_AUDIO_PROBLEM_NONE;
                WaitForChange(INFINITE);
                continue;
            }

            _state = VV_AUDIO_SOURCE_STARTING;
            HRESULT hr = Capture(deviceId, version);
            if (_stop.load() || _version.load() != version)
            {
                continue;
            }

            // Unplugged, missing, or busy: silence in its place, and the device is tried again.
            int state = VV_AUDIO_SOURCE_ERROR;
            int problem = VV_AUDIO_PROBLEM_FAILED;
            Classify(hr, state, problem);
            _lastError = static_cast<int>(hr);
            _state = state;
            _problem = problem;
            WaitForChange(RetryMilliseconds);
        }

        if (task != nullptr)
        {
            AvRevertMmThreadCharacteristics(task);
        }

        if (SUCCEEDED(com))
        {
            CoUninitialize();
        }
    }

    // Opens the device and captures until it fails, the device changes, or the source stops. Returns why it ended.
    HRESULT DeviceSource::Capture(const std::wstring& deviceId, uint64_t version)
    {
        ComPtr<IMMDeviceEnumerator> enumerator;
        HRESULT hr = CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&enumerator));
        ComPtr<IMMDevice> device;
        if (SUCCEEDED(hr))
        {
            hr = enumerator->GetDevice(deviceId.c_str(), &device);
        }

        DWORD deviceState = 0;
        if (SUCCEEDED(hr) && SUCCEEDED(device->GetState(&deviceState)) && deviceState != DEVICE_STATE_ACTIVE)
        {
            hr = AUDCLNT_E_DEVICE_INVALIDATED;
        }

        ComPtr<IAudioClient> client;
        if (SUCCEEDED(hr))
        {
            hr = device->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, reinterpret_cast<void**>(client.GetAddressOf()));
        }

        // Windows converts the device's own rate and channels to 48 kHz float stereo.
        WAVEFORMATEXTENSIBLE format = FloatStereo();
        if (SUCCEEDED(hr))
        {
            hr = client->Initialize(
                AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                BufferDuration,
                0,
                reinterpret_cast<WAVEFORMATEX*>(&format),
                nullptr);
        }

        HANDLE packets = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        if (SUCCEEDED(hr))
        {
            hr = client->SetEventHandle(packets);
        }

        ComPtr<IAudioCaptureClient> capture;
        if (SUCCEEDED(hr))
        {
            hr = client->GetService(IID_PPV_ARGS(&capture));
        }

        if (SUCCEEDED(hr))
        {
            hr = client->Start();
        }

        if (SUCCEEDED(hr))
        {
            _state = VV_AUDIO_SOURCE_RUNNING;
            _problem = VV_AUDIO_PROBLEM_NONE;
            _lastError = 0;
        }

        HANDLE waits[] = { packets, _wake };
        while (SUCCEEDED(hr) && !_stop.load() && _version.load() == version)
        {
            WaitForMultipleObjects(2, waits, FALSE, PacketWaitMilliseconds);
            UINT32 next = 0;
            while (SUCCEEDED(hr = capture->GetNextPacketSize(&next)) && next > 0)
            {
                BYTE* data = nullptr;
                UINT32 frames = 0;
                DWORD flags = 0;
                hr = capture->GetBuffer(&data, &frames, &flags, nullptr, nullptr);
                if (FAILED(hr))
                {
                    break;
                }

                // A silent packet carries no samples worth reading; it is written as silence.
                bool silent = (flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0;
                _mixer.Write(_id, silent ? nullptr : reinterpret_cast<const float*>(data), frames);
                hr = capture->ReleaseBuffer(frames);
            }
        }

        if (client)
        {
            client->Stop();
        }

        CloseHandle(packets);
        return hr;
    }
}
