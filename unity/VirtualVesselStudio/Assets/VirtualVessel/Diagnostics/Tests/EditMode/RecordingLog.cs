using System;
using System.Collections.Generic;
using VirtualVessel.Diagnostics.Logging;

namespace VirtualVessel.Diagnostics.Tests
{
    /// <summary>An <see cref="ILog"/> that keeps entries in memory so tests can inspect them.</summary>
    internal sealed class RecordingLog : ILog
    {
        private readonly object _gate = new object();
        private readonly List<(LogLevel Level, string Message)> _entries = new List<(LogLevel, string)>();

        public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_gate)
                {
                    return _entries.ToArray();
                }
            }
        }

        public bool IsEnabled(LogLevel level)
        {
            return level >= MinimumLevel;
        }

        public void Write(LogLevel level, string message, Exception exception = null)
        {
            Write(level, message, null, exception);
        }

        public void Write(LogLevel level, string message, IReadOnlyList<LogProperty> properties, Exception exception = null)
        {
            if (!IsEnabled(level))
            {
                return;
            }

            lock (_gate)
            {
                _entries.Add((level, message));
            }
        }

        public ILog WithContext(string key, string value)
        {
            return this;
        }
    }
}
