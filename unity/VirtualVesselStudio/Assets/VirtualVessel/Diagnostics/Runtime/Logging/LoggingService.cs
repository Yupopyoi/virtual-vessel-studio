using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging.Pipeline;
using VirtualVessel.Diagnostics.Logging.Recent;
using VirtualVessel.Diagnostics.Logging.UnityIntegration;
using VirtualVessel.Diagnostics.Logging.Writing;

namespace VirtualVessel.Diagnostics.Logging
{
    /// <summary>
    /// The application service that owns logging: the pipeline, sinks, writer thread, and Unity capture.
    /// </summary>
    /// <remarks>
    /// Started first and stopped last by the application host, so that every other service's startup
    /// and shutdown is recorded. <see cref="Provider"/> can be handed out before initialization;
    /// entries written early wait in the queue until the writer starts.
    /// </remarks>
    public sealed class LoggingService : IApplicationService
    {
        public const string ServiceName = "Logging";

        private readonly LoggingSettings _settings;
        private readonly string _logDirectory;
        private readonly LogSessionHeader _header;
        private readonly ISystemClock _systemClock;
        private readonly LogPipeline _pipeline;
        private readonly RecentLogBuffer _recent;
        private readonly ILog _log;

        private JsonLinesFileSink _fileSink;
        private UnityConsoleSink _consoleSink;
        private LogWriterThread _writer;
        private UnityLogCapture _capture;
        private bool _writerStopped;

        public LoggingService(
            LoggingSettings settings,
            string logDirectory,
            LogSessionHeader header,
            ISystemClock systemClock,
            IMonotonicClock monotonicClock)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.Validate();
            _logDirectory = logDirectory ?? throw new ArgumentNullException(nameof(logDirectory));
            _header = header ?? throw new ArgumentNullException(nameof(header));
            _systemClock = systemClock ?? throw new ArgumentNullException(nameof(systemClock));

            _pipeline = new LogPipeline(settings, systemClock, monotonicClock ?? throw new ArgumentNullException(nameof(monotonicClock)));
            _recent = new RecentLogBuffer(settings.RecentEntryCapacity);
            Provider = new PipelineLogProvider(_pipeline);
            _log = Provider.GetLog("Diagnostics", "Logging");
        }

        public string Name => ServiceName;

        public ILogProvider Provider { get; }

        public string CurrentFilePath => _fileSink?.CurrentFilePath;

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            RetentionResult retention = LogFileRetention.Apply(
                _logDirectory,
                JsonLinesFileSink.BuildPrefix(_header),
                // Leave room for the file this session is about to create.
                Math.Max(0, _settings.MaxRetainedFiles - 1),
                _settings.MaxRetentionAge,
                _settings.MaxRetainedTotalBytes,
                _systemClock.UtcNow);

            _fileSink = new JsonLinesFileSink(_logDirectory, _header, _settings.MaxFileSizeBytes);
            _fileSink.Open();

            var sinks = new List<ILogSink> { _fileSink, _recent };
            if (_settings.WriteToUnityConsole)
            {
                _consoleSink = new UnityConsoleSink(_settings.ConsoleMinimumLevel);
                sinks.Add(_consoleSink);
            }

            _writer = new LogWriterThread(_pipeline, sinks, ReportSinkFailure);
            _writer.Start();

            if (_settings.CaptureUnityLogs)
            {
                _capture = new UnityLogCapture(_pipeline, () => _systemClock.UtcNow);
                _capture.Start();
            }

            foreach (string warning in retention.Warnings)
            {
                _log.Warning(warning);
            }

            _log.Information($"Logging started. File: {_fileSink.CurrentFilePath}", new[]
            {
                new LogProperty("SessionId", _header.SessionId),
                new LogProperty("DeletedOldFiles", retention.DeletedCount),
            });

            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            _capture?.Dispose();
            _capture = null;

            _log.Information("Logging stopped.", new[]
            {
                new LogProperty("Written", _writer?.WrittenCount ?? 0),
                new LogProperty("Dropped", _pipeline.DroppedCount),
                new LogProperty("Suppressed", _pipeline.SuppressedCount),
            });

            StopWriter(_settings.FlushTimeout);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _capture?.Dispose();
            _capture = null;
            StopWriter(_settings.FlushTimeout);
        }

        public void SetMinimumLevel(LogLevel level)
        {
            _pipeline.MinimumLevel = level;
        }

        /// <summary>Overrides the minimum level for one module, or clears the override when <paramref name="level"/> is null.</summary>
        public void SetModuleLevel(string module, LogLevel? level)
        {
            _pipeline.SetModuleLevel(module, level);
        }

        public IReadOnlyList<LogEntry> GetRecentEntries()
        {
            return _recent.Snapshot();
        }

        public LoggingStatistics GetStatistics()
        {
            return new LoggingStatistics(
                CurrentFilePath,
                _writer?.WrittenCount ?? 0,
                _pipeline.DroppedCount,
                _pipeline.SuppressedCount,
                _pipeline.QueuedCount,
                _writer?.ActiveSinkNames ?? Array.Empty<string>());
        }

        /// <summary>
        /// Records an entry that was produced before logging started, keeping its original timestamp.
        /// </summary>
        /// <param name="alreadyShownInConsole">True if the entry was already printed to the Unity console.</param>
        public void WriteImported(
            DateTimeOffset timestampUtc,
            LogLevel level,
            string module,
            string message,
            Exception exception,
            bool alreadyShownInConsole)
        {
            _pipeline.WriteExternal(
                timestampUtc,
                level,
                module,
                null,
                message,
                exception?.GetType().FullName,
                exception?.ToString(),
                alreadyShownInConsole);
        }

        private void StopWriter(TimeSpan timeout)
        {
            if (_writerStopped)
            {
                return;
            }

            if (_writer == null)
            {
                // Initialization failed after the file was opened but before the writer started.
                _fileSink?.Dispose();
                return;
            }

            _writerStopped = true;
            if (_writer.Stop(timeout))
            {
                _fileSink?.Dispose();
            }
            else
            {
                // The writer thread is still busy; disposing the file under it would race. The process
                // is exiting, and the OS closes the handle.
                ReportSinkFailure($"Log writer did not finish within {timeout.TotalSeconds:F1} s. {_pipeline.QueuedCount} queued entries were not written.");
            }
        }

        private static void ReportSinkFailure(string message)
        {
            // Bypass the pipeline: it may be the component that is failing.
            UnityConsoleWriteGuard.Run(() => UnityEngine.Debug.LogError("[Diagnostics/Logging] " + message));
        }
    }

    /// <summary>
    /// A snapshot of logging health for diagnostics (detailed design 14).
    /// </summary>
    public sealed class LoggingStatistics
    {
        internal LoggingStatistics(
            string currentFilePath,
            long writtenCount,
            long droppedCount,
            long suppressedCount,
            int queuedCount,
            IReadOnlyList<string> activeSinks)
        {
            CurrentFilePath = currentFilePath;
            WrittenCount = writtenCount;
            DroppedCount = droppedCount;
            SuppressedCount = suppressedCount;
            QueuedCount = queuedCount;
            ActiveSinks = activeSinks;
        }

        public string CurrentFilePath { get; }

        public long WrittenCount { get; }

        public long DroppedCount { get; }

        public long SuppressedCount { get; }

        public int QueuedCount { get; }

        public IReadOnlyList<string> ActiveSinks { get; }
    }
}
