using System;
using System.Threading;

namespace VirtualVessel.Diagnostics.Performance.Recording
{
    /// <summary>
    /// Fixed-length ring of recent samples. Writing is lock-free and allocation-free; statistics are
    /// computed by the reader.
    /// </summary>
    /// <remarks>
    /// Concurrent writers may occasionally overwrite each other's slot. That slightly skews diagnostic
    /// statistics and is preferred over making real-time threads wait on a lock.
    /// </remarks>
    internal sealed class SampleRing
    {
        private readonly double[] _samples;
        private long _written;
        private double _last;

        public SampleRing(int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _samples = new double[capacity];
        }

        public void Add(double value)
        {
            long index = Interlocked.Increment(ref _written) - 1;
            _samples[index % _samples.Length] = value;
            Volatile.Write(ref _last, value);
        }

        public SampleStatistics ComputeStatistics()
        {
            long written = Interlocked.Read(ref _written);
            int count = (int)Math.Min(written, _samples.Length);
            if (count == 0)
            {
                return SampleStatistics.Empty;
            }

            var copy = new double[count];
            Array.Copy(_samples, copy, count);
            return SampleStatistics.FromSamples(copy, Volatile.Read(ref _last), written);
        }
    }

    internal readonly struct SampleStatistics
    {
        public static readonly SampleStatistics Empty = default;

        public SampleStatistics(double last, double average, double max, double p95, double p99, int sampleCount, long totalCount)
        {
            Last = last;
            Average = average;
            Max = max;
            P95 = p95;
            P99 = p99;
            SampleCount = sampleCount;
            TotalCount = totalCount;
        }

        public double Last { get; }

        public double Average { get; }

        public double Max { get; }

        public double P95 { get; }

        public double P99 { get; }

        public int SampleCount { get; }

        public long TotalCount { get; }

        /// <summary>Computes statistics over <paramref name="samples"/>, which is sorted in place.</summary>
        public static SampleStatistics FromSamples(double[] samples, double last, long totalCount)
        {
            if (samples.Length == 0)
            {
                return Empty;
            }

            Array.Sort(samples);

            double sum = 0;
            foreach (double value in samples)
            {
                sum += value;
            }

            return new SampleStatistics(
                last,
                sum / samples.Length,
                samples[samples.Length - 1],
                Percentile(samples, 0.95),
                Percentile(samples, 0.99),
                samples.Length,
                totalCount);
        }

        private static double Percentile(double[] sorted, double fraction)
        {
            // Nearest-rank percentile: simple and adequate for diagnostics.
            int rank = (int)Math.Ceiling(fraction * sorted.Length) - 1;
            return sorted[Math.Max(0, Math.Min(rank, sorted.Length - 1))];
        }
    }
}
