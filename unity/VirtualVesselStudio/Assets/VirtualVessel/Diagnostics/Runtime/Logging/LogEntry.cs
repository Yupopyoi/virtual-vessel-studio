using System;
using System.Collections.Generic;
using System.Threading;
using VirtualVessel.Diagnostics.Logging.Pipeline;

namespace VirtualVessel.Diagnostics.Logging
{
    /// <summary>
    /// One immutable log record, as stored in files and the recent-log buffer. Every text it exposes
    /// is masked.
    /// </summary>
    public sealed class LogEntry
    {
        private static readonly LogProperty[] s_noProperties = Array.Empty<LogProperty>();
        private static readonly KeyValuePair<string, string>[] s_noContext = Array.Empty<KeyValuePair<string, string>>();

        private string _exceptionText;
        private Exception _sourceException;

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
            bool excludeFromConsole = false,
            Exception sourceException = null)
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
            _exceptionText = exceptionText;
            _sourceException = exceptionText == null ? sourceException : null;
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

        /// <summary>The exception's message and stack trace, masked, or null.</summary>
        /// <remarks>
        /// For exceptions logged through <see cref="ILog"/>, the text is built on first access, which is
        /// normally the writer thread. Formatting a stack trace and masking it costs tens of
        /// microseconds, which must not land on audio or tracking threads that report an error.
        /// </remarks>
        public string ExceptionText
        {
            get
            {
                string text = Volatile.Read(ref _exceptionText);
                if (text != null)
                {
                    return text;
                }

                Exception exception = Volatile.Read(ref _sourceException);
                if (exception == null)
                {
                    // Either there is no exception, or another thread has just formatted it.
                    return Volatile.Read(ref _exceptionText);
                }

                text = SecretMasker.MaskText(FormatException(exception));
                string existing = Interlocked.CompareExchange(ref _exceptionText, text, null);

                // Release the exception so that entries kept in the recent-log buffer do not keep
                // whatever the exception references alive.
                Volatile.Write(ref _sourceException, null);
                return existing ?? text;
            }
        }

        public int ThreadId { get; }

        /// <summary>How many identical entries were suppressed and summarized by this one (system design 5.27).</summary>
        public int RepeatCount { get; }

        /// <summary>
        /// True for entries that are already visible in the Unity console, such as captured Unity logs.
        /// </summary>
        internal bool ExcludeFromConsole { get; }

        private static string FormatException(Exception exception)
        {
            try
            {
                return exception.ToString();
            }
            catch (Exception formatFailure)
            {
                // A throwing ToString override must not reach the writer thread, which would disable
                // the sink that asked for the text.
                return $"{exception.GetType().FullName}: <ToString failed with {formatFailure.GetType().FullName}>";
            }
        }

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
                Volatile.Read(ref _exceptionText),
                ThreadId,
                repeatCount,
                ExcludeFromConsole,
                Volatile.Read(ref _sourceException));
        }
    }
}
