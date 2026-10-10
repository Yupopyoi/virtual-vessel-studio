using System;
using System.Collections.Generic;

namespace VirtualVessel.Diagnostics.Logging
{
    /// <summary>
    /// One immutable log record, already masked, as stored in files and the recent-log buffer.
    /// </summary>
    public sealed class LogEntry
    {
        private static readonly LogProperty[] s_noProperties = Array.Empty<LogProperty>();
        private static readonly KeyValuePair<string, string>[] s_noContext = Array.Empty<KeyValuePair<string, string>>();

        internal LogEntry(
            DateTimeOffset timestampUtc,
            long elapsedMilliseconds,
            LogLevel level,
            string module,
            string category,
            string message,
            IReadOnlyList<LogProperty> properties,
            IReadOnlyList<KeyValuePair<string, string>> context,
            string exceptionType,
            string exceptionText,
            int threadId,
            int repeatCount = 0,
            bool excludeFromConsole = false)
        {
            TimestampUtc = timestampUtc;
            ElapsedMilliseconds = elapsedMilliseconds;
            Level = level;
            Module = module ?? string.Empty;
            Category = category;
            Message = message ?? string.Empty;
            Properties = properties ?? s_noProperties;
            Context = context ?? s_noContext;
            ExceptionType = exceptionType;
            ExceptionText = exceptionText;
            ThreadId = threadId;
            RepeatCount = repeatCount;
            ExcludeFromConsole = excludeFromConsole;
        }

        public DateTimeOffset TimestampUtc { get; }

        /// <summary>Milliseconds since the session started, from the monotonic clock.</summary>
        public long ElapsedMilliseconds { get; }

        public LogLevel Level { get; }

        public string Module { get; }

        /// <summary>Null when no category was given.</summary>
        public string Category { get; }

        public string Message { get; }

        public IReadOnlyList<LogProperty> Properties { get; }

        public IReadOnlyList<KeyValuePair<string, string>> Context { get; }

        /// <summary>Null when the entry has no exception.</summary>
        public string ExceptionType { get; }

        /// <summary>The exception's message and stack trace, or null.</summary>
        public string ExceptionText { get; }

        public int ThreadId { get; }

        /// <summary>How many identical entries were suppressed and summarized by this one (system design 5.27).</summary>
        public int RepeatCount { get; }

        /// <summary>
        /// True for entries that are already visible in the Unity console, such as captured Unity logs.
        /// </summary>
        internal bool ExcludeFromConsole { get; }

        internal LogEntry WithRepeatCount(int repeatCount)
        {
            return new LogEntry(
                TimestampUtc,
                ElapsedMilliseconds,
                Level,
                Module,
                Category,
                Message,
                Properties,
                Context,
                ExceptionType,
                ExceptionText,
                ThreadId,
                repeatCount,
                ExcludeFromConsole);
        }
    }
}
