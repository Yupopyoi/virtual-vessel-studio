using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance.Builtin;
using VirtualVessel.Diagnostics.Performance.Recording;

namespace VirtualVessel.Diagnostics.Performance
{
    /// <summary>
    /// Owns all measurements: registration, the detailed on/off switch, built-in metrics, and the
    /// periodic log summary.
    /// </summary>
    /// <remarks>
    /// Measurements can be obtained and recorded before <see cref="InitializeAsync"/>; initialization
    /// only starts the built-in collector and the summary timer.
    /// </remarks>
    public sealed class PerformanceMetricsService : IApplicationService, IPerformanceMetrics
    {
        public const string ServiceName = "PerformanceMetrics";

        private readonly PerformanceSettings _settings;
        private readonly IMonotonicClock _clock;
        private readonly ISystemClock _systemClock;
        private readonly ILog _log;
        private readonly DetailedSwitch _switch;
        private readonly MetricRegistry _registry = new MetricRegistry();

        // Serializes the built-in collector between the main thread (start/stop) and snapshot readers,
        // which include the summary timer's thread-pool thread.
        private readonly object _builtinGate = new object();
        private readonly BuiltinMetricsCollector _builtin = new BuiltinMetricsCollector();
        private bool _unavailableReported;

        // Fully qualified: the Timer method below hides the System.Threading.Timer type name.
        private System.Threading.Timer _summaryTimer;
        private bool _initialized;
        private bool _disposed;

        /// <param name="log">Optional; null disables the periodic summary and the exit totals.</param>
        public PerformanceMetricsService(PerformanceSettings settings, IMonotonicClock clock, ISystemClock systemClock, ILog log)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.Validate();
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _systemClock = systemClock ?? throw new ArgumentNullException(nameof(systemClock));
            _log = log;
            _switch = new DetailedSwitch(settings.DetailedEnabled);
        }

        public string Name => ServiceName;

        public bool IsDetailedEnabled => _switch.IsEnabled;

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            lock (_builtinGate)
            {
                _initialized = true;
                UpdateBuiltinCollectorLocked();
            }

            if (_log != null && _settings.SummaryInterval > TimeSpan.Zero)
            {
                _summaryTimer = new System.Threading.Timer(_ => WriteSummary(), null, _settings.SummaryInterval, _settings.SummaryInterval);
            }

            _log?.Information(
                "Performance metrics started.",
                new[]
                {
                    new LogProperty("DetailedEnabled", _switch.IsEnabled),
                    new LogProperty("SampleCapacity", _settings.SampleCapacity),
                });
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            StopSummaryTimer();
            WriteExitTotals();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            StopSummaryTimer();
            lock (_builtinGate)
            {
                _disposed = true;
                _builtin.Dispose();
            }
        }

        /// <summary>Turns detailed recording on or off at runtime. Call from the main thread.</summary>
        public void SetDetailedEnabled(bool enabled)
        {
            if (_switch.IsEnabled == enabled)
            {
                return;
            }

            _switch.IsEnabled = enabled;
            lock (_builtinGate)
            {
                UpdateBuiltinCollectorLocked();
            }

            _log?.Information(enabled ? "Detailed performance metrics enabled." : "Detailed performance metrics disabled.");
        }

        public PerfTimer Timer(string module, string name, TimeSpan? budget = null)
        {
            return _registry.GetOrAdd(module, name, MetricKind.Timer, () => new PerfTimer(module, name, budget, _switch, _clock, _settings.SampleCapacity));
        }

        public PerfCounter Counter(string module, string name)
        {
            return _registry.GetOrAdd(module, name, MetricKind.Counter, () => new PerfCounter(module, name));
        }

        public PerfGauge Gauge(string module, string name, string unit = null)
        {
            return _registry.GetOrAdd(module, name, MetricKind.Gauge, () => new PerfGauge(module, name, unit, _switch, _settings.SampleCapacity));
        }

        public PerformanceSnapshot GetSnapshot()
        {
            var snapshots = new List<MetricSnapshot>();
            _registry.AppendSnapshots(snapshots);
            lock (_builtinGate)
            {
                _builtin.AppendSnapshots(snapshots);
            }

            return new PerformanceSnapshot(_systemClock.UtcNow, _switch.IsEnabled, snapshots);
        }

        private void UpdateBuiltinCollectorLocked()
        {
            bool shouldRun = _initialized && !_disposed && _settings.BuiltinMetricsEnabled && _switch.IsEnabled;
            if (shouldRun && !_builtin.IsRunning)
            {
                _builtin.Start();
                ReportUnavailableLocked();
            }
            else if (!shouldRun && _builtin.IsRunning)
            {
                _builtin.Dispose();
            }
        }

        private void ReportUnavailableLocked()
        {
            // Availability depends on the platform, not on time, so reporting once per session is enough.
            if (_unavailableReported || _builtin.Unavailable.Count == 0)
            {
                return;
            }

            _unavailableReported = true;
            _log?.Information(
                "Some built-in performance metrics are unavailable on this platform.",
                new[] { new LogProperty("Metrics", string.Join(", ", _builtin.Unavailable)) });
        }

        private void StopSummaryTimer()
        {
            System.Threading.Timer timer = Interlocked.Exchange(ref _summaryTimer, null);
            timer?.Dispose();
        }

        private void WriteSummary()
        {
            try
            {
                if (!_switch.IsEnabled || _log == null || !_log.IsEnabled(LogLevel.Debug))
                {
                    return;
                }

                string text = Format(GetSnapshot().Metrics.Where(m => m.Kind != MetricKind.Counter && m.SampleCount > 0));
                if (text.Length > 0)
                {
                    _log.Debug("Performance summary:\n" + text);
                }
            }
            catch (Exception exception)
            {
                // A timer callback exception would otherwise be lost on the thread pool.
                _log?.Warning("Failed to write the performance summary.", exception);
            }
        }

        private void WriteExitTotals()
        {
            if (_log == null)
            {
                return;
            }

            PerformanceSnapshot snapshot = GetSnapshot();
            IEnumerable<MetricSnapshot> counters = snapshot.Metrics.Where(m => m.Kind == MetricKind.Counter && m.Last != 0);
            IEnumerable<MetricSnapshot> timers = snapshot.DetailedEnabled
                ? snapshot.Metrics.Where(m => m.Kind == MetricKind.Timer && m.SampleCount > 0)
                : Enumerable.Empty<MetricSnapshot>();

            string text = Format(counters.Concat(timers));
            if (text.Length > 0)
            {
                _log.Information("Performance totals at exit:\n" + text);
            }
        }

        internal static string Format(IEnumerable<MetricSnapshot> metrics)
        {
            var builder = new StringBuilder();
            foreach (MetricSnapshot m in metrics)
            {
                builder.Append("  ").Append(m.Module).Append('/').Append(m.Name).Append(": ");
                if (m.Kind == MetricKind.Counter)
                {
                    builder.Append(m.Last.ToString("F0", CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "last {0:F2} avg {1:F2} p95 {2:F2} p99 {3:F2} max {4:F2}",
                        m.Last, m.Average, m.P95, m.P99, m.Max);
                    if (m.Unit != null)
                    {
                        builder.Append(' ').Append(m.Unit);
                    }

                    builder.Append(" (n=").Append(m.SampleCount).Append(')');
                    if (m.Budget.HasValue)
                    {
                        builder.AppendFormat(CultureInfo.InvariantCulture, ", over {0:0.###} ms budget: {1}", m.Budget.Value.TotalMilliseconds, m.OverBudgetCount);
                    }
                }

                builder.Append('\n');
            }

            return builder.ToString();
        }
    }
}
