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

    Session::Session(std::wstring deviceName, bool drawPointer)
        : _deviceName(std::move(deviceName))
        , _drawPointer(drawPointer)
    {
    }

    Session::~Session()
    {
        StopAndJoin();
    }

    void Session::Capture()
    {
        Duplication duplication(_drawPointer);

        // Failures since the last captured frame, so that a duplication that is lost again right
        // after being created also waits and finally gives up instead of spinning.
        int failures = 0;
        bool wasRunning = false;
        while (!Stopping())
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
                hr = duplication.Capture(Exchange(), AcquireTimeoutMilliseconds);
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
}
