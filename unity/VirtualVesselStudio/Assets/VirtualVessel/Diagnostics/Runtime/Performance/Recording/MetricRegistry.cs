using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace VirtualVessel.Diagnostics.Performance.Recording
{
    /// <summary>
    /// Holds every measurement by module and name, so that classes asking for the same name share one
    /// instance. Registration is thread-safe; it is expected at construction time, not per call.
    /// </summary>
    internal sealed class MetricRegistry
    {
        private readonly ConcurrentDictionary<string, Metric> _metrics = new ConcurrentDictionary<string, Metric>(StringComparer.Ordinal);

        public T GetOrAdd<T>(string module, string name, MetricKind kind, Func<T> create)
            where T : Metric
        {
            if (string.IsNullOrWhiteSpace(module))
            {
                throw new ArgumentException("Module must not be empty.", nameof(module));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Name must not be empty.", nameof(name));
            }

            Metric metric = _metrics.GetOrAdd(module + "/" + name, _ => create());
            if (metric is T typed)
            {
                return typed;
            }

            // Two modules using one name for different kinds is a development mistake; fail loudly.
            throw new InvalidOperationException($"Metric '{module}/{name}' is already registered as {metric.Kind}, not {kind}.");
        }

        public void AppendSnapshots(List<MetricSnapshot> target)
        {
            foreach (Metric metric in _metrics.Values.OrderBy(m => m.Module, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal))
            {
                target.Add(metric.CreateSnapshot());
            }
        }
    }
}
