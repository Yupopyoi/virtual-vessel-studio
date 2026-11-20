#include "session_core.h"

#include <chrono>

namespace vv::capture
{
    void SessionCore::Start()
    {
        _thread = std::thread(&SessionCore::Run, this);
    }

    void SessionCore::StopAndJoin()
    {
        {
            std::lock_guard<std::mutex> lock(_stopMutex);
            _stop.store(true, std::memory_order_release);
        }

        _stopSignal.notify_all();
        if (_thread.joinable())
        {
            _thread.join();
        }
    }

    vv_capture_status SessionCore::Status() const
    {
        vv_capture_status status = {};
        {
            std::lock_guard<std::mutex> lock(_statusMutex);
            status.state = _state;
            status.last_error = static_cast<int32_t>(_lastError);
            status.recoveries = _recoveries;
        }

        _exchange.Snapshot(status.frame_number, status.overwritten, status.width, status.height);
        return status;
    }

    bool SessionCore::Take(uint64_t after, vv_capture_frame& frame)
    {
        uint64_t number = 0;
        const FrameBuffer* buffer = _exchange.Take(after, number);
        if (buffer == nullptr)
        {
            return false;
        }

        frame.data = buffer->pixels.data();
        frame.width = buffer->width;
        frame.height = buffer->height;
        frame.frame_number = number;
        return true;
    }

    void SessionCore::Run()
    {
        try
        {
            Capture();
        }
        catch (...)
        {
            // An exception leaving a thread would end the whole application.
            SetState(VV_CAPTURE_FAILED, E_UNEXPECTED);
        }
    }

    void SessionCore::SetState(int32_t state, HRESULT error)
    {
        std::lock_guard<std::mutex> lock(_statusMutex);
        _state = state;
        _lastError = error;
    }

    void SessionCore::SetRunning(bool recovered)
    {
        std::lock_guard<std::mutex> lock(_statusMutex);
        _state = VV_CAPTURE_RUNNING;
        if (recovered)
        {
            _recoveries++;
        }
    }

    void SessionCore::Wait(int milliseconds)
    {
        std::unique_lock<std::mutex> lock(_stopMutex);
        _stopSignal.wait_for(lock, std::chrono::milliseconds(milliseconds), [this] { return _stop.load(std::memory_order_acquire); });
    }
}
