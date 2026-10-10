using System;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Logging.Pipeline;
using VirtualVessel.Diagnostics.Logging.UnityIntegration;
using LogLevel = VirtualVessel.Diagnostics.Logging.LogLevel;

namespace VirtualVessel.Diagnostics.Tests
{
    public sealed class LoggingServiceTests
    {
        private TempDirectory _temp;

        [SetUp]
        public void SetUp()
        {
            _temp = new TempDirectory();
        }

        [TearDown]
        public void TearDown()
        {
            _temp.Dispose();
        }

        [Test]
        public void Lifecycle_WritesHeaderEntriesAndStopMessageToFile()
        {
            LoggingService service = CreateService();

            service.InitializeAsync(CancellationToken.None).Wait();
            service.Provider.GetLog("Avatar").Information("Avatar loaded.", new[] { new LogProperty("AvatarId", "a1") });
            string filePath = service.CurrentFilePath;
            service.ShutdownAsync(CancellationToken.None).Wait();
            service.Dispose();

            string[] lines = File.ReadAllLines(filePath);
            Assert.That(lines[0], Does.StartWith("{\"header\":true"));
            Assert.That(lines.Any(l => l.Contains("\"msg\":\"Avatar loaded.\"") && l.Contains("\"AvatarId\":\"a1\"")), Is.True);
            Assert.That(lines.Last(), Does.Contain("Logging stopped."));
        }

        [Test]
        public void EntriesWrittenBeforeInitialize_AreKept()
        {
            LoggingService service = CreateService();
            ILog log = service.Provider.GetLog("Application");

            log.Information("before start");
            service.InitializeAsync(CancellationToken.None).Wait();
            string filePath = service.CurrentFilePath;
            service.ShutdownAsync(CancellationToken.None).Wait();

            Assert.That(File.ReadAllText(filePath), Does.Contain("before start"));
        }

        [Test]
        public void WriteImported_KeepsOriginalTimestamp()
        {
            LoggingService service = CreateService();
            var original = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

            service.InitializeAsync(CancellationToken.None).Wait();
            service.WriteImported(original, LogLevel.Warning, "Application", "early warning", null, alreadyShownInConsole: true);
            string filePath = service.CurrentFilePath;
            service.ShutdownAsync(CancellationToken.None).Wait();

            string line = File.ReadAllLines(filePath).Single(l => l.Contains("early warning"));
            Assert.That(line, Does.Contain("\"ts\":\"2026-01-02T03:04:05.000Z\""));
        }

        [Test]
        public void SecretsInMessages_AreMaskedInFile()
        {
            LoggingService service = CreateService();

            service.InitializeAsync(CancellationToken.None).Wait();
            service.Provider.GetLog("Streaming").Information("Connecting with stream key = live-secret-123");
            string filePath = service.CurrentFilePath;
            service.ShutdownAsync(CancellationToken.None).Wait();

            Assert.That(File.ReadAllText(filePath), Does.Not.Contain("live-secret-123"));
        }

        [Test]
        public void Initialize_DeletesOldFilesBeyondRetention()
        {
            for (int i = 0; i < 5; i++)
            {
                string path = Path.Combine(_temp.Path, $"old{i}.jsonl");
                File.WriteAllText(path, "{}");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-i - 1));
            }

            LoggingService service = CreateService(new LoggingSettings { MaxRetainedFiles = 3, WriteToUnityConsole = false, CaptureUnityLogs = false });
            service.InitializeAsync(CancellationToken.None).Wait();
            service.ShutdownAsync(CancellationToken.None).Wait();

            Assert.That(Directory.GetFiles(_temp.Path, "*.jsonl"), Has.Length.EqualTo(3), "Two old files plus the current session file.");
        }

        [Test]
        public void Initialize_UnwritableDirectory_Throws()
        {
            string fileInsteadOfDirectory = Path.Combine(_temp.Path, "NotADirectory");
            File.WriteAllText(fileInsteadOfDirectory, "x");
            var service = new LoggingService(QuietSettings(), fileInsteadOfDirectory, Header(), new UtcSystemClock(), new StopwatchMonotonicClock());

            Assert.Catch<Exception>(() => service.InitializeAsync(CancellationToken.None).Wait());
            service.Dispose();
        }

        [Test]
        public void UnityLogCapture_RecordsUnityLogs_ButNotItsOwnConsoleEcho()
        {
            var pipeline = new LogPipeline(new LoggingSettings { RepeatSuppressionWindow = TimeSpan.Zero }, new ManualSystemClock(), new ManualMonotonicClock());
            var capture = new UnityLogCapture(pipeline, () => DateTimeOffset.UtcNow);

            capture.OnUnityLog("Shader compile failed", "at Something()", LogType.Error);
            UnityConsoleWriteGuard.Run(() => capture.OnUnityLog("[Avatar] echo of our own entry", string.Empty, LogType.Log));

            Assert.That(pipeline.TryDequeue(out LogEntry captured), Is.True);
            Assert.That(captured.Module, Is.EqualTo(UnityLogCapture.UnityModule));
            Assert.That(captured.Level, Is.EqualTo(LogLevel.Error));
            Assert.That(captured.ExceptionText, Is.EqualTo("at Something()"));
            Assert.That(pipeline.TryDequeue(out _), Is.False, "The console echo must not be captured again.");
        }

        [Test]
        public void GetStatistics_ReportsCounts()
        {
            LoggingService service = CreateService();

            service.InitializeAsync(CancellationToken.None).Wait();
            service.Provider.GetLog("Test").Information("one");
            service.ShutdownAsync(CancellationToken.None).Wait();
            LoggingStatistics statistics = service.GetStatistics();

            Assert.That(statistics.WrittenCount, Is.GreaterThanOrEqualTo(3), "Started, the test entry, and stopped.");
            Assert.That(statistics.DroppedCount, Is.Zero);
            Assert.That(service.GetRecentEntries().Any(e => e.Message == "one"), Is.True);
        }

        private LoggingService CreateService(LoggingSettings settings = null)
        {
            return new LoggingService(settings ?? QuietSettings(), _temp.Path, Header(), new UtcSystemClock(), new StopwatchMonotonicClock());
        }

        private static LoggingSettings QuietSettings()
        {
            // Unity console output and capture are covered separately; keep them out of the test runner's log.
            return new LoggingSettings { WriteToUnityConsole = false, CaptureUnityLogs = false };
        }

        private static LogSessionHeader Header()
        {
            return new LogSessionHeader(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, "0.0.0-test", "test", Application.unityVersion, "test");
        }
    }
}
