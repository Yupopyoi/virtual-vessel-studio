using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.Diagnostics.Performance.Recording;
using Is = NUnit.Framework.Is;
using UnityIs = UnityEngine.TestTools.Constraints.Is;

namespace VirtualVessel.Diagnostics.Tests
{
    public sealed class PerformanceRecordingTests
    {
        private ManualMonotonicClock _clock;
        private DetailedSwitch _switch;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualMonotonicClock();
            _switch = new DetailedSwitch(true);
        }

        [Test]
        public void SampleRing_ComputesAverageMaxAndPercentiles()
        {
            var ring = new SampleRing(100);
            for (int i = 1; i <= 100; i++)
            {
                ring.Add(i);
            }

            SampleStatistics statistics = ring.ComputeStatistics();

            Assert.That(statistics.Last, Is.EqualTo(100));
            Assert.That(statistics.Average, Is.EqualTo(50.5));
            Assert.That(statistics.Max, Is.EqualTo(100));
            Assert.That(statistics.P95, Is.EqualTo(95));
            Assert.That(statistics.P99, Is.EqualTo(99));
            Assert.That(statistics.SampleCount, Is.EqualTo(100));
            Assert.That(statistics.TotalCount, Is.EqualTo(100));
        }

        [Test]
        public void SampleRing_KeepsOnlyRecentSamplesButCountsAll()
        {
            var ring = new SampleRing(4);
            foreach (double value in new double[] { 100, 100, 1, 2, 3, 4 })
            {
                ring.Add(value);
            }

            SampleStatistics statistics = ring.ComputeStatistics();

            // The early spike has been overwritten, so it no longer hides the recent state.
            Assert.That(statistics.Max, Is.EqualTo(4));
            Assert.That(statistics.Average, Is.EqualTo(2.5));
            Assert.That(statistics.SampleCount, Is.EqualTo(4));
            Assert.That(statistics.TotalCount, Is.EqualTo(6));
        }

        [Test]
        public void SampleRing_WithoutSamples_ReturnsEmptyStatistics()
        {
            SampleStatistics statistics = new SampleRing(8).ComputeStatistics();

            Assert.That(statistics.SampleCount, Is.Zero);
            Assert.That(statistics.TotalCount, Is.Zero);
        }

        [Test]
        public void Timer_Measure_RecordsElapsedTime()
        {
            PerfTimer timer = CreateTimer();

            using (timer.Measure())
            {
                _clock.Advance(TimeSpan.FromMilliseconds(5));
            }

            MetricSnapshot snapshot = timer.CreateSnapshot();
            Assert.That(snapshot.Last, Is.EqualTo(5));
            Assert.That(snapshot.Unit, Is.EqualTo("ms"));
            Assert.That(snapshot.TotalCount, Is.EqualTo(1));
        }

        [Test]
        public void Timer_BeginEnd_RecordsElapsedTime()
        {
            PerfTimer timer = CreateTimer();

            long token = timer.Begin();
            _clock.Advance(TimeSpan.FromMilliseconds(7));
            timer.End(token);

            Assert.That(timer.CreateSnapshot().Last, Is.EqualTo(7));
        }

        [Test]
        public void Timer_CountsRecordingsOverBudget()
        {
            PerfTimer timer = CreateTimer(TimeSpan.FromMilliseconds(10));

            timer.Record(TimeSpan.FromMilliseconds(9));
            timer.Record(TimeSpan.FromMilliseconds(10));
            timer.Record(TimeSpan.FromMilliseconds(11));
            timer.Record(TimeSpan.FromMilliseconds(30));

            MetricSnapshot snapshot = timer.CreateSnapshot();
            Assert.That(snapshot.OverBudgetCount, Is.EqualTo(2));
            Assert.That(snapshot.Budget, Is.EqualTo(TimeSpan.FromMilliseconds(10)));
        }

        [Test]
        public void Disabled_TimerAndGaugeDoNotRecord_CounterDoes()
        {
            _switch.IsEnabled = false;
            PerfTimer timer = CreateTimer();
            PerfGauge gauge = new PerfGauge("Test", "Gauge", "items", _switch, 16);
            var counter = new PerfCounter("Test", "Counter");

            using (timer.Measure())
            {
                _clock.Advance(TimeSpan.FromMilliseconds(5));
            }

            timer.End(timer.Begin());
            timer.Record(TimeSpan.FromMilliseconds(5));
            gauge.Set(3);
            counter.Increment();
            counter.Add(2);

            Assert.That(timer.CreateSnapshot().TotalCount, Is.Zero);
            Assert.That(gauge.CreateSnapshot().TotalCount, Is.Zero);
            Assert.That(counter.Value, Is.EqualTo(3));
            Assert.That(counter.CreateSnapshot().Last, Is.EqualTo(3));
        }

        [Test]
        public void SwitchingOff_KeepsStatisticsAndStopsRecording()
        {
            PerfGauge gauge = new PerfGauge("Test", "Gauge", null, _switch, 16);
            gauge.Set(1);
            gauge.Set(3);

            _switch.IsEnabled = false;
            gauge.Set(100);

            MetricSnapshot snapshot = gauge.CreateSnapshot();
            Assert.That(snapshot.TotalCount, Is.EqualTo(2));
            Assert.That(snapshot.Average, Is.EqualTo(2));

            _switch.IsEnabled = true;
            gauge.Set(5);
            Assert.That(gauge.CreateSnapshot().TotalCount, Is.EqualTo(3));
        }

        [Test]
        public void SwitchingOffDuringMeasure_EndsScopeWithoutRecording()
        {
            PerfTimer timer = CreateTimer();

            using (timer.Measure())
            {
                _switch.IsEnabled = false;
            }

            Assert.That(timer.CreateSnapshot().TotalCount, Is.Zero);
        }

        [Test]
        public void Registry_SameNameReturnsSameInstance()
        {
            var registry = new MetricRegistry();

            PerfCounter first = registry.GetOrAdd("Audio", "Underrun", MetricKind.Counter, () => new PerfCounter("Audio", "Underrun"));
            PerfCounter second = registry.GetOrAdd("Audio", "Underrun", MetricKind.Counter, () => new PerfCounter("Audio", "Underrun"));

            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void Registry_KindMismatchThrows()
        {
            var registry = new MetricRegistry();
            registry.GetOrAdd("Audio", "Underrun", MetricKind.Counter, () => new PerfCounter("Audio", "Underrun"));

            Assert.Throws<InvalidOperationException>(() =>
                registry.GetOrAdd("Audio", "Underrun", MetricKind.Timer, () => CreateTimer()));
        }

        [Test]
        public void Registry_RejectsEmptyNames()
        {
            var registry = new MetricRegistry();

            Assert.Throws<ArgumentException>(() => registry.GetOrAdd(" ", "Name", MetricKind.Counter, () => new PerfCounter(" ", "Name")));
            Assert.Throws<ArgumentException>(() => registry.GetOrAdd("Module", "", MetricKind.Counter, () => new PerfCounter("Module", "")));
        }

        [Test]
        public void Registry_SnapshotsAreOrderedByModuleAndName()
        {
            var registry = new MetricRegistry();
            registry.GetOrAdd("Voice", "B", MetricKind.Counter, () => new PerfCounter("Voice", "B"));
            registry.GetOrAdd("Audio", "Z", MetricKind.Counter, () => new PerfCounter("Audio", "Z"));
            registry.GetOrAdd("Voice", "A", MetricKind.Counter, () => new PerfCounter("Voice", "A"));

            var snapshots = new List<MetricSnapshot>();
            registry.AppendSnapshots(snapshots);

            Assert.That(snapshots.Select(s => s.Module + "/" + s.Name), Is.EqualTo(new[] { "Audio/Z", "Voice/A", "Voice/B" }));
        }

        [Test]
        public void MeasuringCalls_DoNotAllocate()
        {
            PerfTimer timer = CreateTimer(TimeSpan.FromMilliseconds(1));
            PerfGauge gauge = new PerfGauge("Test", "Gauge", null, _switch, 16);
            var counter = new PerfCounter("Test", "Counter");

            TestDelegate measure = () =>
            {
                using (timer.Measure())
                {
                }
            };
            TestDelegate beginEnd = () => timer.End(timer.Begin());
            TestDelegate increment = () => counter.Increment();
            TestDelegate set = () => gauge.Set(1);

            // Run the exact delegates once first so that JIT and first-call costs are not measured.
            measure();
            beginEnd();
            increment();
            set();

            Assert.That(measure, UnityIs.Not.AllocatingGCMemory(), "Measure must not allocate when enabled.");
            Assert.That(beginEnd, UnityIs.Not.AllocatingGCMemory(), "Begin/End must not allocate.");
            Assert.That(increment, UnityIs.Not.AllocatingGCMemory(), "Increment must not allocate.");
            Assert.That(set, UnityIs.Not.AllocatingGCMemory(), "Set must not allocate.");

            _switch.IsEnabled = false;
            Assert.That(measure, UnityIs.Not.AllocatingGCMemory(), "Measure must not allocate when disabled.");
        }

        private PerfTimer CreateTimer(TimeSpan? budget = null)
        {
            return new PerfTimer("Test", "Timer", budget, _switch, _clock, 16);
        }
    }
}
