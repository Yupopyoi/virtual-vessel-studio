using System;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Diagnostics.Logging;

namespace VirtualVessel.Application.Logging
{
    /// <summary>
    /// Hosts <see cref="LoggingService"/> and hands the application foundation's own log over to it.
    /// </summary>
    /// <remarks>
    /// Entries the foundation recorded before logging started are replayed with their original
    /// timestamps, and later foundation entries are forwarded. Just before logging stops, the
    /// foundation log returns to the Unity console so that the host's final messages are still visible.
    /// </remarks>
    internal sealed class ApplicationLoggingService : IApplicationService
    {
        public const string Module = "Application";

        private readonly LoggingService _logging;
        private readonly BufferedApplicationLog _foundationLog;

        public ApplicationLoggingService(LoggingService logging, BufferedApplicationLog foundationLog)
        {
            _logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _foundationLog = foundationLog ?? throw new ArgumentNullException(nameof(foundationLog));
        }

        public string Name => LoggingService.ServiceName;

        public ILogProvider Provider => _logging.Provider;

        public LoggingService Logging => _logging;

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await _logging.InitializeAsync(cancellationToken);
            _foundationLog.AttachForwarder(Forward);
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            _foundationLog.DetachForwarder();
            return _logging.ShutdownAsync(cancellationToken);
        }

        public void Dispose()
        {
            _foundationLog.DetachForwarder();
            _logging.Dispose();
        }

        private void Forward(ApplicationLogEntry entry, bool replayed)
        {
            _logging.WriteImported(entry.TimestampUtc, ToLogLevel(entry.Level), Module, entry.Message, entry.Exception, alreadyShownInConsole: replayed);
        }

        private static LogLevel ToLogLevel(ApplicationLogLevel level)
        {
            switch (level)
            {
                case ApplicationLogLevel.Error:
                    return LogLevel.Error;
                case ApplicationLogLevel.Warning:
                    return LogLevel.Warning;
                default:
                    return LogLevel.Information;
            }
        }
    }
}
