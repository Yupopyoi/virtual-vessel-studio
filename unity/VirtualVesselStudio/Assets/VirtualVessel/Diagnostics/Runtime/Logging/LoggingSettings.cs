using System;

namespace VirtualVessel.Diagnostics.Logging
{
    /// <summary>
    /// All tunable logging values in one place. Passed in by the composition root so that values can
    /// later come from application settings without changing the logging implementation.
    /// </summary>
    public sealed class LoggingSettings
    {
        public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

        public LogLevel ConsoleMinimumLevel { get; set; } = LogLevel.Information;

        public long MaxFileSizeBytes { get; set; } = 10L * 1024 * 1024;

        public int MaxRetainedFiles { get; set; } = 50;

        public TimeSpan MaxRetentionAge { get; set; } = TimeSpan.FromDays(14);

        public long MaxRetainedTotalBytes { get; set; } = 500L * 1024 * 1024;

        public int QueueCapacity { get; set; } = 10000;

        public TimeSpan RepeatSuppressionWindow { get; set; } = TimeSpan.FromSeconds(5);

        public TimeSpan FlushTimeout { get; set; } = TimeSpan.FromSeconds(3);

        public int RecentEntryCapacity { get; set; } = 2000;

        /// <summary>Subscribe to Unity's log callback and unhandled exception events.</summary>
        public bool CaptureUnityLogs { get; set; } = true;

        /// <summary>Echo entries to the Unity console.</summary>
        public bool WriteToUnityConsole { get; set; } = true;

        internal void Validate()
        {
            if (MaxFileSizeBytes < 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxFileSizeBytes), MaxFileSizeBytes, "Must be at least 1 KB.");
            }

            if (MaxRetainedFiles < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxRetainedFiles), MaxRetainedFiles, "Must be at least 1.");
            }

            if (QueueCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(QueueCapacity), QueueCapacity, "Must be at least 1.");
            }

            if (RecentEntryCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(RecentEntryCapacity), RecentEntryCapacity, "Must be at least 1.");
            }
        }
    }
}
