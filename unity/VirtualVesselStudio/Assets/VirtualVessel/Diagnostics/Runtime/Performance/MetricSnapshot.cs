using System;
using VirtualVessel.Diagnostics.Performance.Recording;

namespace VirtualVessel.Diagnostics.Performance
{
    /// <summary>Statistics of one measurement at the time a snapshot was taken.</summary>
    public sealed class MetricSnapshot
    {
        internal MetricSnapshot(Metric metric, SampleStatistics statistics, long overBudgetCount, TimeSpan? budget)
            : this(metric.Module, metric.Name, metric.Kind, metric.Unit, statistics, overBudgetCount, budget)
        {
        }

        internal MetricSnapshot(string module, string name, MetricKind kind, string unit, SampleStatistics statistics, long overBudgetCount, TimeSpan? budget)
        {
            Module = module;
            Name = name;
            Kind = kind;
            Unit = unit;
            Last = statistics.Last;
            Average = statistics.Average;
            Max = statistics.Max;
            P95 = statistics.P95;
            P99 = statistics.P99;
            SampleCount = statistics.SampleCount;
            TotalCount = statistics.TotalCount;
            OverBudgetCount = overBudgetCount;
            Budget = budget;
        }

        public string Module { get; }

        public string Name { get; }

        public MetricKind Kind { get; }

        public string Unit { get; }

        /// <summary>Latest value; for counters, the running total.</summary>
        public double Last { get; }

        public double Average { get; }

        public double Max { get; }

        public double P95 { get; }

        public double P99 { get; }

        /// <summary>Number of recent samples the statistics are based on.</summary>
        public int SampleCount { get; }

        /// <summary>Number of recordings since start.</summary>
        public long TotalCount { get; }

        public TimeSpan? Budget { get; }

        /// <summary>Running total of recordings that exceeded <see cref="Budget"/>.</summary>
        public long OverBudgetCount { get; }
    }
}
