using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Logging.Pipeline;
using VirtualVessel.Diagnostics.Logging.Writing;

namespace VirtualVessel.Diagnostics.Tests
{
    public sealed class LogWriterThreadTests
    {
        private static readonly TimeSpan s_stopTimeout = TimeSpan.FromSeconds(5);

        [Test]
        public void Stop_WritesEverythingQueuedBeforeStopping()
        {
            LogPipeline pipeline = CreatePipeline(new LoggingSettings { RepeatSuppressionWindow = TimeSpan.Zero });
            var sink = new CollectingSink("A");
            var writer = new LogWriterThread(pipeline, new[] { sink }, null);
            ILog log = new PipelineLogProvider(pipeline).GetLog("Test");

            writer.Start();
            for (int i = 0; i < 100; i++)
            {
                log.Information($"entry {i}");
            }

            Assert.That(writer.Stop(s_stopTimeout), Is.True);
            Assert.That(sink.Entries.Select(e => e.Message), Is.EqualTo(Enumerable.Range(0, 100).Select(i => $"entry {i}")));
        }

        [Test]
        public void FailingSink_IsDisabled_OthersContinue()
        {
            LogPipeline pipeline = CreatePipeline(new LoggingSettings { RepeatSuppressionWindow = TimeSpan.Zero });
            var healthy = new CollectingSink("Healthy");
            var failing = new CollectingSink("Failing") { ThrowOnWrite = true };
            var failures = new List<string>();
            var writer = new LogWriterThread(pipeline, new ILogSink[] { healthy, failing }, failures.Add);
            ILog log = new PipelineLogProvider(pipeline).GetLog("Test");

            log.Information("first");
            log.Information("second");
            writer.DrainForTesting();

            Assert.That(healthy.Entries, Has.Count.EqualTo(2));
            Assert.That(failing.Disposed, Is.True);
            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(failures[0], Does.Contain("Failing"));
            Assert.That(writer.ActiveSinkNames, Is.EqualTo(new[] { "Healthy" }));
        }

        [Test]
        public void DroppedEntries_AreReportedOnceWritingResumes()
        {
            LogPipeline pipeline = CreatePipeline(new LoggingSettings { QueueCapacity = 2, RepeatSuppressionWindow = TimeSpan.Zero });
            var sink = new CollectingSink("A");
            var writer = new LogWriterThread(pipeline, new[] { sink }, null);
            ILog log = new PipelineLogProvider(pipeline).GetLog("Test");

            for (int i = 0; i < 5; i++)
            {
                log.Information($"entry {i}");
            }

            writer.Start();
            Assert.That(writer.Stop(s_stopTimeout), Is.True);

            LogEntry report = sink.Entries.Single(e => e.Module == "Diagnostics");
            Assert.That(report.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(report.Message, Does.Contain("3 log entries were dropped"));
        }

        [Test]
        public void Stop_WritesOpenRepeatSummaries()
        {
            LogPipeline pipeline = CreatePipeline(new LoggingSettings { RepeatSuppressionWindow = TimeSpan.FromHours(1) });
            var sink = new CollectingSink("A");
            var writer = new LogWriterThread(pipeline, new[] { sink }, null);
            ILog log = new PipelineLogProvider(pipeline).GetLog("Test");

            writer.Start();
            for (int i = 0; i < 5; i++)
            {
                log.Warning("same");
            }

            Assert.That(writer.Stop(s_stopTimeout), Is.True);
            Assert.That(sink.Entries.Select(e => e.RepeatCount), Is.EqualTo(new[] { 0, 4 }));
        }

        private static LogPipeline CreatePipeline(LoggingSettings settings)
        {
            return new LogPipeline(settings, new ManualSystemClock(), new ManualMonotonicClock());
        }

        private sealed class CollectingSink : ILogSink
        {
            private readonly List<LogEntry> _entries = new List<LogEntry>();

            public CollectingSink(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public bool ThrowOnWrite { get; set; }

            public bool Disposed { get; private set; }

            public List<LogEntry> Entries
            {
                get
                {
                    lock (_entries)
                    {
                        return _entries.ToList();
                    }
                }
            }

            public void Write(LogEntry entry)
            {
                if (ThrowOnWrite)
                {
                    throw new InvalidOperationException("Simulated sink failure.");
                }

                lock (_entries)
                {
                    _entries.Add(entry);
                }
            }

            public void Flush()
            {
            }

            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
