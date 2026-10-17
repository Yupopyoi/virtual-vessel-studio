#pragma once

#include <windows.h>

#include <atomic>
#include <condition_variable>
#include <mutex>
#include <string>
#include <thread>

#include "frame_exchange.h"
#include "vv_capture.h"

namespace vv::capture
{
    // One monitor duplicated on a thread of its own (capture detailed design 8.2). The thread owns
    // every D3D11 and DXGI object; the caller sees only the status and the exchanged frames.
    class Session
    {
    public:
        Session(std::wstring deviceName, bool drawPointer);

        // Stops the thread and waits for it.
        ~Session();

        Session(const Session&) = delete;
        Session& operator=(const Session&) = delete;

        // Starts the capture thread. Throws std::system_error when it cannot.
        void Start();

        vv_capture_status Status() const;

        bool Take(uint64_t after, vv_capture_frame& frame);

    private:
        void Run();

        void Capture();

        void SetState(int32_t state, HRESULT error);

        void SetRunning(bool recovered);

        // Waits `milliseconds` unless the session is stopping.
        void Wait(int milliseconds);

        const std::wstring _deviceName;
        const bool _drawPointer;
        FrameExchange _exchange;

        mutable std::mutex _statusMutex;
        int32_t _state = VV_CAPTURE_STARTING;
        HRESULT _lastError = S_OK;
        uint32_t _recoveries = 0;

        // The loop reads the atomic every round; waits use the condition so that stopping cuts them short.
        std::atomic<bool> _stop{false};
        std::mutex _stopMutex;
        std::condition_variable _stopSignal;
        std::thread _thread;
    };
}
