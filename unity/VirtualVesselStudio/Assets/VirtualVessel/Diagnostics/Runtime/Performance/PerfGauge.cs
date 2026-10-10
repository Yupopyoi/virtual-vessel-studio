using VirtualVessel.Diagnostics.Performance.Recording;

namespace VirtualVessel.Diagnostics.Performance
{
    /// <summary>Records a value at a point in time, such as a queue length or an A/V offset.</summary>
    public sealed class PerfGauge : Metric
    {
        private readonly DetailedSwitch _switch;
        private readonly SampleRing _samples;

        internal PerfGauge(string module, string name, string unit, DetailedSwitch detailedSwitch, int sampleCapacity)
            : base(module, name, MetricKind.Gauge, unit)
        {
            _switch = detailedSwitch;
            _samples = new SampleRing(sampleCapacity);
        }

        public void Set(double value)
        {
            if (_switch.IsEnabled)
            {
                _samples.Add(value);
            }
        }

        internal override MetricSnapshot CreateSnapshot()
        {
            return new MetricSnapshot(this, _samples.ComputeStatistics(), 0, null);
        }
    }
}
