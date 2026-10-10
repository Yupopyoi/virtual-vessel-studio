using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VirtualVessel.Application.Build;
using VirtualVessel.Application.Hosting;
using VirtualVessel.Application.Logging;
using VirtualVessel.Application.Session;
using VirtualVessel.Core.Threading;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.DataRoot;

namespace VirtualVessel.Application.Startup
{
    /// <summary>
    /// Runs the startup and shutdown sequence of the application foundation.
    /// </summary>
    /// <remarks>
    /// Separated from the MonoBehaviour entry point so that the whole sequence, including Data Root
    /// failures and session markers, can be tested in EditMode with temporary directories.
    /// </remarks>
    internal sealed class ApplicationRuntime
    {
        private readonly ApplicationStartupOptions _options;
        private readonly BuildInfo _buildInfo;
        private readonly DataRootResolver _dataRootResolver;
        private readonly IMonotonicClock _monotonicClock;
        private readonly ISystemClock _systemClock;
        private readonly IMainThreadDispatcher _mainThreadDispatcher;
        private readonly IApplicationLog _log;
        private readonly Func<ApplicationCompositionContext, IReadOnlyList<ApplicationServiceDescriptor>> _compose;

        private SessionMarker _sessionMarker;
        private bool _sessionMarkerWritten;
        private ApplicationState _startupFailureState = ApplicationState.NotStarted;

        public ApplicationRuntime(
            ApplicationStartupOptions options,
            BuildInfo buildInfo,
            DataRootResolver dataRootResolver,
            IMonotonicClock monotonicClock,
            ISystemClock systemClock,
            IMainThreadDispatcher mainThreadDispatcher,
            IApplicationLog log,
            Func<ApplicationCompositionContext, IReadOnlyList<ApplicationServiceDescriptor>> compose)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _buildInfo = buildInfo ?? throw new ArgumentNullException(nameof(buildInfo));
            _dataRootResolver = dataRootResolver ?? throw new ArgumentNullException(nameof(dataRootResolver));
            _monotonicClock = monotonicClock ?? throw new ArgumentNullException(nameof(monotonicClock));
            _systemClock = systemClock ?? throw new ArgumentNullException(nameof(systemClock));
            _mainThreadDispatcher = mainThreadDispatcher ?? throw new ArgumentNullException(nameof(mainThreadDispatcher));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _compose = compose ?? throw new ArgumentNullException(nameof(compose));
        }

        public ApplicationState State => Host?.State ?? _startupFailureState;

        public ApplicationHost Host { get; private set; }

        public IDataRoot DataRoot { get; private set; }

        public SessionInfo Session { get; private set; }

        public async Task StartAsync()
        {
            if (State != ApplicationState.NotStarted)
            {
                throw new InvalidOperationException($"The application can only be started once. Current state: {State}.");
            }

            _startupFailureState = ApplicationState.Starting;
            _log.Write(ApplicationLogLevel.Information, $"Starting {_buildInfo}.");

            if (!TryResolveDataRoot())
            {
                _startupFailureState = ApplicationState.Failed;
                return;
            }

            StartSession();

            var context = new ApplicationCompositionContext(DataRoot, Session, _buildInfo, _monotonicClock, _systemClock, _mainThreadDispatcher);
            IReadOnlyList<ApplicationServiceDescriptor> descriptors;
            try
            {
                descriptors = _compose(context);
            }
            catch (Exception exception)
            {
                _log.Write(ApplicationLogLevel.Error, "Failed to compose application services.", exception);
                _startupFailureState = ApplicationState.Failed;
                return;
            }

            Host = new ApplicationHost(descriptors, _monotonicClock, _log, _options.ShutdownTimeout);
            await Host.StartAsync();
        }

        public async Task StopAsync()
        {
            if (Host != null)
            {
                await Host.StopAsync();
            }

            EndSession();
        }

        /// <summary>Synchronous shutdown for the Editor's Play Mode exit.</summary>
        public void Stop()
        {
            Host?.Stop();
            EndSession();
        }

        private bool TryResolveDataRoot()
        {
            try
            {
                DataRootResolution resolution = _dataRootResolver.Resolve(_options.DataRootOverride);
                DataRoot = resolution.DataRoot;
                foreach (string warning in resolution.Warnings)
                {
                    _log.Write(ApplicationLogLevel.Warning, warning);
                }

                _log.Write(ApplicationLogLevel.Information, $"Data Root: {DataRoot.RootPath} (source: {resolution.Source}).");
                return true;
            }
            catch (DataRootUnavailableException exception)
            {
                _log.Write(ApplicationLogLevel.Error, $"The Data Root '{exception.Path}' cannot be used. The application cannot start.", exception);
                return false;
            }
        }

        private void StartSession()
        {
            _sessionMarker = new SessionMarker(DataRoot.GetDirectory(DataRootDirectory.Logs));
            PreviousSession previous = _sessionMarker.ReadLeftover();
            Session = new SessionInfo(SessionInfo.NewSessionId(), _systemClock.UtcNow, previous);

            if (previous != null)
            {
                _log.Write(
                    ApplicationLogLevel.Warning,
                    $"The previous session did not exit normally (session: {previous.SessionId ?? "unknown"}, started: {previous.StartedAtUtc ?? "unknown"}, version: {previous.ApplicationVersion ?? "unknown"}).");
            }

            try
            {
                _sessionMarker.Write(Session, _buildInfo.ApplicationVersion);
                _sessionMarkerWritten = true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // Without a marker, the next run cannot detect a crash of this run. Startup is still safe.
                _log.Write(ApplicationLogLevel.Warning, $"Failed to write the session marker at {_sessionMarker.FilePath}.", exception);
            }

            _log.Write(ApplicationLogLevel.Information, $"Session {Session.SessionId} started.");
        }

        private void EndSession()
        {
            if (!_sessionMarkerWritten)
            {
                return;
            }

            try
            {
                _sessionMarker.Delete();
                _sessionMarkerWritten = false;
                _log.Write(ApplicationLogLevel.Information, $"Session {Session.SessionId} ended normally.");
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                _log.Write(ApplicationLogLevel.Warning, $"Failed to delete the session marker at {_sessionMarker.FilePath}. The next start will report an abnormal exit.", exception);
            }
        }
    }
}
