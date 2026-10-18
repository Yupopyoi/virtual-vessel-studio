using System;
using System.Collections.Generic;

namespace VirtualVessel.Tracking.Recording
{
    /// <summary>
    /// Numbers for choosing smoothing and depth methods by comparison rather than by feel (tracking
    /// detailed design 12). Pure functions over series of samples taken at a fixed rate.
    /// </summary>
    public static class TrackingMetrics
    {
        /// <summary>
        /// The RMS of the change from one sample to the next over [<paramref name="start"/>,
        /// <paramref name="end"/>): how much a value shakes. Over a still interval, lower is steadier.
        /// </summary>
        public static double Jitter(IReadOnlyList<float> series, int start, int end)
        {
            CheckRange(series, start, end);
            double sum = 0;
            int count = 0;
            for (int i = Math.Max(start, 1); i < end; i++)
            {
                double change = series[i] - series[i - 1];
                sum += change * change;
                count++;
            }

            return count == 0 ? 0 : Math.Sqrt(sum / count);
        }

        /// <summary>
        /// How many samples <paramref name="output"/> trails <paramref name="reference"/>: the shift, up to
        /// <paramref name="maxShift"/>, at which the output best matches the earlier reference (least
        /// squared error).
        /// </summary>
        public static int Lag(IReadOnlyList<float> reference, IReadOnlyList<float> output, int maxShift)
        {
            if (reference == null || output == null || reference.Count != output.Count)
            {
                throw new ArgumentException("The series must have the same length.");
            }

            if (maxShift < 0 || maxShift >= output.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(maxShift));
            }

            int best = 0;
            double bestError = double.MaxValue;
            for (int shift = 0; shift <= maxShift; shift++)
            {
                // Compare the same samples for every shift so the errors are comparable.
                double error = 0;
                for (int i = maxShift; i < output.Count; i++)
                {
                    double difference = output[i] - reference[i - shift];
                    error += difference * difference;
                }

                if (error < bestError)
                {
                    bestError = error;
                    best = shift;
                }
            }

            return best;
        }

        /// <summary>The RMS of output minus reference over [<paramref name="start"/>, <paramref name="end"/>).</summary>
        public static double Error(IReadOnlyList<float> reference, IReadOnlyList<float> output, int start, int end)
        {
            CheckRange(output, start, end);
            double sum = 0;
            for (int i = start; i < end; i++)
            {
                double difference = output[i] - reference[i];
                sum += difference * difference;
            }

            return end > start ? Math.Sqrt(sum / (end - start)) : 0;
        }

        /// <summary>
        /// Standard deviation over mean: how much a length that should not change, such as a bone,
        /// varies. An indicator of depth estimation quality. 0 for an empty or zero series.
        /// </summary>
        public static double Variation(IReadOnlyList<float> lengths)
        {
            if (lengths == null || lengths.Count == 0)
            {
                return 0;
            }

            double mean = 0;
            foreach (float length in lengths)
            {
                mean += length;
            }

            mean /= lengths.Count;
            if (mean == 0)
            {
                return 0;
            }

            double variance = 0;
            foreach (float length in lengths)
            {
                variance += (length - mean) * (length - mean);
            }

            return Math.Sqrt(variance / lengths.Count) / Math.Abs(mean);
        }

        /// <summary>Samples outside [<paramref name="min"/>, <paramref name="max"/>], such as a joint past its limit.</summary>
        public static int CountOutside(IReadOnlyList<float> series, float min, float max)
        {
            int count = 0;
            foreach (float value in series)
            {
                if (value < min || value > max)
                {
                    count++;
                }
            }

            return count;
        }

        private static void CheckRange(IReadOnlyList<float> series, int start, int end)
        {
            if (series == null)
            {
                throw new ArgumentNullException(nameof(series));
            }

            if (start < 0 || end > series.Count || start > end)
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }
        }
    }
}
