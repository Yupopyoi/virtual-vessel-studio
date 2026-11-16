using System;

namespace VirtualVessel.Voice
{
    /// <summary>
    /// Keeps a converted voice under full scale after its finish (system design 9.9; Voice detailed design 8.6): a lifted
    /// band can push a loud voice past it, and the stream or a WAV would clip it. The gain drops at once on a peak above
    /// the ceiling and comes back over the release, so it needs no look-ahead and adds no latency (the stream is over its
    /// 200 ms already, Voice detailed design 15). It allocates nothing while processing. One per stream; not thread-safe.
    /// </summary>
    public sealed class PeakLimiter
    {
        /// <summary>About -1 dBFS: room for the resampler and the mixer after it.</summary>
        public const float Ceiling = 0.89f;

        public const float ReleaseSeconds = 0.08f;

        private readonly float _release;
        private float _gain = 1f;

        public PeakLimiter(int sampleRate)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            _release = ReleaseStep(sampleRate);
        }

        /// <summary>The gain applied to the last sample, for tests and diagnostics: 1 when nothing is held down.</summary>
        public float Gain => _gain;

        /// <summary>Limits <paramref name="samples"/> in place.</summary>
        public void Process(Span<float> samples)
        {
            float gain = _gain;
            for (int i = 0; i < samples.Length; i++)
            {
                float x = samples[i];
                gain = NextGain(gain, Math.Abs(x), _release);
                samples[i] = x * gain;
            }

            _gain = gain;
        }

        /// <summary>
        /// The gain for a sample of <paramref name="level"/>: back toward 1 by <paramref name="release"/> of the way, but no
        /// higher than keeps it at the ceiling. Pure.
        /// </summary>
        public static float NextGain(float gain, float level, float release)
        {
            // Never above what this sample allows, so coming back after a peak cannot carry the next one over.
            float needed = level > Ceiling ? Ceiling / level : 1f;
            return Math.Min(needed, gain + ((1f - gain) * release));
        }

        /// <summary>The share of the way back to 1 per sample, so the gain recovers over <see cref="ReleaseSeconds"/>. Pure.</summary>
        public static float ReleaseStep(int sampleRate)
        {
            return 1f - (float)Math.Exp(-1.0 / (ReleaseSeconds * sampleRate));
        }
    }
}
