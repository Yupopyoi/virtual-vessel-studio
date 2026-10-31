// What an input and an output device share (audio detailed design 7, 8.1): a thread of its own that opens the
// chosen device and runs until it fails, retrying every second when it is missing, unplugged, or busy, and a
// shared-mode WASAPI client at 48 kHz float stereo that Windows converts to the device's own format.
#pragma once

#include <windows.h>

#include <audioclient.h>
#include <wrl/client.h>

#include <atomic>
#include <functional>
#include <mutex>
#include <string>
#include <thread>

namespace vv::audio
{
    // Opens the endpoint in shared mode with event wakes on `events`. Every result is checked: without an
    // audio service (a server), calls can succeed and still give nothing.
    HRESULT OpenSharedClient(const std::wstring& deviceId, REFERENCE_TIME bufferDuration, HANDLE events, Microsoft::WRL::ComPtr<IAudioClient>& client);

    class DeviceLoop
    {
    public:
        // Runs one device until it fails, the device changes, or the loop stops (`Runs`); returns why it ended.
        using Body = std::function<HRESULT(const std::wstring& deviceId, uint64_t version)>;

        DeviceLoop() = default;
        ~DeviceLoop();

        DeviceLoop(const DeviceLoop&) = delete;
        DeviceLoop& operator=(const DeviceLoop&) = delete;

        // Starts the thread. Separate from the constructor, so the owner's fields are ready before it runs.
        void Start(Body body);

        // Stops and joins the thread, before the owner's fields go away.
        void Stop();

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

        // For the body: the device is open and working.
        void Running();

        // For the body: whether to go on with this device.
        bool Runs(uint64_t version) const
        {
            return !_stop.load() && _version.load() == version;
        }

        // For the body: signaled when the device changes or the loop stops.
        HANDLE Wake() const
        {
            return _wake;
        }

        // What a failure means for the user: unavailable (missing, unplugged) or an error, and why. Pure.
        static void Classify(HRESULT hr, int& state, int& problem);

    private:
        void Run(const Body& body);

        std::mutex _deviceLock;
        std::wstring _deviceId;
        std::atomic<uint64_t> _version{ 0 };
        std::atomic<bool> _stop{ false };
        HANDLE _wake = nullptr;
        std::atomic<int> _state{ 0 };
        std::atomic<int> _problem{ 0 };
        std::atomic<int> _lastError{ 0 };
        std::thread _thread;
    };
}
