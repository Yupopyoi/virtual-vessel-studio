using System;

namespace VirtualVessel.Application.Session
{
    /// <summary>
    /// Identifies one application run (system design 5.6) and whether the previous run ended abnormally (5.28).
    /// </summary>
    public sealed class SessionInfo
    {
        public SessionInfo(string sessionId, DateTimeOffset startedAtUtc, PreviousSession previousSession)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("Session ID must not be empty.", nameof(sessionId));
            }

            SessionId = sessionId;
            StartedAtUtc = startedAtUtc;
            Previous = previousSession;
        }

        public string SessionId { get; }

        public DateTimeOffset StartedAtUtc { get; }

        /// <summary>The previous session that did not exit normally, or null if it did.</summary>
        public PreviousSession Previous { get; }

        public bool PreviousSessionEndedAbnormally => Previous != null;

        public static string NewSessionId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>
    /// What is known about a previous session from a session marker left behind.
    /// </summary>
    public sealed class PreviousSession
    {
        public PreviousSession(string sessionId, string startedAtUtc, string applicationVersion)
        {
            SessionId = sessionId;
            StartedAtUtc = startedAtUtc;
            ApplicationVersion = applicationVersion;
        }

        /// <summary>Null when the marker existed but could not be read.</summary>
        public string SessionId { get; }

        public string StartedAtUtc { get; }

        public string ApplicationVersion { get; }
    }
}
