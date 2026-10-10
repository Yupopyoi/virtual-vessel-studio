using System;

namespace VirtualVessel.Diagnostics.Logging.Writing
{
    /// <summary>
    /// A log destination. Called only from the writer thread, so implementations need no locking
    /// for writes. A sink that throws is disabled and the others keep running.
    /// </summary>
    internal interface ILogSink : IDisposable
    {
        string Name { get; }

        void Write(LogEntry entry);

        void Flush();
    }
}
