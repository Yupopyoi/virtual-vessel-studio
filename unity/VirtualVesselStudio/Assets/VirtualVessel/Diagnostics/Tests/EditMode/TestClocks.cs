using System;
using VirtualVessel.Core.Time;

namespace VirtualVessel.Diagnostics.Tests
{
    /// <summary>A monotonic clock that only moves when the test advances it. One tick per millisecond.</summary>
    internal sealed class ManualMonotonicClock : IMonotonicClock
    {
        public long Now { get; set; } = 1_000_000;

        public long Frequency => 1000;

        public long GetTimestamp()
        {
            return Now;
        }

        public TimeSpan GetElapsed(long startTimestamp, long endTimestamp)
        {
            return TimeSpan.FromMilliseconds(endTimestamp - startTimestamp);
        }

        public void Advance(TimeSpan duration)
        {
            Now += (long)duration.TotalMilliseconds;
        }
    }

    internal sealed class ManualSystemClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    }
}
