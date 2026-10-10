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

        public IReadOnlyList<ApplicationLogEntry> Snapshot()
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }
}
