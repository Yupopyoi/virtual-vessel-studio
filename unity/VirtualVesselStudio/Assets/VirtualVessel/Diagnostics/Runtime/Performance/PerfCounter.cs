using System.Threading;
using VirtualVessel.Diagnostics.Performance.Recording;

namespace VirtualVessel.Diagnostics.Performance
{
    /// <summary>
    /// Counts occurrences. Always recorded, because counts such as audio dropouts cannot be recovered
    /// after the fact.
    /// </summary>
    public sealed class PerfCounter : Metric
    {
        private long _value;

        internal PerfCounter(string module, string name)
            : base(module, name, MetricKind.Counter, null)
        {
        }

        public long Value => Interlocked.Read(ref _value);

        public void Increment()
        {
            Interlocked.Increment(ref _value);
        }

        public void Add(long value)
        {
            Interlocked.Add(ref _value, value);
        }

        internal override MetricSnapshot CreateSnapshot()
        {
            long value = Value;
            return new MetricSnapshot(this, new SampleStatistics(value, 0, 0, 0, 0, 0, value), 0, null);
        }
    }
}
