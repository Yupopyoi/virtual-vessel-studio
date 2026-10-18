using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.ProjectData.Tests
{
    public sealed class ConfigSaveSchedulerTests
    {
        private string _directory;
        private SteppedClock _clock;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _clock = new SteppedClock();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Test]
        public void ABurstOfChanges_IsSavedOnce_TheDelayAfterTheLast()
        {
            var store = new ConfigFileStore<Sample>(_directory, "sample.json", 1, () => new Sample(), new UtcSystemClock());
            var scheduler = new ConfigSaveScheduler<Sample>(store, _clock, TimeSpan.FromSeconds(0.5), null);

            scheduler.Changed(new Sample { Value = 1 });
            _clock.Advance(0.4);
            scheduler.Changed(new Sample { Value = 2 });
            _clock.Advance(0.4);
            scheduler.Tick();
            Assert.That(scheduler.Pending, Is.True, "A later change moves the save.");

            _clock.Advance(0.1);
            scheduler.Tick();
            scheduler.FlushAsync().Wait();

            Assert.That(scheduler.Pending, Is.False);
            Assert.That(store.Load().Value.Value, Is.EqualTo(2));
        }

        [Test]
        public void Flush_SavesAWaitingChange_AndReportsFailures()
        {
            // A file where the folder should be makes every save fail.
            Directory.CreateDirectory(Path.GetDirectoryName(_directory));
            File.WriteAllText(_directory, "not a folder");
            try
            {
                var failures = new List<Exception>();
                var store = new ConfigFileStore<Sample>(_directory, "sample.json", 1, () => new Sample(), new UtcSystemClock());
                var scheduler = new ConfigSaveScheduler<Sample>(store, _clock, TimeSpan.FromSeconds(10), failures.Add);

                scheduler.Changed(new Sample { Value = 3 });
                scheduler.FlushAsync().Wait();

                Assert.That(failures, Has.Count.EqualTo(1));
            }
            finally
            {
                File.Delete(_directory);
            }
        }

        public sealed class Sample
        {
            public int Value { get; set; }
        }

        private sealed class SteppedClock : IMonotonicClock
        {
            private long _now = 1_000_000;

            public long Frequency => 1_000_000;

            public long GetTimestamp()
            {
                return _now;
            }

            public TimeSpan GetElapsed(long startTimestamp, long endTimestamp)
            {
                return TimeSpan.FromTicks((endTimestamp - startTimestamp) * 10);
            }

            public void Advance(double seconds)
            {
                _now += (long)(seconds * Frequency);
            }
        }
    }
}
