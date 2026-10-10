using System;
using System.Diagnostics;

namespace VirtualVessel.Core.Time
{
    /// <summary>
    /// <see cref="IMonotonicClock"/> backed by <see cref="Stopwatch"/>, which uses the
    /// high-resolution performance counter on Windows.
    /// </summary>
    public sealed class StopwatchMonotonicClock : IMonotonicClock
    {
        public long Frequency => Stopwatch.Frequency;

        public long GetTimestamp()
        {
            return Stopwatch.GetTimestamp();
        }

        public TimeSpan GetElapsed(long startTimestamp, long endTimestamp)
        {
            // Convert through double to avoid overflow when multiplying large tick counts.
            double seconds = (endTimestamp - startTimestamp) / (double)Stopwatch.Frequency;
            return TimeSpan.FromTicks((long)(seconds * TimeSpan.TicksPerSecond));
        }
    }
}
