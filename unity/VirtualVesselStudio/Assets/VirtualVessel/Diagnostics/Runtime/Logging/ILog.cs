using System;
using System.Collections.Generic;

namespace VirtualVessel.Diagnostics.Logging
{
    /// <summary>
    /// Writes log entries for one module and category.
    /// </summary>
    /// <remarks>
    /// Safe to call from any thread; <see cref="Write(LogLevel, string, Exception)"/> never blocks on I/O.
    /// Each enabled write allocates an entry, so do not log from per-frame or per-audio-buffer code;
    /// log on state changes instead (system design 5.26).
    /// </remarks>
    public interface ILog
    {
        /// <summary>Returns whether entries at <paramref name="level"/> are recorded. Never allocates.</summary>
        bool IsEnabled(LogLevel level);

        void Write(LogLevel level, string message, Exception exception = null);

        void Write(LogLevel level, string message, IReadOnlyList<LogProperty> properties, Exception exception = null);

        /// <summary>
        /// Returns a new log that adds <paramref name="key"/> = <paramref name="value"/> to every entry,
        /// for correlation identifiers such as a training run ID (system design 5.7). This log is not changed.
        /// </summary>
        ILog WithContext(string key, string value);
    }
}
