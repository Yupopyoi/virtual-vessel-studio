using System;

namespace VirtualVessel.Diagnostics.Performance
{
    /// <summary>
    /// Creates measurements. Passed to services by the composition root, like <c>ILogProvider</c>.
    /// </summary>
    /// <remarks>
    /// Obtain measurements once (for example in a constructor) and keep them in fields; the
    /// measuring calls themselves never allocate.
    /// </remarks>
    public interface IPerformanceMetrics
    {
        /// <summary>Whether timers and gauges are currently recorded. Counters are always recorded.</summary>
        bool IsDetailedEnabled { get; }

        /// <param name="budget">Optional time budget; exceeding it is counted.</param>
        PerfTimer Timer(string module, string name, TimeSpan? budget = null);

        PerfCounter Counter(string module, string name);

        PerfGauge Gauge(string module, string name, string unit = null);

        PerformanceSnapshot GetSnapshot();
    }
}
