using System;

namespace VirtualVessel.Core.Time
{
    public sealed class UtcSystemClock : ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
