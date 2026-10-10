using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualVessel.Diagnostics.Logging;
using UnityEngine.TestTools.Constraints;
using VirtualVessel.Diagnostics.Logging.Pipeline;
using Is = NUnit.Framework.Is;
using UnityIs = UnityEngine.TestTools.Constraints.Is;

namespace VirtualVessel.Diagnostics.Tests
{
    public sealed class LogPipelineTests
    {
        private ManualMonotonicClock _monotonic;
        private ManualSystemClock _system;

        [SetUp]
        public void SetUp()
        {
            _monotonic = new ManualMonotonicClock();
            _system = new ManualSystemClock();
        }

        [Test]
        public void Write_BelowMinimumLevel_IsNotQueued()
        {
            LogPipeline pipeline = CreatePipeline(new LoggingSettings { MinimumLevel = LogLevel.Warning });
            ILog log = new PipelineLogProvider(pipeline).GetLog("Test");

            log.Information("ignored");
            log.Warning("kept");

            Assert.That(Drain(pipeline).Select(e => e.Message), Is.EqualTo(new[] { "kept" }));
        }

        [Test]
        public void IsEnabled_ModuleOverride_TakesPrecedence()
        {
            LogPipeline pipeline = CreatePipeline(new LoggingSettings { MinimumLevel = LogLevel.Warning });
            pipeline.SetModuleLevel("Voice", LogLevel.Trace);

            Assert.That(pipeline.IsEnabled(LogLevel.Trace, "Voice"), Is.True);
            Assert.That(pipeline.IsEnabled(LogLevel.Information, "Avatar"), Is.False);

            pipeline.SetModuleLevel("Voice", null);

            Assert.That(pipeline.IsEnabled(LogLevel.Trace, "Voice"), Is.False);
        }

        [Test]
        public void MinimumLevel_CanChangeAtRuntime()
        {
            LogPipeline pipeline = CreatePipeline(new LoggingSettings());
            Assert.That(pipeline.IsEnabled(LogLevel.Debug, "Test"), Is.False);

            pipeline.MinimumLevel = LogLevel.Debug;

            Assert.That(pipeline.IsEnabled(LogLevel.Debug, "Test"), Is.True);
        }

        [Test]
        public void WithContext_ReturnsNewLog_AndLeavesOriginalUnchanged()
        {
            LogPipeline pipeline = CreatePipeline(new LoggingSettings());
            ILog original = new PipelineLogProvider(pipeline).GetLog("VoiceLab", "RvcTraining");

            ILog withRun = original.WithContext("RunId", "run-1");
            ILog withRunAndStep = withRun.WithContext("Step", "Preprocess");
            original.Information("plain");
            withRun.Information("run");
            withRunAndStep.Information("step");

            List<LogEntry> entries = Drain(pipeline);
            Assert.That(entries[0].Context, Is.Empty);
            Assert.That(entries[1].Context.Select(c => c.Key), Is.EqualTo(new[] { "RunId" }));
            Assert.That(entries[2].Context.Select(c => c.Key), Is.EqualTo(new[] { "RunId", "Step" }));
            Assert.That(entries[2].Module, Is.EqualTo("VoiceLab"));
            Assert.That(entries[2].Category, Is.EqualTo("RvcTraining"));
        }

        [Test]
        public void Write_RecordsTimestampsAndExceptionDetails()
        {
            LogPipeline pipeline = CreatePipeline(new LoggingSettings());
            ILog log = new PipelineLogProvider(pipeline).GetLog("Test");
            _monotonic.Advance(TimeSpan.FromMilliseconds(250));

            log.Error("failed", new InvalidOperationException("boom"));

            LogEntry entry = Drain(pipeline).Single();
            Assert.That(entry.TimestampUtc, Is.EqualTo(_system.UtcNow));
            Assert.That(entry.ElapsedMilliseconds, Is.EqualTo(250));
            Assert.That(entry.ExceptionType, Is.EqualTo(typeof(InvalidOperationException).FullName));
            Assert.That(entry.ExceptionText, Does.Contain("boom"));
        }

        [Test]
        public void Write_FormatsExceptionOnlyWhenTextIsRead()
        {
            LogPipeline pipeline = CreatePipeline(new LoggingSettings());
            ILog log = new PipelineLogProvider(pipeline).GetLog("Test");
            var exception = new CountingException("password=hunter2");

            log.Error("failed", exception);
            LogEntry entry = Drain(pipeline).Single();

            Assert.That(exception.ToStringCalls, Is.Zero, "The caller must not pay for formatting the exception.");
            Assert.That(entry.ExceptionText, Does.Contain("CountingException").And.Not.Contain("hunter2"));
            Assert.That(entry.ExceptionText, Is.SameAs(entry.ExceptionText));
            Assert.That(exception.ToStringCalls, Is.EqualTo(1));
        }

        [Test]
        public void Write_SummaryOfRepeatedException_KeepsExceptionText()
        {
            LogPipeline pipeline = CreatePipeline(new LoggingSettings());
            ILog log = new PipelineLogProvider(pipeline).GetLog("Test");

            log.Error("failed", new InvalidOperationException("first"));
            log.Error("failed", new InvalidOperationException("second"));
            var summaries = new List<LogEntry>();
            pipeline.FlushRepeatSummaries(long.MaxValue, summaries);

            Assert.That(summaries.Single().ExceptionText, Does.Contain("second"));
        }

        [Test]
        public void ExceptionText_WhenToStringThrows_DescribesTheFailure()
        {
            LogPipeline pipeline = CreatePipeline(new LoggingSettings());
            ILog log = new PipelineLogProvider(pipeline).GetLog("Test");

            log.Error("failed", new ThrowingToStringException());

            Assert.That(Drain(pipeline).Single().ExceptionText, Does.Contain("ToString failed"));
        }

        [Test]
        public void Write_CopiesAndMasksProperties()
        {
            LogPipeline pipeline = CreatePipeline(new LoggingSettings());
            ILog log = new PipelineLogProvider(pipeline).GetLog("Streaming");
            var properties = new List<LogProperty>
            {
                new LogProperty("StreamKey", "abcd-efgh"),
                new LogProperty("Url", "rtmps://a.rtmps.youtube.com/live2/abcd-efgh"),
                new LogProperty("Bitrate", 6000),
            };

            log.Information("connecting", properties);
            properties.Clear();

            LogEntry entry = Drain(pipeline).Single();
            Assert.That(entry.Properties.Count, Is.EqualTo(3));
            Assert.That(entry.Properties[0].Value, Is.EqualTo(SecretMasker.Mask));
            Assert.That((string)entry.Properties[1].Value, Does.Not.Contain("abcd-efgh"));
            Assert.That(entry.Properties[2].Value, Is.EqualTo(6000));
        }

        [Test]
        public void Write_QueueFull_DropsAndCounts()
        {
            LogPipeline pipeline = CreatePipeline(new LoggingSettings { QueueCapacity = 3, RepeatSuppressionWindow = TimeSpan.Zero });
            ILog log = new PipelineLogProvider(pipeline).GetLog("Test");

            for (int i = 0; i < 5; i++)
            {
                log.Information($"entry {i}");
            }

            Assert.That(pipeline.QueuedCount, Is.EqualTo(3));
            Assert.That(pipeline.DroppedCount, Is.EqualTo(2));
            Assert.That(Drain(pipeline).Select(e => e.Message), Is.EqualTo(new[] { "entry 0", "entry 1", "entry 2" }));
        }

        [Test]
        public void RepeatedEntries_WithinWindow_AreSummarized()
        {
            LogPipeline pipeline = CreatePipeline(new LoggingSettings { RepeatSuppressionWindow = TimeSpan.FromSeconds(5) });
            ILog log = new PipelineLogProvider(pipeline).GetLog("Tracking");

            for (int i = 0; i < 4; i++)
            {
                log.Warning("Camera frame unavailable.");
                _monotonic.Advance(TimeSpan.FromMilliseconds(100));
            }

            Assert.That(Drain(pipeline), Has.Count.EqualTo(1), "Only the first occurrence is written immediately.");

            _monotonic.Advance(TimeSpan.FromSeconds(10));
            var summaries = new List<LogEntry>();
            pipeline.FlushRepeatSummaries(_monotonic.Now, summaries);

            Assert.That(summaries, Has.Count.EqualTo(1));
            Assert.That(summaries[0].RepeatCount, Is.EqualTo(3));
            Assert.That(pipeline.SuppressedCount, Is.EqualTo(3));
        }

        [Test]
        public void RepeatedEntries_AfterWindow_WriteSummaryThenNewEntry()
        {
            LogPipeline pipeline = CreatePipeline(new LoggingSettings { RepeatSuppressionWindow = TimeSpan.FromSeconds(5) });
            ILog log = new PipelineLogProvider(pipeline).GetLog("Tracking");

            log.Warning("lost");
            log.Warning("lost");
            _monotonic.Advance(TimeSpan.FromSeconds(6));
            log.Warning("lost");

            List<LogEntry> entries = Drain(pipeline);
            Assert.That(entries.Select(e => e.RepeatCount), Is.EqualTo(new[] { 0, 1, 0 }));
        }

        [Test]
        public void DifferentExceptionTypes_AreNotSuppressedTogether()
        {
            LogPipeline pipeline = CreatePipeline(new LoggingSettings());
            ILog log = new PipelineLogProvider(pipeline).GetLog("Test");

            log.Error("failed", new InvalidOperationException());
            log.Error("failed", new ArgumentException());

            Assert.That(Drain(pipeline), Has.Count.EqualTo(2));
        }

        [Test]
        public void DisabledLevel_DoesNotAllocate()
        {
            LogPipeline pipeline = CreatePipeline(new LoggingSettings { MinimumLevel = LogLevel.Warning });
            ILog log = new PipelineLogProvider(pipeline).GetLog("Audio");

            TestDelegate checkLevel = () => log.IsEnabled(LogLevel.Debug);
            TestDelegate writeDisabled = () => log.Debug("buffer processed");

            // Run the exact delegates once first so that JIT and first-call costs are not measured.
            checkLevel();
            writeDisabled();

            Assert.That(checkLevel, UnityIs.Not.AllocatingGCMemory(), "IsEnabled must not allocate.");
            Assert.That(writeDisabled, UnityIs.Not.AllocatingGCMemory(), "Writing at a disabled level must not allocate.");
        }

        private sealed class CountingException : Exception
        {
            public CountingException(string message)
                : base(message)
            {
            }

            public int ToStringCalls { get; private set; }

            public override string ToString()
            {
                ToStringCalls++;
                return base.ToString();
            }
        }

        private sealed class ThrowingToStringException : Exception
        {
            public override string ToString()
            {
                throw new InvalidOperationException("Broken ToString.");
            }
        }

        private LogPipeline CreatePipeline(LoggingSettings settings)
        {
            return new LogPipeline(settings, _system, _monotonic);
        }

        private static List<LogEntry> Drain(LogPipeline pipeline)
        {
            var entries = new List<LogEntry>();
            while (pipeline.TryDequeue(out LogEntry entry))
            {
                entries.Add(entry);
            }

            return entries;
        }
    }
}
