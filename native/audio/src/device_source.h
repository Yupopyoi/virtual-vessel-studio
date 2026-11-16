// One source's capture from an input device (audio detailed design 8.1): the device's loop opens it with
// WASAPI and writes each packet to the source's input in the mixer.
#pragma once

#include "device_loop.h"
#include "mixer.h"

#include <atomic>
#include <string>

namespace vv::audio
{
    class DeviceSource
    {
    public:
        DeviceSource(Mixer& mixer, int id);
        ~DeviceSource();

        DeviceSource(const DeviceSource&) = delete;
        DeviceSource& operator=(const DeviceSource&) = delete;

        void SetDevice(const std::wstring& deviceId)
        {
            _loop.SetDevice(deviceId);
        }

        const DeviceLoop& Loop() const
        {
            return _loop;
        }

        // What Windows reports the device adds before its packets, in frames.
        int32_t DeviceLatency() const
        {
            return _deviceLatency.load();
        }

    private:
        HRESULT Capture(const std::wstring& deviceId, uint64_t version);

        Mixer& _mixer;
        int _id;
        std::atomic<int32_t> _deviceLatency{ 0 };
        DeviceLoop _loop;
    };
}
