// The monitor output (audio detailed design 8.1): a thread of its own plays the monitor bus on the chosen device
// with WASAPI in shared mode, following the device's clock against the mix's with a frame more or less now
// and then. With nothing to play it writes silence, so the device keeps running.
#pragma once

#include "device_loop.h"
#include "fill_tracker.h"
#include "mixer.h"

#include <atomic>
#include <string>

namespace vv::audio
{
    class DeviceOutput
    {
    public:
        explicit DeviceOutput(Mixer& mixer);
        ~DeviceOutput();

        DeviceOutput(const DeviceOutput&) = delete;
        DeviceOutput& operator=(const DeviceOutput&) = delete;

        void SetDevice(const std::wstring& deviceId)
        {
            _loop.SetDevice(deviceId);
        }

        const DeviceLoop& Loop() const
        {
            return _loop;
        }

        // Blocks the mix had nothing ready for, and samples dropped or repeated, since the start.
        int64_t Underruns() const
        {
            return _underruns.load();
        }

        int64_t DriftCorrections() const
        {
            return _driftCorrections.load();
        }

    private:
        HRESULT Play(const std::wstring& deviceId, uint64_t version);

        // Fills `data`, `frames` long, from the monitor bus; false to play silence instead.
        bool Fill(float* data, int frames);

        Mixer& _mixer;
        FillTracker _fill;
        std::atomic<int64_t> _underruns{ 0 };
        std::atomic<int64_t> _driftCorrections{ 0 };
        DeviceLoop _loop;
    };
}
