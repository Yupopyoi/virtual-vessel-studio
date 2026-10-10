using System;
using System.Collections.Generic;

namespace VirtualVessel.Diagnostics.Logging
{
    /// <summary>
    /// Level-specific shorthands for <see cref="ILog"/>.
    /// </summary>
    /// <remarks>
    /// Overloads are explicit rather than using params arrays so that a disabled level never allocates.
    /// </remarks>
    public static class LogExtensions
    {
        public static void Trace(this ILog log, string message, Exception exception = null)
        {
            log.Write(LogLevel.Trace, message, exception);
        }

        public static void Trace(this ILog log, string message, IReadOnlyList<LogProperty> properties, Exception exception = null)
        {
            log.Write(LogLevel.Trace, message, properties, exception);
        }

        public static void Debug(this ILog log, string message, Exception exception = null)
        {
            log.Write(LogLevel.Debug, message, exception);
        }

        public static void Debug(this ILog log, string message, IReadOnlyList<LogProperty> properties, Exception exception = null)
        {
            log.Write(LogLevel.Debug, message, properties, exception);
        }

        public static void Information(this ILog log, string message, Exception exception = null)
        {
            log.Write(LogLevel.Information, message, exception);
        }

        public static void Information(this ILog log, string message, IReadOnlyList<LogProperty> properties, Exception exception = null)
        {
            log.Write(LogLevel.Information, message, properties, exception);
        }

        public static void Warning(this ILog log, string message, Exception exception = null)
        {
            log.Write(LogLevel.Warning, message, exception);
        }

        public static void Warning(this ILog log, string message, IReadOnlyList<LogProperty> properties, Exception exception = null)
        {
            log.Write(LogLevel.Warning, message, properties, exception);
        }

        public static void Error(this ILog log, string message, Exception exception = null)
        {
            log.Write(LogLevel.Error, message, exception);
        }

        public static void Error(this ILog log, string message, IReadOnlyList<LogProperty> properties, Exception exception = null)
        {
            log.Write(LogLevel.Error, message, properties, exception);
        }

        public static void Critical(this ILog log, string message, Exception exception = null)
        {
            log.Write(LogLevel.Critical, message, exception);
        }

        public static void Critical(this ILog log, string message, IReadOnlyList<LogProperty> properties, Exception exception = null)
        {
            log.Write(LogLevel.Critical, message, properties, exception);
        }
    }
}
