using System;
using System.Threading.Tasks;
using UnityEngine;
using VirtualVessel.Application.Build;
using VirtualVessel.Application.Hosting;
using VirtualVessel.Application.Logging;
using VirtualVessel.Application.Startup;
using VirtualVessel.Core.Threading;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.DataRoot;

namespace VirtualVessel.Application
{
    /// <summary>
    /// The single entry point of the application, placed in the persistent scene.
    /// </summary>
    /// <remarks>
    /// Connects the Unity lifecycle (Awake, Update, quit) to <see cref="ApplicationRuntime"/> and owns
    /// nothing else. It deliberately exposes no static instance: services receive their dependencies
    /// from the composition root, not from a global lookup.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ApplicationBootstrap : MonoBehaviour
    {
        // Bounds how much posted work can run in one frame so a burst cannot cause a frame spike.
        private const int MaxDispatchedActionsPerFrame = 256;

        private MainThreadDispatcher _dispatcher;
        private BufferedApplicationLog _log;
        private ApplicationRuntime _runtime;
        private Task _startTask;
        private Task _stopTask;
        private bool _quitAllowed;

        internal ApplicationState State => _runtime?.State ?? ApplicationState.NotStarted;

        internal ApplicationRuntime Runtime => _runtime;

        private void Awake()
        {
            if (FindObjectsByType<ApplicationBootstrap>(FindObjectsSortMode.None).Length > 1)
            {
                Debug.LogError($"[Application] More than one {nameof(ApplicationBootstrap)} exists. The duplicate on '{name}' is destroyed.");
                Destroy(gameObject);
                return;
            }

            var systemClock = new UtcSystemClock();
            _dispatcher = new MainThreadDispatcher();
            _log = new BufferedApplicationLog(() => systemClock.UtcNow, WriteToUnityConsole);

            ApplicationStartupOptions options = ApplicationStartupOptions.FromEnvironment(
                Environment.GetCommandLineArgs(),
                Environment.GetEnvironmentVariable);

            _runtime = new ApplicationRuntime(
                options,
                BuildInfo.FromUnity(),
                DataRootResolver.ForCurrentUser(),
                new StopwatchMonotonicClock(),
                systemClock,
                _dispatcher,
                _log,
                ApplicationComposition.Create);

            UnityEngine.Application.wantsToQuit += OnWantsToQuit;
            _startTask = StartRuntimeAsync();
        }

        private void Update()
        {
            _dispatcher?.Drain(MaxDispatchedActionsPerFrame, OnDispatchedActionFailed);
        }

        private void OnApplicationQuit()
        {
            // In the Editor, exiting Play Mode does not raise wantsToQuit and does not wait for
            // asynchronous work, so shut down synchronously here. In a player build the asynchronous
            // path below has already run, and this does nothing.
            if (_runtime != null && _stopTask == null)
            {
                try
                {
                    _runtime.Stop();
                }
                catch (Exception exception)
                {
                    _log.Write(ApplicationLogLevel.Error, "Unexpected failure during synchronous shutdown.", exception);
                }
            }
        }

        private void OnDestroy()
        {
            UnityEngine.Application.wantsToQuit -= OnWantsToQuit;
        }

        /// <summary>
        /// Starts the asynchronous shutdown without quitting Unity. Used by tests.
        /// </summary>
        internal Task ShutdownAsync()
        {
            if (_stopTask == null)
            {
                _stopTask = StopRuntimeAsync();
            }

            return _stopTask;
        }

        private bool OnWantsToQuit()
        {
            if (_quitAllowed)
            {
                return true;
            }

            // Cancel the first quit request, shut down asynchronously, then quit again.
            if (_stopTask == null)
            {
                _stopTask = StopRuntimeAndQuitAsync();
            }

            return false;
        }

        private async Task StartRuntimeAsync()
        {
            try
            {
                await _runtime.StartAsync();
            }
            catch (Exception exception)
            {
                _log.Write(ApplicationLogLevel.Error, "Unexpected failure during application startup.", exception);
            }
        }

        private async Task StopRuntimeAsync()
        {
            try
            {
                if (_startTask != null)
                {
                    await _startTask;
                }

                await _runtime.StopAsync();
            }
            catch (Exception exception)
            {
                _log.Write(ApplicationLogLevel.Error, "Unexpected failure during application shutdown.", exception);
            }
        }

        private async Task StopRuntimeAndQuitAsync()
        {
            await StopRuntimeAsync();
            _quitAllowed = true;
            UnityEngine.Application.Quit();
        }

        private void OnDispatchedActionFailed(Exception exception)
        {
            _log.Write(ApplicationLogLevel.Error, "An action posted to the main thread failed.", exception);
        }

        private static void WriteToUnityConsole(ApplicationLogEntry entry)
        {
            string message = $"[Application] {entry.Message}";
            switch (entry.Level)
            {
                case ApplicationLogLevel.Error:
                    Debug.LogError(entry.Exception == null ? message : $"{message}\n{entry.Exception}");
                    break;
                case ApplicationLogLevel.Warning:
                    Debug.LogWarning(entry.Exception == null ? message : $"{message}\n{entry.Exception}");
                    break;
                default:
                    Debug.Log(message);
                    break;
            }
        }
    }
}
