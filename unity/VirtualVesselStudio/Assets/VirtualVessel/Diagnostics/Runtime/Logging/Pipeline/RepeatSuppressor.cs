using System;
using System.Collections.Generic;

namespace VirtualVessel.Diagnostics.Logging.Pipeline
{
    /// <summary>
    /// Collapses identical entries repeated within a time window into one summary entry
    /// (system design 5.27), so that the first error is not buried under copies of itself.
    /// </summary>
    /// <remarks>
    /// The first occurrence is always written. Later occurrences inside the window are counted, and a
    /// summary carrying the count is emitted when the window ends. Thread-safe.
    /// </remarks>
    internal sealed class RepeatSuppressor
    {
        // Bounds memory if many distinct messages arrive; the oldest windows are summarized early.
        private const int MaxTrackedKeys = 1024;

        private readonly object _gate = new object();
        private readonly Dictionary<RepeatKey, Window> _windows = new Dictionary<RepeatKey, Window>();
        private readonly long _windowTicks;

        /// <param name="windowLengthInClockTicks">Window length in the same units as the timestamps passed in.</param>
        public RepeatSuppressor(long windowLengthInClockTicks)
        {
            _windowTicks = windowLengthInClockTicks;
        }

        private long _suppressedTotal;

        public long SuppressedTotal
        {
            get
            {
                lock (_gate)
                {
                    return _suppressedTotal;
                }
            }
        }

        /// <summary>
        /// Returns true if <paramref name="entry"/> should be written; false if it was counted as a repeat.
        /// A summary of a previous window that has just ended is added to <paramref name="summaries"/>.
        /// </summary>
        public bool ShouldWrite(LogEntry entry, long nowTicks, List<LogEntry> summaries)
        {
            if (_windowTicks <= 0)
            {
                return true;
            }

            var key = new RepeatKey(entry);
            lock (_gate)
            {
                if (_windows.TryGetValue(key, out Window window))
                {
                    if (nowTicks - window.StartTicks < _windowTicks)
                    {
                        window.SuppressedCount++;
                        window.LastEntry = entry;
                        _suppressedTotal++;
                        return false;
                    }

                    if (window.SuppressedCount > 0)
                    {
                        summaries.Add(window.LastEntry.WithRepeatCount(window.SuppressedCount));
                    }

                    window.StartTicks = nowTicks;
                    window.SuppressedCount = 0;
                    window.LastEntry = entry;
                    return true;
                }

                if (_windows.Count >= MaxTrackedKeys)
                {
                    DrainLocked(long.MaxValue, summaries, removeAll: true);
                }

                _windows.Add(key, new Window { StartTicks = nowTicks, LastEntry = entry });
                return true;
            }
        }

        /// <summary>
        /// Emits summaries for windows that have ended and forgets windows without repeats.
        /// Called periodically by the writer thread, and with <c>long.MaxValue</c> at shutdown.
        /// </summary>
        public void Flush(long nowTicks, List<LogEntry> summaries)
        {
            lock (_gate)
            {
                DrainLocked(nowTicks, summaries, removeAll: false);
            }
        }

        private void DrainLocked(long nowTicks, List<LogEntry> summaries, bool removeAll)
        {
            List<RepeatKey> expired = null;
            foreach (KeyValuePair<RepeatKey, Window> pair in _windows)
            {
                Window window = pair.Value;
                bool ended = removeAll || nowTicks == long.MaxValue || nowTicks - window.StartTicks >= _windowTicks;
                if (!ended)
                {
                    continue;
                }

                if (window.SuppressedCount > 0)
                {
                    summaries.Add(window.LastEntry.WithRepeatCount(window.SuppressedCount));
                }

                (expired ??= new List<RepeatKey>()).Add(pair.Key);
            }

            if (expired != null)
            {
                foreach (RepeatKey key in expired)
                {
                    _windows.Remove(key);
                }
            }
        }

        private sealed class Window
        {
            public long StartTicks;
            public int SuppressedCount;
            public LogEntry LastEntry;
        }

        private readonly struct RepeatKey : IEquatable<RepeatKey>
        {
            private readonly LogLevel _level;
            private readonly string _module;
            private readonly string _category;
            private readonly string _message;
            private readonly string _exceptionType;

            public RepeatKey(LogEntry entry)
            {
                _level = entry.Level;
                _module = entry.Module;
                _category = entry.Category;
                _message = entry.Message;
                _exceptionType = entry.ExceptionType;
            }

            public bool Equals(RepeatKey other)
            {
                return _level == other._level
                    && string.Equals(_module, other._module, StringComparison.Ordinal)
                    && string.Equals(_category, other._category, StringComparison.Ordinal)
                    && string.Equals(_message, other._message, StringComparison.Ordinal)
                    && string.Equals(_exceptionType, other._exceptionType, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is RepeatKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int)_level;
                    hash = (hash * 397) ^ (_module?.GetHashCode() ?? 0);
                    hash = (hash * 397) ^ (_category?.GetHashCode() ?? 0);
                    hash = (hash * 397) ^ (_message?.GetHashCode() ?? 0);
                    hash = (hash * 397) ^ (_exceptionType?.GetHashCode() ?? 0);
                    return hash;
                }
            }
        }
    }
}
