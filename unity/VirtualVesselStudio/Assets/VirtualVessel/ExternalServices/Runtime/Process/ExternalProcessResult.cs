using System;
using System.Collections.Generic;

namespace VirtualVessel.ExternalServices.Process
{
    /// <summary>How a run ended (external services detailed design 7).</summary>
    public enum ExternalProcessState
    {
        Completed,
        Failed,
        Cancelled,
    }

    /// <summary>What a run left behind for its caller and for diagnostics (system design 4.17, 4.23).</summary>
    public sealed class ExternalProcessResult
    {
        public ExternalProcessResult(ExternalProcessState state, int? exitCode, bool timedOut, string failure,
            DateTimeOffset startedAt, DateTimeOffset endedAt, IReadOnlyList<string> lastErrorLines)
        {
            State = state;
            ExitCode = exitCode;
            TimedOut = timedOut;
            Failure = failure;
            StartedAt = startedAt;
            EndedAt = endedAt;
            LastErrorLines = lastErrorLines ?? Array.Empty<string>();
        }

        public ExternalProcessState State { get; }

        /// <summary>Null when the process never started, or was stopped as a tree.</summary>
        public int? ExitCode { get; }

        public bool TimedOut { get; }

        /// <summary>Why it could not start, for developers; null when it ran.</summary>
        public string Failure { get; }

        public DateTimeOffset StartedAt { get; }

        public DateTimeOffset EndedAt { get; }

        /// <summary>The last lines of standard error, the usual place a failure explains itself.</summary>
        public IReadOnlyList<string> LastErrorLines { get; }

        public static ExternalProcessResult NotStarted(string failure, DateTimeOffset at)
        {
            return new ExternalProcessResult(ExternalProcessState.Failed, null, false, failure, at, at, null);
        }
    }
}
