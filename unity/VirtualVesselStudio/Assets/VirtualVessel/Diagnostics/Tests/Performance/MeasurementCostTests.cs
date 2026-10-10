using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using Unity.PerformanceTesting;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;

namespace VirtualVessel.Diagnostics.Tests.Performance
{
    /// <summary>
    /// Caller-side cost of the diagnostics APIs that real-time code calls (performance metrics
    /// detailed design 15.2). Results are per <see cref="CallsPerMeasurement"/> calls, so the
    /// microsecond value reads directly as nanoseconds per call.
    /// </summary>
    public sealed class MeasurementCostTests
    {
        private const int CallsPerMeasurement = 1000;
        private const int WarmupCount = 5;
        private const int MeasurementCount = 20;
        private const int MessageCount = (WarmupCount + MeasurementCount) * CallsPerMeasurement;

        private string _logDirectory;
        private LoggingService _logging;

        [SetUp]
        public void SetUp()
        {
            _logDirectory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (_logging != null)
            {
                _logging.ShutdownAsync(CancellationToken.None).Wait();
                _logging.Dispose();
                _logging = null;
            }

            if (Directory.Exists(_logDirectory))
            {
                Directory.Delete(_logDirectory, recursive: true);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        [Performance]
        public void Timer_Measure(bool detailedEnabled)
        {
            using PerformanceMetricsService metrics = CreateMetrics(detailedEnabled);
            PerfTimer timer = metrics.Timer("Benchmark", "Measure");

            Run(() =>
            {
                using (timer.Measure())
                {
                }
            });
        }

        [Test]
        [Performance]
        public void Counter_Increment()
        {
            using PerformanceMetricsService metrics = CreateMetrics(detailedEnabled: false);
            PerfCounter counter = metrics.Counter("Benchmark", "Increment");

            Run(() => counter.Increment());
        }

        [Test]
        [Performance]
        public void Log_DisabledLevel()
        {
            ILog log = StartLogging().Provider.GetLog("Benchmark");

            Run(() => log.Debug("Buffer processed."));
        }

        [Test]
        [Performance]
        public void Log_Information()
        {
            ILog log = StartLogging().Provider.GetLog("Benchmark");
            int i = 0;

            string[] messages = CreateMessages("A");
            Run(() => log.Information(messages[i++ % messages.Length]));
        }

        [Test]
        [Performance]
        public void Log_InformationWithProperties()
        {
            ILog log = StartLogging().Provider.GetLog("Benchmark");
            var properties = new[] { new LogProperty("DeviceId", "device-1"), new LogProperty("SampleRate", 48000) };
            int i = 0;

            string[] messages = CreateMessages("A");
            Run(() => log.Information(messages[i++ % messages.Length], properties));
        }

        [Test]
        [Performance]
        public void Log_ErrorWithException()
        {
            ILog log = StartLogging().Provider.GetLog("Benchmark");
            Exception exception = CreateThrownException();
            int i = 0;

            string[] messages = CreateMessages("A");
            Run(() => log.Error(messages[i++ % messages.Length], exception));
        }

        [Test]
        [Performance]
        public void Log_Information_WhileAnotherThreadLogs()
        {
            ILog log = StartLogging().Provider.GetLog("Benchmark");
            ILog otherLog = _logging.Provider.GetLog("Other");
            int i = 0;
            string[] messages = CreateMessages("A");
            string[] otherMessages = CreateMessages("B");

            // Models a background thread logging at the same time as the measured thread, so that
            // contention on shared logging state shows up in the result.
            using var stop = new ManualResetEventSlim(false);
            var other = new Thread(() =>
            {
                int j = 0;
                while (!stop.IsSet)
                {
                    // Bursts with short pauses: heavy, but bounded so the queue does not fill up.
                    for (int k = 0; k < 50; k++)
                    {
                        otherLog.Information(otherMessages[j++ % otherMessages.Length]);
                    }

                    Thread.Sleep(1);
                }
            })
            {
                IsBackground = true,
            };
            other.Start();

            try
            {
                Run(() => log.Information(messages[i++ % messages.Length]));
            }
            finally
            {
                stop.Set();
                other.Join();
            }
        }

        private static void Run(Action call)
        {
            Measure.Method(() =>
                {
                    for (int i = 0; i < CallsPerMeasurement; i++)
                    {
                        call();
                    }
                })
                // A new group per run: the package accumulates samples into the instance it is given.
                .SampleGroup(new SampleGroup("Time per 1000 calls", SampleUnit.Microsecond))
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .GC()
                .Run();
        }

        private static PerformanceMetricsService CreateMetrics(bool detailedEnabled)
        {
            var settings = new PerformanceSettings
            {
                DetailedEnabled = detailedEnabled,
                BuiltinMetricsEnabled = false,
                SummaryInterval = TimeSpan.Zero,
            };
            return new PerformanceMetricsService(settings, new StopwatchMonotonicClock(), new UtcSystemClock(), log: null);
        }

        private LoggingService StartLogging()
        {
            var settings = new LoggingSettings
            {
                // Large enough that the measured calls take the enqueue path rather than the drop path.
                QueueCapacity = 1_000_000,
                CaptureUnityLogs = false,
                WriteToUnityConsole = false,
            };
            var header = new LogSessionHeader("benchmark", DateTimeOffset.UtcNow, "0.0.0-benchmark", null, UnityEngine.Application.unityVersion, Environment.OSVersion.ToString());

            _logging = new LoggingService(settings, _logDirectory, header, new UtcSystemClock(), new StopwatchMonotonicClock());
            _logging.InitializeAsync(CancellationToken.None).Wait();
            return _logging;
        }

        /// <summary>
        /// One distinct message per measured call, so that repeat suppression counts nothing and every
        /// call takes the full enqueue path. Created up front so that formatting is not measured.
        /// </summary>
        private static string[] CreateMessages(string prefix)
        {
            var messages = new string[MessageCount];
            for (int i = 0; i < messages.Length; i++)
            {
                messages[i] = $"{prefix}: audio device state changed to state {i} after reconnecting.";
            }

            return messages;
        }

        private static Exception CreateThrownException()
        {
            try
            {
                throw new InvalidOperationException("Capture device was lost.");
            }
            catch (InvalidOperationException exception)
            {
                return exception;
            }
        }
    }
}
