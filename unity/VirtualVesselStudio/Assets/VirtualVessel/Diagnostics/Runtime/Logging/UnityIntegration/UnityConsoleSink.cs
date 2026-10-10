using System;
using System.Text;
using VirtualVessel.Diagnostics.Logging.Writing;

namespace VirtualVessel.Diagnostics.Logging.UnityIntegration
{
    /// <summary>
    /// Marks the current thread while it writes to the Unity console, so that
    /// <see cref="UnityLogCapture"/> can ignore the echo of our own output.
    /// </summary>
    internal static class UnityConsoleWriteGuard
    {
        [ThreadStatic]
        private static bool t_isWriting;

        public static bool IsWriting => t_isWriting;

        public static void Run(Action write)
        {
            t_isWriting = true;
            try
            {
                write();
            }
            finally
            {
                t_isWriting = false;
            }
        }
    }

    /// <summary>
    /// Echoes entries to the Unity console for developers watching the Editor.
    /// </summary>
    /// <remarks>
    /// Unity's Debug methods are thread-safe, so this runs on the writer thread like the other sinks.
    /// Entries that came from the Unity console in the first place are skipped.
    /// </remarks>
    internal sealed class UnityConsoleSink : ILogSink
    {
        private readonly LogLevel _minimumLevel;
        private readonly StringBuilder _builder = new StringBuilder(256);

        public UnityConsoleSink(LogLevel minimumLevel)
        {
            _minimumLevel = minimumLevel;
        }

        public string Name => "UnityConsole";

        public void Write(LogEntry entry)
        {
            if (entry.Level < _minimumLevel || entry.ExcludeFromConsole)
            {
                return;
            }

            string text = Format(entry);
            UnityConsoleWriteGuard.Run(() =>
            {
                switch (entry.Level)
                {
                    case LogLevel.Error:
                    case LogLevel.Critical:
                        UnityEngine.Debug.LogError(text);
                        break;
                    case LogLevel.Warning:
                        UnityEngine.Debug.LogWarning(text);
                        break;
                    default:
                        UnityEngine.Debug.Log(text);
                        break;
                }
            });
        }

        public void Flush()
        {
        }

        public void Dispose()
        {
        }

        private string Format(LogEntry entry)
        {
            _builder.Clear();
            _builder.Append('[').Append(entry.Module);
            if (entry.Category != null)
            {
                _builder.Append('/').Append(entry.Category);
            }

            _builder.Append("] ").Append(entry.Message);

            if (entry.RepeatCount > 0)
            {
                _builder.Append(" (repeated ").Append(entry.RepeatCount).Append(" more times)");
            }

            if (entry.ExceptionText != null)
            {
                _builder.Append('\n').Append(entry.ExceptionText);
            }

            return _builder.ToString();
        }
    }
}
