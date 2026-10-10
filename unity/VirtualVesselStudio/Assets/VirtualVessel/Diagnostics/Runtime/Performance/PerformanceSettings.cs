using System;

namespace VirtualVessel.Diagnostics.Performance
{
    /// <summary>All tunable performance-metrics values, passed in by the composition root.</summary>
    public sealed class PerformanceSettings
    {
        /// <summary>Initial state of detailed recording. Developer Mode switches it at runtime.</summary>
        public bool DetailedEnabled { get; set; }

        /// <summary>Recent samples per timer or gauge that statistics are computed from.</summary>
        public int SampleCapacity { get; set; } = 512;

        /// <summary>How often statistics are written to the log while detailed metrics are on. Zero disables it.</summary>
        public TimeSpan SummaryInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Frame time, GC, and memory from Unity. Active only while detailed metrics are on.</summary>
        public bool BuiltinMetricsEnabled { get; set; } = true;

        internal void Validate()
        {
            if (SampleCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(SampleCapacity), SampleCapacity, "Must be at least 1.");
            }

            if (SummaryInterval < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(SummaryInterval), SummaryInterval, "Must not be negative.");
            }
        }
    }
}
