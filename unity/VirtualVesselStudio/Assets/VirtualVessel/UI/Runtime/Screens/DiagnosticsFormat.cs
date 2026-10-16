using System;
using System.Globalization;
using VirtualVessel.Diagnostics.Performance;

namespace VirtualVessel.UI.Screens
{
    /// <summary>The cells of the Diagnostics metrics table (UI foundation detailed design 7.7). Pure.</summary>
    public static class DiagnosticsFormat
    {
        /// <summary>Number of cells per row, matching the columns of the Diagnostics table.</summary>
        public const int ColumnCount = 10;

        /// <summary>
        /// The texts of one row. The kind cell is a string table key; the caller looks it up. Statistics
        /// a counter does not have are empty.
        /// </summary>
        public static string[] Row(MetricSnapshot metric)
        {
            if (metric == null)
            {
                throw new ArgumentNullException(nameof(metric));
            }

            string unit = Unit(metric);
            bool counter = metric.Kind == MetricKind.Counter;
            return new[]
            {
                metric.Module,
                metric.Name,
                KindKey(metric.Kind),
                counter ? Count(metric.Last) : Value(metric.Last, unit),
                counter ? string.Empty : Value(metric.Average, unit),
                counter ? string.Empty : Value(metric.Max, unit),
                counter ? string.Empty : Value(metric.P95, unit),
                counter ? string.Empty : Value(metric.P99, unit),
                counter ? string.Empty : Count(metric.TotalCount),
                metric.Budget.HasValue ? OverBudget(metric.OverBudgetCount, metric.Budget.Value) : string.Empty,
            };
        }

        public static string KindKey(MetricKind kind)
        {
            return kind switch
            {
                MetricKind.Timer => ScreenStrings.KindTimer,
                MetricKind.Counter => ScreenStrings.KindCounter,
                _ => ScreenStrings.KindGauge,
            };
        }

        /// <summary>"1.23 ms", "512 MB": two decimals at most, digits grouped.</summary>
        public static string Value(double value, string unit)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return "-";
            }

            string number = value.ToString("#,0.##", CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(unit) ? number : number + " " + unit;
        }

        public static string Count(double value)
        {
            return value.ToString("#,0", CultureInfo.InvariantCulture);
        }

        /// <summary>"3 (> 16.67 ms)": how often the budget was exceeded, and the budget.</summary>
        public static string OverBudget(long count, TimeSpan budget)
        {
            return Count(count) + " (> " + Value(budget.TotalMilliseconds, "ms") + ")";
        }

        private static string Unit(MetricSnapshot metric)
        {
            return metric.Kind == MetricKind.Timer ? "ms" : metric.Unit;
        }
    }
}
