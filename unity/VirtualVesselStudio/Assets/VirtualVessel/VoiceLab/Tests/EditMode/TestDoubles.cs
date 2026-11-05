using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ExternalServices.Process;

namespace VirtualVessel.VoiceLab.Tests
{
    /// <summary>Plays back what a script would print, per script and operation, and records each request.</summary>
    internal sealed class ScriptedRunner : IExternalProcessRunner
    {
        private readonly Func<string, string, IReadOnlyList<string>> _lines;
        private readonly Func<string, string, ExternalProcessState> _state;

        public ScriptedRunner(Func<string, string, IReadOnlyList<string>> lines, Func<string, string, ExternalProcessState> state = null)
        {
            _lines = lines;
            _state = state ?? ((_, __) => ExternalProcessState.Completed);
        }

        /// <summary>"script operation", in the order they ran.</summary>
        public ConcurrentQueue<string> Ran { get; } = new ConcurrentQueue<string>();

        public ConcurrentQueue<ExternalProcessRequest> Requests { get; } = new ConcurrentQueue<ExternalProcessRequest>();

        public Task<ExternalProcessResult> RunAsync(ExternalProcessRequest request, Action<string> onOutputLine, CancellationToken cancellationToken)
        {
            // Arguments: -I, the script's path, the operation, the request file.
            string script = System.IO.Path.GetFileName(request.Arguments[1]);
            string operation = request.Arguments[2];
            Ran.Enqueue(script + " " + operation);
            Requests.Enqueue(request);
            foreach (string line in _lines(script, operation))
            {
                onOutputLine(line);
            }

            ExternalProcessState state = _state(script, operation);
            int? exitCode = state == ExternalProcessState.Completed ? 0 : (state == ExternalProcessState.Failed ? 1 : (int?)null);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return Task.FromResult(new ExternalProcessResult(state, exitCode, false, null, now, now, Array.Empty<string>()));
        }
    }

    internal sealed class FixedClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    }

    internal sealed class NullLog : ILog
    {
        public bool IsEnabled(LogLevel level)
        {
            return false;
        }

        public void Write(LogLevel level, string message, Exception exception = null)
        {
        }

        public void Write(LogLevel level, string message, IReadOnlyList<LogProperty> properties, Exception exception = null)
        {
        }

        public ILog WithContext(string key, string value)
        {
            return this;
        }
    }
}
