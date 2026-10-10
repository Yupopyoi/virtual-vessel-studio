using System;
using System.Collections.Generic;

namespace VirtualVessel.Diagnostics.Performance
{
    /// <summary>Statistics of all measurements, ordered by module and name.</summary>
    public sealed class PerformanceSnapshot
    {
        internal PerformanceSnapshot(DateTimeOffset takenAtUtc, bool detailedEnabled, IReadOnlyList<MetricSnapshot> metrics)
        {
            TakenAtUtc = takenAtUtc;
            DetailedEnabled = detailedEnabled;
            Metrics = metrics;
        }

        public DateTimeOffset TakenAtUtc { get; }

        public bool DetailedEnabled { get; }

        public IReadOnlyList<MetricSnapshot> Metrics { get; }
    }
}
