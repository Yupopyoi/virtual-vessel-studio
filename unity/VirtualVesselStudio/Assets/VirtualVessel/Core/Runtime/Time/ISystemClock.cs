using System;

namespace VirtualVessel.Core.Time
{
    /// <summary>
    /// Wall-clock time for human-readable timestamps such as log entries.
    /// </summary>
    public interface ISystemClock
    {
        DateTimeOffset UtcNow { get; }
    }
}
