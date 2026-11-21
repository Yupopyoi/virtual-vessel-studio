#pragma once

#include <windows.h>

#include <atomic>
#include <condition_variable>
#include <mutex>
#include <thread>

#include "frame_exchange.h"
#include "vv_capture.h"

namespace vv::capture
{
    // What every capture session shares (capture detailed design 8.2, 8.6): a thread of its own, the
    // state the caller reads, and the frames handed over through three buffers. A session type gives
    // the thread's work in Capture(), and stops the thread in its own destructor with StopAndJoin(),
    // before its members go away.
    class SessionCore
    {
    public:
        SessionCore() = default;
        virtual ~SessionCore() = default;

        SessionCore(const SessionCore&) = delete;
        SessionCore& operator=(const SessionCore&) = delete;

        // Starts the capture thread. Throws std::system_error when it cannot.
        void Start();

        vv_capture_status Status() const;

        bool Take(uint64_t after, vv_capture_frame& frame);

    protected:
        // The thread's work; returns when the session stops, fails, or loses what it captures.
        virtual void Capture() = 0;

        // Stops the thread and waits for it. Called by each session type's destructor.
        void StopAndJoin();

        bool Stopping() const
        {
            return _stop.load(std::memory_order_acquire);
        }

        void SetState(int32_t state, HRESULT error);

        void SetRunning(bool recovered);

        // Waits `milliseconds` unless the session is stopping.
        void Wait(int milliseconds);

        FrameExchange& Exchange()
        {
            return _exchange;
        }

    private:
        void Run();

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
