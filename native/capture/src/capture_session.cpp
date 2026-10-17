#include "capture_session.h"

#include <chrono>
#include <utility>

#include "desktop_duplication.h"
#include "retry_policy.h"

namespace vv::capture
{
    namespace
    {
        // One wait for a frame; it also bounds how long closing waits for the thread.
        constexpr UINT AcquireTimeoutMilliseconds = 100;
    }

    Session::Session(std::wstring deviceName)
        : _deviceName(std::move(deviceName))
    {
    }

    Session::~Session()
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

    void Session::Start()
    {
        _thread = std::thread(&Session::Run, this);
    }

    vv_capture_status Session::Status() const
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

    bool Session::Take(uint64_t after, vv_capture_frame& frame)
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

    void Session::Run()
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

    void Session::Capture()
    {
        Duplication duplication;

        // Failures since the last captured frame, so that a duplication that is lost again right
        // after being created also waits and finally gives up instead of spinning.
        int failures = 0;
        bool wasRunning = false;
        while (!_stop.load(std::memory_order_acquire))
        {
            HRESULT hr;
            if (!duplication.IsCreated())
            {
                hr = duplication.Create(_deviceName);
                if (hr == MonitorNotFound)
                {
                    SetState(VV_CAPTURE_MONITOR_LOST, hr);
                    return;
                }

                if (SUCCEEDED(hr))
                {
                    SetRunning(wasRunning);
                    wasRunning = true;
                    continue;
                }
            }
            else
            {
                hr = duplication.Capture(_exchange, AcquireTimeoutMilliseconds);
                if (hr == S_OK)
                {
                    failures = 0;
                }

                if (SUCCEEDED(hr))
                {
                    continue;
                }

                // Lost: a mode change, the secure desktop, or a monitor that went away. Creating it
                // again tells a monitor that is gone apart (the prototype stopped for good here).
                duplication.Release();
            }

            failures = CountFailure(failures, hr, wasRunning);
            int delay = RetryDelayMilliseconds(failures, hr, wasRunning);
            if (delay < 0)
            {
                SetState(VV_CAPTURE_FAILED, hr);
                return;
            }

            SetState(wasRunning ? VV_CAPTURE_RECOVERING : VV_CAPTURE_STARTING, hr);
            Wait(delay);
        }
    }

    void Session::SetState(int32_t state, HRESULT error)
    {
        std::lock_guard<std::mutex> lock(_statusMutex);
        _state = state;
        _lastError = error;
    }

    void Session::SetRunning(bool recovered)
    {
        std::lock_guard<std::mutex> lock(_statusMutex);
        _state = VV_CAPTURE_RUNNING;
        if (recovered)
        {
            _recoveries++;
        }
    }

    void Session::Wait(int milliseconds)
    {
        std::unique_lock<std::mutex> lock(_stopMutex);
        _stopSignal.wait_for(lock, std::chrono::milliseconds(milliseconds), [this] { return _stop.load(std::memory_order_acquire); });
    }
}
