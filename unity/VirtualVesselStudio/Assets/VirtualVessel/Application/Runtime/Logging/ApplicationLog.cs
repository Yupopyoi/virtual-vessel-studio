using System;
using System.Collections.Generic;

namespace VirtualVessel.Application.Logging
{
    internal enum ApplicationLogLevel
    {
        Information,
        Warning,
        Error,
    }

    internal readonly struct ApplicationLogEntry
    {
        public ApplicationLogEntry(DateTimeOffset timestampUtc, ApplicationLogLevel level, string message, Exception exception)
        {
            TimestampUtc = timestampUtc;
            Level = level;
            Message = message;
            Exception = exception;
        }

        public DateTimeOffset TimestampUtc { get; }

        public ApplicationLogLevel Level { get; }

        public string Message { get; }

        public Exception Exception { get; }
    }

    /// <summary>
    /// Minimal log used by the application foundation itself.
    /// </summary>
    /// <remarks>
    /// The logging service (system design Chapter 5) starts as one of the hosted services, so the
    /// foundation needs somewhere to record what happens before and around it.
    /// </remarks>
    internal interface IApplicationLog
    {
        void Write(ApplicationLogLevel level, string message, Exception exception = null);
    }

    /// <summary>
    /// Keeps foundation log entries in memory so they can be replayed into the logging service once it
    /// starts, and forwards each entry to an immediate sink such as the Unity console.
    /// </summary>
    internal sealed class BufferedApplicationLog : IApplicationLog
    {
        // Bounded so that a startup failure loop cannot grow memory without limit.
        public const int Capacity = 1000;

        private readonly object _gate = new object();
        private readonly List<ApplicationLogEntry> _entries = new List<ApplicationLogEntry>();
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly Action<ApplicationLogEntry> _sink;
        private Action<ApplicationLogEntry, bool> _forwarder;
        private int _droppedCount;

        public BufferedApplicationLog(Func<DateTimeOffset> utcNow, Action<ApplicationLogEntry> sink)
        {
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            _sink = sink;
        }

        public int DroppedCount
        {
            get
            {
                lock (_gate)
                {
                    return _droppedCount;
                }
            }
        }

        public void Write(ApplicationLogLevel level, string message, Exception exception = null)
        {
            var entry = new ApplicationLogEntry(_utcNow(), level, message, exception);
            lock (_gate)
            {
                if (_forwarder != null)
                {
                    // The logging service now owns console output and files; do not echo or buffer.
                    _forwarder(entry, false);
                    return;
                }

                if (_entries.Count < Capacity)
                {
                    _entries.Add(entry);
                }
                else
                {
                    _droppedCount++;
                }
            }

            _sink?.Invoke(entry);
        }

        /// <summary>
        /// Replays every buffered entry to <paramref name="forwarder"/>, then sends all later entries
        /// there instead of the immediate sink. The second argument is true for replayed entries,
        /// which were already shown by the immediate sink.
        /// </summary>
        /// <remarks>
        /// Replay and switch-over happen under the lock so that no entry is lost or reordered between them.
        /// </remarks>
        public void AttachForwarder(Action<ApplicationLogEntry, bool> forwarder)
        {
            lock (_gate)
            {
                _forwarder = forwarder ?? throw new ArgumentNullException(nameof(forwarder));
                foreach (ApplicationLogEntry entry in _entries)
                {
                    forwarder(entry, true);
                }

                _entries.Clear();
            }
        }

        /// <summary>Returns to buffering and the immediate sink, for example when logging stops.</summary>
        public void DetachForwarder()
        {
            lock (_gate)
            {
                _forwarder = null;
            }
        }

        public IReadOnlyList<ApplicationLogEntry> Snapshot()
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }
}
