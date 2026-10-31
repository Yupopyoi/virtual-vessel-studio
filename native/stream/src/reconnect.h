// When a lost stream is tried again (streaming detailed design 7). Pure.
#pragma once

#include <cstdint>

namespace vv::stream
{
    // The wait before attempt n (from 0): 1, 2, 4, 8, 15, then 30 seconds.
    inline int64_t ReconnectDelayMs(int attempt)
    {
        static const int64_t delays[] = { 1'000, 2'000, 4'000, 8'000, 15'000, 30'000 };
        return delays[attempt < 0 ? 0 : (attempt > 5 ? 5 : attempt)];
    }

    // After this long without a connection the stream has failed, and the user is told.
    constexpr int64_t GiveUpAfterMs = 10 * 60 * 1000;
}
