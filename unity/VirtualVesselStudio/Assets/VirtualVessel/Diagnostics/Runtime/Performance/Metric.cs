namespace VirtualVessel.Diagnostics.Performance
{
    public enum MetricKind
    {
        Timer,
        Counter,
        Gauge,
    }

    /// <summary>Common identity of a timer, counter, or gauge.</summary>
    public abstract class Metric
    {
        internal Metric(string module, string name, MetricKind kind, string unit)
        {
            Module = module;
            Name = name;
            Kind = kind;
            Unit = unit;
        }

        public string Module { get; }

        public string Name { get; }

        public MetricKind Kind { get; }

        public string Unit { get; }

        internal abstract MetricSnapshot CreateSnapshot();
    }
}
