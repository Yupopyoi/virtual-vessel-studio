using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.Diagnostics.Performance.Builtin;

namespace VirtualVessel.Diagnostics.Tests
{
    public sealed class PerformanceMetricsServiceTests
    {
        private ManualMonotonicClock _clock;
        private ManualSystemClock _systemClock;
        private RecordingLog _log;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualMonotonicClock();
            _systemClock = new ManualSystemClock();
            _log = new RecordingLog();
        }

        [Test]
        public void SameName_ReturnsSameInstanceAcrossCallers()
        {
            using PerformanceMetricsService service = CreateService(detailed: true);

            Assert.That(service.Timer("Voice", "Inference"), Is.SameAs(service.Timer("Voice", "Inference")));
            Assert.That(service.Counter("Audio", "Underrun"), Is.SameAs(service.Counter("Audio", "Underrun")));
            Assert.That(service.Gauge("Audio", "Queue"), Is.SameAs(service.Gauge("Audio", "Queue")));
            Assert.Throws<InvalidOperationException>(() => service.Gauge("Voice", "Inference"));
        }

        [Test]
        public void MeasurementsWork_BeforeInitialize()
        {
            using PerformanceMetricsService service = CreateService(detailed: true);

            service.Counter("Audio", "Underrun").Increment();
            service.Timer("Voice", "Inference").Record(TimeSpan.FromMilliseconds(4));

            PerformanceSnapshot snapshot = service.GetSnapshot();
            Assert.That(Find(snapshot, "Audio", "Underrun").Last, Is.EqualTo(1));
            Assert.That(Find(snapshot, "Voice", "Inference").Last, Is.EqualTo(4));
        }

        [Test]
        public void SetDetailedEnabled_SwitchesRecordingAtRuntime()
        {
            using PerformanceMetricsService service = CreateService(detailed: false);
            PerfTimer timer = service.Timer("Voice", "Inference");
            PerfCounter counter = service.Counter("Audio", "Underrun");

            timer.Record(TimeSpan.FromMilliseconds(1));
            counter.Increment();
            service.SetDetailedEnabled(true);
            timer.Record(TimeSpan.FromMilliseconds(2));
            counter.Increment();

            PerformanceSnapshot snapshot = service.GetSnapshot();
            Assert.That(service.IsDetailedEnabled, Is.True);
            Assert.That(snapshot.DetailedEnabled, Is.True);
            Assert.That(Find(snapshot, "Voice", "Inference").TotalCount, Is.EqualTo(1));
            Assert.That(Find(snapshot, "Audio", "Underrun").Last, Is.EqualTo(2));
        }

        [Test]
        public void Snapshot_CarriesTimeAndOrderedMetrics()
        {
            using PerformanceMetricsService service = CreateService(detailed: true, builtin: false);
            service.Counter("Voice", "A");
            service.Counter("Audio", "B");

            PerformanceSnapshot snapshot = service.GetSnapshot();

            Assert.That(snapshot.TakenAtUtc, Is.EqualTo(_systemClock.UtcNow));
            Assert.That(snapshot.Metrics.Select(m => m.Module), Is.EqualTo(new[] { "Audio", "Voice" }));
        }

        [Test]
        public void BuiltinMetrics_FollowDetailedSwitch()
        {
            using PerformanceMetricsService service = CreateService(detailed: true);
            service.InitializeAsync(CancellationToken.None).Wait();

            // Recorders need frames to produce samples, so check the collector state, not the values.
            service.SetDetailedEnabled(false);
            Assert.That(service.GetSnapshot().Metrics.Any(m => m.Module == BuiltinMetricsCollector.Module), Is.False);

            service.SetDetailedEnabled(true);
            Assert.DoesNotThrow(() => service.GetSnapshot());
        }

        [Test]
        public void Shutdown_LogsNonZeroCountersAndTimersWhenDetailed()
        {
            using PerformanceMetricsService service = CreateService(detailed: true, builtin: false);
            service.InitializeAsync(CancellationToken.None).Wait();
            service.Counter("Audio", "Underrun").Add(3);
            service.Counter("Audio", "Unused");
            service.Timer("Voice", "Inference", TimeSpan.FromMilliseconds(10)).Record(TimeSpan.FromMilliseconds(12));

            service.ShutdownAsync(CancellationToken.None).Wait();

            string totals = _log.Entries.Single(e => e.Message.StartsWith("Performance totals at exit", StringComparison.Ordinal)).Message;
            Assert.That(totals, Does.Contain("Audio/Underrun: 3"));
            Assert.That(totals, Does.Not.Contain("Unused"));
            Assert.That(totals, Does.Contain("Voice/Inference: last 12.00"));
            Assert.That(totals, Does.Contain("over 10 ms budget: 1"));
        }

        [Test]
        public void Shutdown_WhenNotDetailed_LogsOnlyCounters()
        {
            using PerformanceMetricsService service = CreateService(detailed: false, builtin: false);
            service.Counter("Audio", "Underrun").Increment();
            service.SetDetailedEnabled(true);
            service.Timer("Voice", "Inference").Record(TimeSpan.FromMilliseconds(5));
            service.SetDetailedEnabled(false);

            service.ShutdownAsync(CancellationToken.None).Wait();

            string totals = _log.Entries.Single(e => e.Message.StartsWith("Performance totals at exit", StringComparison.Ordinal)).Message;
            Assert.That(totals, Does.Contain("Audio/Underrun: 1"));
            Assert.That(totals, Does.Not.Contain("Voice/Inference"));
        }

        [Test]
        public void Shutdown_WithNothingRecorded_WritesNoTotals()
        {
            using PerformanceMetricsService service = CreateService(detailed: true, builtin: false);
            service.Counter("Audio", "Underrun");

            service.ShutdownAsync(CancellationToken.None).Wait();

            Assert.That(_log.Entries.Any(e => e.Message.StartsWith("Performance totals", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void PeriodicSummary_IsWrittenAtDebugWhileDetailed()
        {
            using PerformanceMetricsService service = CreateService(detailed: true, builtin: false, summaryInterval: TimeSpan.FromMilliseconds(20));
            service.Timer("Voice", "Inference").Record(TimeSpan.FromMilliseconds(3));

            service.InitializeAsync(CancellationToken.None).Wait();

            Assert.That(
                () => _log.Entries.Any(e => e.Level == LogLevel.Debug && e.Message.Contains("Voice/Inference")),
                Is.True.After(2000, 20));
        }

        [Test]
        public void NullLog_IsAllowed()
        {
            var service = new PerformanceMetricsService(new PerformanceSettings { DetailedEnabled = true }, _clock, _systemClock, log: null);

            service.InitializeAsync(CancellationToken.None).Wait();
            service.Counter("Audio", "Underrun").Increment();
            service.ShutdownAsync(CancellationToken.None).Wait();
            service.Dispose();
        }

        [Test]
        public void InvalidSettings_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PerformanceMetricsService(new PerformanceSettings { SampleCapacity = 0 }, _clock, _systemClock, _log));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PerformanceMetricsService(new PerformanceSettings { SummaryInterval = TimeSpan.FromSeconds(-1) }, _clock, _systemClock, _log));
        }

        private PerformanceMetricsService CreateService(bool detailed, bool builtin = true, TimeSpan? summaryInterval = null)
        {
            var settings = new PerformanceSettings
            {
                DetailedEnabled = detailed,
                BuiltinMetricsEnabled = builtin,
                SummaryInterval = summaryInterval ?? TimeSpan.Zero,
            };
            return new PerformanceMetricsService(settings, _clock, _systemClock, _log);
        }

        private static MetricSnapshot Find(PerformanceSnapshot snapshot, string module, string name)
        {
            return snapshot.Metrics.Single(m => m.Module == module && m.Name == name);
        }
    }
}
