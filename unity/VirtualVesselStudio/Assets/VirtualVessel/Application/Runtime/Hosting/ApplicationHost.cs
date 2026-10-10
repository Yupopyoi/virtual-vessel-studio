using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Application.Logging;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;

namespace VirtualVessel.Application.Hosting
{
    /// <summary>
    /// Starts services in composition order and stops them in reverse order.
    /// </summary>
    /// <remarks>
    /// The host only manages order and lifetime. It never calls into a service beyond
    /// <see cref="IApplicationService"/>, so feature logic stays inside the owning module.
    /// The host does not depend on UnityEngine, which keeps lifecycle rules testable in EditMode.
    /// </remarks>
    internal sealed class ApplicationHost
    {
        private readonly IReadOnlyList<ApplicationServiceDescriptor> _descriptors;
        private readonly IMonotonicClock _clock;
        private readonly IApplicationLog _log;
        private readonly TimeSpan _shutdownTimeout;
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
        private readonly List<ApplicationServiceStatus> _statuses = new List<ApplicationServiceStatus>();

        // Every instance the host created, in creation order, so that each is disposed exactly once.
        private readonly List<IApplicationService> _created = new List<IApplicationService>();

        // Services whose InitializeAsync completed successfully, in startup order.
        private readonly List<IApplicationService> _running = new List<IApplicationService>();

        private bool _stopStarted;

        public ApplicationHost(
            IReadOnlyList<ApplicationServiceDescriptor> descriptors,
            IMonotonicClock clock,
            IApplicationLog log,
            TimeSpan shutdownTimeout)
        {
            _descriptors = descriptors ?? throw new ArgumentNullException(nameof(descriptors));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _shutdownTimeout = shutdownTimeout;

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ApplicationServiceDescriptor descriptor in descriptors)
            {
                if (!names.Add(descriptor.Name))
                {
                    throw new ArgumentException($"Duplicate service name '{descriptor.Name}'.", nameof(descriptors));
                }

                _statuses.Add(new ApplicationServiceStatus(descriptor.Name, descriptor.Criticality));
            }
        }

        public ApplicationState State { get; private set; } = ApplicationState.NotStarted;

        public IReadOnlyList<ApplicationServiceStatus> Services => _statuses;

        /// <summary>Cancelled when shutdown begins. Passed to every service.</summary>
        public CancellationToken Stopping => _stopping.Token;

        public async Task StartAsync()
        {
            if (State != ApplicationState.NotStarted)
            {
                throw new InvalidOperationException($"The host can only be started once. Current state: {State}.");
            }

            State = ApplicationState.Starting;
            long startTimestamp = _clock.GetTimestamp();

            for (int i = 0; i < _descriptors.Count; i++)
            {
                if (_stopping.IsCancellationRequested)
                {
                    _log.Write(ApplicationLogLevel.Warning, "Startup was interrupted by a shutdown request.");
                    return;
                }

                ApplicationServiceDescriptor descriptor = _descriptors[i];
                ApplicationServiceStatus status = _statuses[i];
                bool started = await StartServiceAsync(descriptor, status);

                if (!started && descriptor.Criticality == ServiceCriticality.Required)
                {
                    State = ApplicationState.Failed;
                    _log.Write(ApplicationLogLevel.Error, $"Required service '{descriptor.Name}' did not start. The application cannot run.");
                    return;
                }
            }

            State = ApplicationState.Running;
            TimeSpan elapsed = _clock.GetElapsed(startTimestamp, _clock.GetTimestamp());
            int failedCount = _statuses.Count(s => s.State == ApplicationServiceState.Failed || s.State == ApplicationServiceState.Skipped);
            _log.Write(
                failedCount == 0 ? ApplicationLogLevel.Information : ApplicationLogLevel.Warning,
                $"Application started in {elapsed.TotalMilliseconds:F0} ms. Services: {_running.Count} running, {failedCount} unavailable.");
        }

        /// <summary>
        /// Stops running services in reverse startup order and disposes every created service.
        /// </summary>
        public Task StopAsync()
        {
            return StopCoreAsync(synchronous: false);
        }

        /// <summary>
        /// Synchronous variant for the Editor's Play Mode exit, where Unity does not wait for asynchronous work.
        /// Each service shutdown is awaited by blocking the calling thread up to the remaining timeout.
        /// </summary>
        public void Stop()
        {
            StopCoreAsync(synchronous: true).GetAwaiter().GetResult();
        }

        private async Task<bool> StartServiceAsync(ApplicationServiceDescriptor descriptor, ApplicationServiceStatus status)
        {
            string missingDependency = descriptor.DependsOn.FirstOrDefault(dependency => !IsRunning(dependency));
            if (missingDependency != null)
            {
                status.State = ApplicationServiceState.Skipped;
                status.FailureReason = $"Dependency '{missingDependency}' is not running.";
                _log.Write(ApplicationLogLevel.Warning, $"Service '{descriptor.Name}' was skipped: {status.FailureReason}");
                return false;
            }

            IApplicationService service;
            try
            {
                service = descriptor.Factory();
                if (service == null)
                {
                    throw new InvalidOperationException("The service factory returned null.");
                }
            }
            catch (Exception exception)
            {
                MarkFailed(status, $"Creation failed: {exception.Message}", exception);
                return false;
            }

            _created.Add(service);
            status.State = ApplicationServiceState.Initializing;
            long startTimestamp = _clock.GetTimestamp();

            Task initializeTask;
            try
            {
                initializeTask = service.InitializeAsync(_stopping.Token) ?? Task.CompletedTask;
            }
            catch (Exception exception)
            {
                MarkFailed(status, $"Initialization failed: {exception.Message}", exception);
                return false;
            }

            Task completed = await Task.WhenAny(initializeTask, Task.Delay(descriptor.InitializeTimeout));
            status.InitializeDuration = _clock.GetElapsed(startTimestamp, _clock.GetTimestamp());

            if (completed != initializeTask)
            {
                ObserveLateFailure(initializeTask, descriptor.Name);
                MarkFailed(status, $"Initialization timed out after {descriptor.InitializeTimeout.TotalSeconds:F1} s.", null);
                return false;
            }

            try
            {
                await initializeTask;
            }
            catch (Exception exception)
            {
                MarkFailed(status, $"Initialization failed: {exception.Message}", exception);
                return false;
            }

            status.State = ApplicationServiceState.Running;
            _running.Add(service);
            _log.Write(ApplicationLogLevel.Information, $"Service '{descriptor.Name}' started in {status.InitializeDuration.TotalMilliseconds:F0} ms.");
            return true;
        }

        private async Task StopCoreAsync(bool synchronous)
        {
            if (_stopStarted)
            {
                return;
            }

            _stopStarted = true;
            ApplicationState previousState = State;
            State = ApplicationState.ShuttingDown;
            _stopping.Cancel();

            long startTimestamp = _clock.GetTimestamp();
            TimeSpan deadline = _shutdownTimeout;

            for (int i = _running.Count - 1; i >= 0; i--)
            {
                IApplicationService service = _running[i];
                ApplicationServiceStatus status = FindStatus(service.Name);
                TimeSpan remaining = deadline - _clock.GetElapsed(startTimestamp, _clock.GetTimestamp());

                if (remaining <= TimeSpan.Zero)
                {
                    _log.Write(ApplicationLogLevel.Warning, $"Shutdown timeout reached. Service '{service.Name}' was not shut down.");
                    continue;
                }

                // Shutdown uses a fresh token: the stopping token is already cancelled and would make
                // well-behaved services abort their cleanup immediately.
                using (var shutdownCancellation = new CancellationTokenSource(remaining))
                {
                    await ShutdownServiceAsync(service, status, remaining, shutdownCancellation.Token, synchronous);
                }
            }

            for (int i = _created.Count - 1; i >= 0; i--)
            {
                IApplicationService service = _created[i];
                try
                {
                    service.Dispose();
                }
                catch (Exception exception)
                {
                    _log.Write(ApplicationLogLevel.Error, $"Service '{service.Name}' threw during Dispose.", exception);
                }
            }

            State = ApplicationState.Stopped;
            TimeSpan elapsed = _clock.GetElapsed(startTimestamp, _clock.GetTimestamp());
            _log.Write(ApplicationLogLevel.Information, $"Application stopped in {elapsed.TotalMilliseconds:F0} ms (previous state: {previousState}).");
        }

        private async Task ShutdownServiceAsync(
            IApplicationService service,
            ApplicationServiceStatus status,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            bool synchronous)
        {
            try
            {
                Task shutdownTask = service.ShutdownAsync(cancellationToken) ?? Task.CompletedTask;
                bool completed;

                if (synchronous)
                {
                    completed = shutdownTask.Wait(timeout);
                }
                else
                {
                    completed = await Task.WhenAny(shutdownTask, Task.Delay(timeout)) == shutdownTask;
                    if (completed)
                    {
                        await shutdownTask;
                    }
                }

                if (!completed)
                {
                    ObserveLateFailure(shutdownTask, service.Name);
                    _log.Write(ApplicationLogLevel.Warning, $"Service '{service.Name}' did not finish shutting down within {timeout.TotalSeconds:F1} s.");
                }

                if (status != null)
                {
                    status.State = ApplicationServiceState.Stopped;
                }
            }
            catch (Exception exception)
            {
                Exception reported = exception is AggregateException aggregate && aggregate.InnerExceptions.Count == 1
                    ? aggregate.InnerException
                    : exception;
                _log.Write(ApplicationLogLevel.Error, $"Service '{service.Name}' threw during shutdown.", reported);
                if (status != null)
                {
                    status.State = ApplicationServiceState.Stopped;
                }
            }
        }

        private bool IsRunning(string serviceName)
        {
            ApplicationServiceStatus status = FindStatus(serviceName);
            return status != null && status.State == ApplicationServiceState.Running;
        }

        private ApplicationServiceStatus FindStatus(string serviceName)
        {
            return _statuses.FirstOrDefault(s => string.Equals(s.Name, serviceName, StringComparison.Ordinal));
        }

        private void MarkFailed(ApplicationServiceStatus status, string reason, Exception exception)
        {
            status.State = ApplicationServiceState.Failed;
            status.FailureReason = reason;
            ApplicationLogLevel level = status.Criticality == ServiceCriticality.Required
                ? ApplicationLogLevel.Error
                : ApplicationLogLevel.Warning;
            _log.Write(level, $"Service '{status.Name}' failed. {reason}", exception);
        }

        private void ObserveLateFailure(Task task, string serviceName)
        {
            // A timed-out task may still fault later. Observe it so the failure is logged instead of
            // surfacing as an unobserved task exception.
            task.ContinueWith(
                t => _log.Write(ApplicationLogLevel.Warning, $"Service '{serviceName}' failed after its timeout.", t.Exception?.GetBaseException()),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
