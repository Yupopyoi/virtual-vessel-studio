using System;
using System.Threading;
using Unity.Profiling;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Performance.Recording;

namespace VirtualVessel.Diagnostics.Performance
{
    /// <summary>
    /// Measures processing time. Use <c>using (timer.Measure()) { ... }</c> by default.
    /// </summary>
    public sealed class PerfTimer : Metric
    {
        private readonly DetailedSwitch _switch;
        private readonly IMonotonicClock _clock;
        private readonly SampleRing _samples;
        private readonly ProfilerMarker _marker;
        private readonly double _budgetMilliseconds;
        private long _overBudgetCount;

        internal PerfTimer(string module, string name, TimeSpan? budget, DetailedSwitch detailedSwitch, IMonotonicClock clock, int sampleCapacity)
            : base(module, name, MetricKind.Timer, "ms")
        {
            Budget = budget;
            _budgetMilliseconds = budget?.TotalMilliseconds ?? double.PositiveInfinity;
            _switch = detailedSwitch;
            _clock = clock;
            _samples = new SampleRing(sampleCapacity);
            _marker = new ProfilerMarker($"VirtualVessel.{module}.{name}");
        }

        public TimeSpan? Budget { get; }

        /// <summary>
        /// Starts measuring a section that ends when the returned scope is disposed. When detailed
        /// metrics are disabled, this only checks the switch and returns an empty scope.
        /// </summary>
        public Scope Measure()
        {
            if (!_switch.IsEnabled)
            {
                return default;
            }

            _marker.Begin();
            return new Scope(this, _clock.GetTimestamp());
        }

        /// <summary>
        /// Starts a section that ends in another method or callback. Pass the result to <see cref="End"/>.
        /// No profiler marker is used, because markers must begin and end on the same thread.
        /// </summary>
        /// <returns>A token for <see cref="End"/>; 0 when detailed metrics are disabled.</returns>
        public long Begin()
        {
            return _switch.IsEnabled ? _clock.GetTimestamp() : 0;
        }

        public void End(long beginToken)
        {
            if (beginToken == 0 || !_switch.IsEnabled)
            {
                return;
            }

            RecordMilliseconds(_clock.GetElapsed(beginToken, _clock.GetTimestamp()).TotalMilliseconds);
        }

        /// <summary>Records a duration measured elsewhere, such as GPU time.</summary>
        public void Record(TimeSpan duration)
        {
            if (_switch.IsEnabled)
            {
                RecordMilliseconds(duration.TotalMilliseconds);
            }
        }

        internal void Complete(long startTimestamp)
        {
            // End the marker even if recording was switched off mid-section, to keep Begin/End paired.
            _marker.End();
            if (_switch.IsEnabled)
            {
                RecordMilliseconds(_clock.GetElapsed(startTimestamp, _clock.GetTimestamp()).TotalMilliseconds);
            }
        }

        internal override MetricSnapshot CreateSnapshot()
        {
            return new MetricSnapshot(this, _samples.ComputeStatistics(), Interlocked.Read(ref _overBudgetCount), Budget);
        }

        private void RecordMilliseconds(double milliseconds)
        {
            _samples.Add(milliseconds);
            if (milliseconds > _budgetMilliseconds)
            {
                Interlocked.Increment(ref _overBudgetCount);
            }
        }

        /// <summary>A measured section. A struct so that <c>using</c> does not allocate.</summary>
        public readonly struct Scope : IDisposable
        {
            private readonly PerfTimer _timer;
            private readonly long _startTimestamp;

            internal Scope(PerfTimer timer, long startTimestamp)
            {
                _timer = timer;
                _startTimestamp = startTimestamp;
            }

            public void Dispose()
            {
                _timer?.Complete(_startTimestamp);
            }
        }
    }
}
