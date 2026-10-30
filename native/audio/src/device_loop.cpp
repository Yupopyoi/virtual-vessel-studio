#include "device_loop.h"

#include "mixer.h"
#include "vv_audio.h"

#include <avrt.h>
#include <mmdeviceapi.h>
#include <mmreg.h>

using Microsoft::WRL::ComPtr;

namespace vv::audio
{
    namespace
    {
        constexpr DWORD RetryMilliseconds = 1000;

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

    HRESULT OpenSharedClient(const std::wstring& deviceId, REFERENCE_TIME bufferDuration, HANDLE events, ComPtr<IAudioClient>& client)
    {
        ComPtr<IMMDeviceEnumerator> enumerator;
        HRESULT hr = CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&enumerator));
        ComPtr<IMMDevice> device;
        if (SUCCEEDED(hr) && !enumerator)
        {
            hr = E_POINTER;
        }

        if (SUCCEEDED(hr))
        {
            hr = enumerator->GetDevice(deviceId.c_str(), &device);
        }

        if (SUCCEEDED(hr) && !device)
        {
            hr = E_NOTFOUND;
        }

        DWORD deviceState = 0;
        if (SUCCEEDED(hr) && SUCCEEDED(device->GetState(&deviceState)) && deviceState != DEVICE_STATE_ACTIVE)
        {
            hr = AUDCLNT_E_DEVICE_INVALIDATED;
        }

        if (SUCCEEDED(hr))
        {
            hr = device->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, reinterpret_cast<void**>(client.ReleaseAndGetAddressOf()));
        }

        if (SUCCEEDED(hr) && !client)
        {
            hr = E_POINTER;
        }

        // Windows converts the device's own rate and channels to and from 48 kHz float stereo.
        WAVEFORMATEXTENSIBLE format = FloatStereo();
        if (SUCCEEDED(hr))
        {
            hr = client->Initialize(
                AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                bufferDuration,
                0,
                reinterpret_cast<WAVEFORMATEX*>(&format),
                nullptr);
        }

        if (SUCCEEDED(hr))
        {
            hr = client->SetEventHandle(events);
        }

        return hr;
    }

    DeviceLoop::~DeviceLoop()
    {
        Stop();
    }

    void DeviceLoop::Start(Body body)
    {
        _wake = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        _thread = std::thread([this, body = std::move(body)] { Run(body); });
    }

    void DeviceLoop::Stop()
    {
        _stop = true;
        if (_wake != nullptr)
        {
            SetEvent(_wake);
        }

        if (_thread.joinable())
        {
            _thread.join();
        }

        if (_wake != nullptr)
        {
            CloseHandle(_wake);
            _wake = nullptr;
        }
    }

    void DeviceLoop::SetDevice(const std::wstring& deviceId)
    {
        {
            std::lock_guard<std::mutex> lock(_deviceLock);
            _deviceId = deviceId;
        }

        _version++;
        SetEvent(_wake);
    }

    void DeviceLoop::Running()
    {
        _state = VV_AUDIO_SOURCE_RUNNING;
        _problem = VV_AUDIO_PROBLEM_NONE;
        _lastError = 0;
    }

    void DeviceLoop::Classify(HRESULT hr, int& state, int& problem)
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

    void DeviceLoop::Run(const Body& body)
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
                WaitForSingleObject(_wake, INFINITE);
                continue;
            }

            _state = VV_AUDIO_SOURCE_STARTING;
            HRESULT hr = body(deviceId, version);
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
            WaitForSingleObject(_wake, RetryMilliseconds);
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
}
