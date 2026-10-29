using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using VirtualVessel.Core.Time;

namespace VirtualVessel.Core.Tests
{
    public sealed class StopwatchMonotonicClockTests
    {
        [Test]
        public void GetTimestamp_IsMonotonic()
        {
            var clock = new StopwatchMonotonicClock();

            long first = clock.GetTimestamp();
            long second = clock.GetTimestamp();

            Assert.That(second, Is.GreaterThanOrEqualTo(first));
        }

        [Test]
        public void GetElapsed_OneFrequencyOfTicks_IsOneSecond()
        {
            var clock = new StopwatchMonotonicClock();

            TimeSpan elapsed = clock.GetElapsed(0, clock.Frequency);

            Assert.That(elapsed.TotalMilliseconds, Is.EqualTo(1000.0).Within(0.001));
        }

        [Test]
        public void GetElapsed_LargeTimestamps_DoesNotOverflow()
        {
            var clock = new StopwatchMonotonicClock();
            long start = long.MaxValue / 2;
            long end = start + clock.Frequency * 3600;

            TimeSpan elapsed = clock.GetElapsed(start, end);

            Assert.That(elapsed.TotalHours, Is.EqualTo(1.0).Within(0.0001));
        }

        [DllImport("kernel32.dll")]
        private static extern bool QueryPerformanceCounter(out long value);

        [Test]
        public void Timestamps_AreTheNativePluginsQpcValues()
        {
            // Unity's Stopwatch counts from another zero; the recording's audio and video must share one
            // (recording detailed design 8.1). A recording without audio was the result when they did not.
            var clock = new StopwatchMonotonicClock();

            QueryPerformanceCounter(out long before);
            long timestamp = clock.GetTimestamp();
            QueryPerformanceCounter(out long after);

            Assert.That(timestamp, Is.InRange(before, after));
        }
    }
}
