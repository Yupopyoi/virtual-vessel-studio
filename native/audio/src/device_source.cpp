#include "device_source.h"

#include <audioclient.h>

using Microsoft::WRL::ComPtr;

namespace vv::audio
{
    namespace
    {
        // The device's buffer: a few of its 10 ms periods, enough to ride out a late wake of the thread.
        constexpr REFERENCE_TIME BufferDuration = 500'000; // 50 ms in 100 ns units
        constexpr DWORD PacketWaitMilliseconds = 200;
    }

    DeviceSource::DeviceSource(Mixer& mixer, int id)
        : _mixer(mixer), _id(id)
    {
        _loop.Start([this](const std::wstring& deviceId, uint64_t version) { return Capture(deviceId, version); });
    }

    DeviceSource::~DeviceSource()
    {
        _loop.Stop();
    }

    // Opens the device and captures until it fails, the device changes, or the source stops. Returns why it ended.
    HRESULT DeviceSource::Capture(const std::wstring& deviceId, uint64_t version)
    {
        HANDLE packets = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        ComPtr<IAudioClient> client;
        HRESULT hr = OpenSharedClient(deviceId, BufferDuration, packets, client);
        ComPtr<IAudioCaptureClient> capture;
        if (SUCCEEDED(hr))
        {
            hr = client->GetService(IID_PPV_ARGS(&capture));
        }

        if (SUCCEEDED(hr) && !capture)
        {
            hr = E_POINTER;
        }

        if (SUCCEEDED(hr))
        {
            hr = client->Start();
        }

        if (SUCCEEDED(hr))
        {
            _loop.Running();
        }

        HANDLE waits[] = { packets, _loop.Wake() };
        while (SUCCEEDED(hr) && _loop.Runs(version))
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
