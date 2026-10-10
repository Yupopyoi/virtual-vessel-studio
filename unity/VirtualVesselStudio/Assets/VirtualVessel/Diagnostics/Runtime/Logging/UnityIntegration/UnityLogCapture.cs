using System;
using System.Threading.Tasks;
using UnityEngine;
using VirtualVessel.Diagnostics.Logging.Pipeline;

namespace VirtualVessel.Diagnostics.Logging.UnityIntegration
{
    /// <summary>
    /// Records Unity's own log messages and unhandled exceptions, so that errors from Unity and
    /// third-party code reach the log files too.
    /// </summary>
    internal sealed class UnityLogCapture : IDisposable
    {
        public const string UnityModule = "Unity";

        private readonly LogPipeline _pipeline;
        private readonly Func<DateTimeOffset> _utcNow;
        private bool _subscribed;

        public UnityLogCapture(LogPipeline pipeline, Func<DateTimeOffset> utcNow)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public void Start()
        {
            if (_subscribed)
            {
                return;
            }

            UnityEngine.Application.logMessageReceivedThreaded += OnUnityLog;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            _subscribed = true;
        }

        public void Dispose()
        {
            if (!_subscribed)
            {
                return;
            }

            UnityEngine.Application.logMessageReceivedThreaded -= OnUnityLog;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            _subscribed = false;
        }

        /// <summary>Handles one Unity log message. Internal so that tests can call it directly.</summary>
        internal void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            // Our own console sink echoes entries through Debug.Log; capturing them again would loop.
            if (UnityConsoleWriteGuard.IsWriting)
            {
                return;
            }

            LogLevel level = ToLevel(type);
            bool hasStack = level >= LogLevel.Error && !string.IsNullOrEmpty(stackTrace);

            _pipeline.WriteExternal(
                _utcNow(),
                level,
                UnityModule,
                type.ToString(),
                condition,
                hasStack ? type.ToString() : null,
                hasStack ? stackTrace : null,
                excludeFromConsole: true);
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            _pipeline.WriteExternal(
                _utcNow(),
                LogLevel.Critical,
                "Application",
                "UnhandledException",
                exception?.Message ?? "An unhandled non-exception object was thrown.",
                exception?.GetType().FullName,
                exception?.ToString() ?? e.ExceptionObject?.ToString(),
                excludeFromConsole: false);
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Exception exception = e.Exception?.Flatten().InnerException ?? e.Exception;
            _pipeline.WriteExternal(
                _utcNow(),
                LogLevel.Error,
                "Application",
                "UnobservedTaskException",
                exception?.Message ?? "A task faulted without being observed.",
                exception?.GetType().FullName,
                e.Exception?.ToString(),
                excludeFromConsole: false);
        }

        private static LogLevel ToLevel(LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    return LogLevel.Error;
                case LogType.Warning:
                    return LogLevel.Warning;
                default:
                    return LogLevel.Information;
            }
        }
    }
}
