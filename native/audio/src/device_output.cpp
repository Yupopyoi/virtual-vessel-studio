#include "device_output.h"

#include <cstring>

using Microsoft::WRL::ComPtr;

namespace vv::audio
{
    namespace
    {
        // The device's buffer: long enough to ride out a late wake of the thread, short enough that the user
        // hears the game close to when it happens.
        constexpr REFERENCE_TIME BufferDuration = 300'000; // 30 ms in 100 ns units
        constexpr DWORD PacketWaitMilliseconds = 200;
    }

    DeviceOutput::DeviceOutput(Mixer& mixer)
        : _mixer(mixer)
    {
        _loop.Start([this](const std::wstring& deviceId, uint64_t version) { return Play(deviceId, version); });
    }

    DeviceOutput::~DeviceOutput()
    {
        _loop.Stop();
    }

    bool DeviceOutput::Fill(float* data, int frames)
    {
        FillStep step = _fill.Next(_mixer.MonitorAvailable(), frames);
        if (step.ranDry)
        {
            _underruns++;
            _mixer.DiscardMonitor();
            return false;
        }

        if (step.take == 0)
        {
            return false;
        }

        _driftCorrections += step.corrected ? 1 : 0;
        int played = step.take < frames ? step.take : frames;
        _mixer.ReadMonitor(data, static_cast<size_t>(played));
        if (step.take > frames)
        {
            // One frame too many: it is simply not played.
            _mixer.ReadMonitor(nullptr, 1);
        }
        else if (step.take < frames)
        {
            // Repeat the last frame to fill the block.
            std::memcpy(&data[static_cast<size_t>(frames - 1) * Channels], &data[static_cast<size_t>(frames - 2) * Channels], Channels * sizeof(float));
        }

        return true;
    }

    // Opens the device and plays until it fails, the device changes, or the output stops. Returns why it ended.
    HRESULT DeviceOutput::Play(const std::wstring& deviceId, uint64_t version)
    {
        HANDLE packets = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        ComPtr<IAudioClient> client;
        HRESULT hr = OpenSharedClient(deviceId, BufferDuration, packets, client);
        ComPtr<IAudioRenderClient> render;
        if (SUCCEEDED(hr))
        {
            hr = client->GetService(IID_PPV_ARGS(&render));
        }

        if (SUCCEEDED(hr) && !render)
        {
            hr = E_POINTER;
        }

        UINT32 bufferFrames = 0;
        if (SUCCEEDED(hr))
        {
            hr = client->GetBufferSize(&bufferFrames);
        }

        // What waited with no device to play on is stale; the user hears from now on.
        _mixer.DiscardMonitor();
        _fill = FillTracker();
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
            UINT32 padding = 0;
            hr = client->GetCurrentPadding(&padding);
            UINT32 room = SUCCEEDED(hr) ? bufferFrames - padding : 0;
            if (room == 0)
            {
                continue;
            }

            BYTE* data = nullptr;
            hr = render->GetBuffer(room, &data);
            if (SUCCEEDED(hr))
            {
                bool filled = Fill(reinterpret_cast<float*>(data), static_cast<int>(room));
                hr = render->ReleaseBuffer(room, filled ? 0 : AUDCLNT_BUFFERFLAGS_SILENT);
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
