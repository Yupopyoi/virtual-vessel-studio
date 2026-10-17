#pragma once

#include <windows.h>

namespace vv::capture
{
    // Creating the duplication gives up at this many failures in a row: a try and five retries.
    constexpr int MaxFailures = 6;

    // The secure desktop (a UAC prompt, the lock screen) denies duplication until it closes, which
    // can take any time, so a duplication that was running waits for it without limit.
    inline bool IsWaitingForSecureDesktop(HRESULT error, bool wasRunning)
    {
        return wasRunning && error == E_ACCESSDENIED;
    }

    // The failures in a row after one more failure with `error`. Waiting for the secure desktop
    // does not count. Pure, like the rest of the policy, so it is tested directly.
    inline int CountFailure(int failures, HRESULT error, bool wasRunning)
    {
        return IsWaitingForSecureDesktop(error, wasRunning) ? failures : failures + 1;
    }

    // Milliseconds to wait before creating the duplication again, or -1 to give up.
    inline int RetryDelayMilliseconds(int failures, HRESULT error, bool wasRunning)
    {
        if (IsWaitingForSecureDesktop(error, wasRunning))
        {
            return 1000;
        }

        // A mode change settles within a few seconds; growing pauses keep the retries cheap.
        static constexpr int s_delays[MaxFailures - 1] = {250, 500, 1000, 2000, 4000};
        return failures >= 1 && failures < MaxFailures ? s_delays[failures - 1] : -1;
    }
}
