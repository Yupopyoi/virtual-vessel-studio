using System;
using System.Collections.Generic;

namespace VirtualVessel.Diagnostics.Logging.Recent
{
    /// <summary>
    /// Keeps the most recent entries in memory for the log viewer and diagnostics snapshot
    /// (system design 5.19, 5.20). Also acts as a sink so it sees exactly what was written.
    /// </summary>
    internal sealed class RecentLogBuffer : Writing.ILogSink
    {
        private readonly object _gate = new object();
        private readonly LogEntry[] _entries;
        private int _next;
        private int _count;

        public RecentLogBuffer(int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _entries = new LogEntry[capacity];
        }

        public string Name => "Recent";

        public void Write(LogEntry entry)
        {
            lock (_gate)
            {
                _entries[_next] = entry;
                _next = (_next + 1) % _entries.Length;
                if (_count < _entries.Length)
                {
                    _count++;
                }
            }
        }

        /// <summary>Returns entries oldest first.</summary>
        public IReadOnlyList<LogEntry> Snapshot()
        {
            lock (_gate)
            {
                var result = new LogEntry[_count];
                int start = (_next - _count + _entries.Length) % _entries.Length;
                for (int i = 0; i < _count; i++)
                {
                    result[i] = _entries[(start + i) % _entries.Length];
                }

                return result;
            }
        }

        public void Flush()
        {
        }

        public void Dispose()
        {
        }
    }
}
