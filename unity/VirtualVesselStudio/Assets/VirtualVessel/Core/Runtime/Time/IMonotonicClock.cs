using System;

namespace VirtualVessel.Core.Time
{
    /// <summary>
    /// A monotonically increasing, high-resolution clock for measuring durations.
    /// </summary>
    /// <remarks>
    /// Use this clock for processing time, performance metrics, and A/V synchronization.
    /// Never compute durations from wall-clock time, which can jump when the system time changes.
    /// </remarks>
    public interface IMonotonicClock
    {
        /// <summary>
        /// Ticks per second of the values returned by <see cref="GetTimestamp"/>.
        /// </summary>
        long Frequency { get; }

        /// <summary>
        /// Returns the current timestamp. Must not allocate.
        /// </summary>
        long GetTimestamp();

        TimeSpan GetElapsed(long startTimestamp, long endTimestamp);
    }
}
