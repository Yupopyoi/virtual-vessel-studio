using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VirtualVessel.Core.Time
{
    /// <summary>
    /// <see cref="IMonotonicClock"/> on the high-resolution performance counter. On Windows it reads
    /// QueryPerformanceCounter itself: the native plugins time audio and video in QPC values, the clock the
    /// whole application shares (recording detailed design 8.1), and Unity's <see cref="Stopwatch"/> counts at
    /// the same rate from another zero, so its values cannot be compared with theirs. Elsewhere it is
    /// <see cref="Stopwatch"/>.
    /// </summary>
    public sealed class StopwatchMonotonicClock : IMonotonicClock
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private static readonly long s_frequency = QueryFrequency();

        public long Frequency => s_frequency;

        public long GetTimestamp()
        {
            QueryPerformanceCounter(out long value);
            return value;
        }

        private static long QueryFrequency()
        {
            QueryPerformanceFrequency(out long frequency);
            return frequency;
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryPerformanceCounter(out long value);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryPerformanceFrequency(out long frequency);
#else
        public long Frequency => Stopwatch.Frequency;

        public long GetTimestamp()
        {
            return Stopwatch.GetTimestamp();
        }
#endif

        public TimeSpan GetElapsed(long startTimestamp, long endTimestamp)
        {
            // Convert through double to avoid overflow when multiplying large tick counts.
            double seconds = (endTimestamp - startTimestamp) / (double)Frequency;
            return TimeSpan.FromTicks((long)(seconds * TimeSpan.TicksPerSecond));
        }
    }
}
