// One source's capture from an input device (audio detailed design 8.1): a thread of its own opens the
// device with WASAPI in shared mode, asks Windows to convert to 48 kHz float stereo, and writes each packet
// to the source's input in the mixer. A missing, unplugged, or busy device is tried again every second.
#pragma once

#include "mixer.h"

#include <windows.h>

#include <atomic>
#include <mutex>
#include <string>
#include <thread>

namespace vv::audio
{
    class DeviceSource
    {
    public:
        DeviceSource(Mixer& mixer, int id);
        ~DeviceSource();

        DeviceSource(const DeviceSource&) = delete;
        DeviceSource& operator=(const DeviceSource&) = delete;

        // An endpoint ID, or empty for none. The thread closes the old device and opens the new one.
        void SetDevice(const std::wstring& deviceId);

        int State() const
        {
            return _state.load();
        }

        int Problem() const
        {
            return _problem.load();
        }

        int LastError() const
        {
            return _lastError.load();
        }

        // What a failure means for the user: unavailable (missing, unplugged) or an error, and why. Pure.
        static void Classify(HRESULT hr, int& state, int& problem);

    private:
        void Run();
        HRESULT Capture(const std::wstring& deviceId, uint64_t version);
        bool WaitForChange(DWORD milliseconds);

        Mixer& _mixer;
        int _id;
        std::mutex _deviceLock;
        std::wstring _deviceId;
        std::atomic<uint64_t> _version{ 0 };
        std::atomic<bool> _stop{ false };
        HANDLE _wake = nullptr;
        std::atomic<int> _state;
        std::atomic<int> _problem;
        std::atomic<int> _lastError{ 0 };
        std::thread _thread;
    };
}
