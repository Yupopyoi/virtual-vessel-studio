using System;

namespace VirtualVessel.Diagnostics.Logging
{
    /// <summary>
    /// Session information written at the top of every log file.
    /// </summary>
    /// <remarks>
    /// Defined here rather than reusing Application types because Diagnostics sits below Application
    /// and must not reference it.
    /// </remarks>
    public sealed class LogSessionHeader
    {
        public LogSessionHeader(
            string sessionId,
            DateTimeOffset startedAtUtc,
            string applicationVersion,
            string buildIdentifier,
            string unityVersion,
            string operatingSystem)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("Session ID must not be empty.", nameof(sessionId));
            }

            SessionId = sessionId;
            StartedAtUtc = startedAtUtc;
            ApplicationVersion = applicationVersion ?? string.Empty;
            BuildIdentifier = buildIdentifier ?? string.Empty;
            UnityVersion = unityVersion ?? string.Empty;
            OperatingSystem = operatingSystem ?? string.Empty;
        }

        public string SessionId { get; }

        public DateTimeOffset StartedAtUtc { get; }

        public string ApplicationVersion { get; }

        public string BuildIdentifier { get; }

        public string UnityVersion { get; }

        public string OperatingSystem { get; }
    }
}
