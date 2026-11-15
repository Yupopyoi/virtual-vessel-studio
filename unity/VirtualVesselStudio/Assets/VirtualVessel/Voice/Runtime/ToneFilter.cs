using System;

namespace VirtualVessel.Voice
{
    /// <summary>A biquad's coefficients, normalized so a0 is 1 (Robert Bristow-Johnson's audio EQ cookbook).</summary>
    public readonly struct Biquad
    {
        public Biquad(float b0, float b1, float b2, float a1, float a2)
        {
            B0 = b0;
            B1 = b1;
            B2 = b2;
            A1 = a1;
            A2 = a2;
        }

        public float B0 { get; }

        public float B1 { get; }

        public float B2 { get; }

        public float A1 { get; }

        public float A2 { get; }

        public static Biquad Identity => new Biquad(1f, 0f, 0f, 0f, 0f);
    }

    /// <summary>
    /// Applies a <see cref="VoiceTone"/> to mono samples (Voice detailed design 8.6): four biquads in a row, each kept
    /// between blocks, so a stream processed block by block sounds as one. No look-ahead, so it adds no latency, and it
    /// allocates nothing while processing: it runs on every block of the stream (CLAUDE.md 23). One per stream; not
    /// thread-safe.
    /// </summary>
    public sealed class ToneFilter
    {
        public const float BodyHz = 200f;
        public const float ClarityHz = 500f;
        public const float BrightnessHz = 4000f;
        public const float AirHz = 10000f;

        // A wide bell for the low middle, so it reads as muffled or clear rather than as a tone at one pitch.
        private const float ClarityQ = 0.8f;

        private readonly Biquad[] _bands = new Biquad[4];
        private readonly float[] _z1 = new float[4];
        private readonly float[] _z2 = new float[4];
        private bool _flat = true;

        public ToneFilter(VoiceTone tone, int sampleRate)
        {
            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            SampleRate = sampleRate;
            SetTone(tone);
        }

        public int SampleRate { get; }

        public VoiceTone Tone { get; private set; }

        /// <summary>Changes the finish; the filters' state is kept, so a change while the voice runs does not reset them.</summary>
        public void SetTone(VoiceTone tone)
        {
            Tone = tone;
            _flat = tone.IsFlat;
            Biquad[] bands = Bands(tone, SampleRate);
            Array.Copy(bands, _bands, bands.Length);
        }

        /// <summary>Filters <paramref name="samples"/> in place. A flat tone leaves them as they are.</summary>
        public void Process(Span<float> samples)
        {
            if (_flat)
            {
                return;
            }

            for (int band = 0; band < _bands.Length; band++)
            {
                Biquad q = _bands[band];
                if (q.B0 == 1f && q.B1 == 0f && q.B2 == 0f && q.A1 == 0f && q.A2 == 0f)
                {
                    continue;
                }

                // Transposed direct form II: two state values per band.
                float z1 = _z1[band];
                float z2 = _z2[band];
                for (int i = 0; i < samples.Length; i++)
                {
                    float x = samples[i];
                    float y = (q.B0 * x) + z1;
                    z1 = (q.B1 * x) - (q.A1 * y) + z2;
                    z2 = (q.B2 * x) - (q.A2 * y);
                    samples[i] = y;
                }

                _z1[band] = z1;
                _z2[band] = z2;
            }
        }

        /// <summary>A copy of <paramref name="samples"/> with <paramref name="tone"/>, as a trial's recording is finished. Pure.</summary>
        public static float[] Apply(ReadOnlySpan<float> samples, VoiceTone tone, int sampleRate)
        {
            float[] finished = samples.ToArray();
            new ToneFilter(tone, sampleRate).Process(finished);
            return finished;
        }

        /// <summary>The four bands for a tone; a band at 0 dB is the identity. Pure.</summary>
        public static Biquad[] Bands(VoiceTone tone, int sampleRate)
        {
            return new[]
            {
                LowShelf(BodyHz, tone.Body, sampleRate),
                Peaking(ClarityHz, -tone.Clarity, ClarityQ, sampleRate),
                HighShelf(BrightnessHz, tone.Brightness, sampleRate),
                HighShelf(AirHz, tone.Air, sampleRate),
            };
        }

        /// <summary>The gain of <paramref name="band"/> at <paramref name="hz"/>, in dB, for tests and diagnostics. Pure.</summary>
        public static double GainDb(Biquad band, double hz, int sampleRate)
        {
            double w = 2.0 * Math.PI * hz / sampleRate;
            double cos1 = Math.Cos(w), sin1 = Math.Sin(w), cos2 = Math.Cos(2.0 * w), sin2 = Math.Sin(2.0 * w);
            double numRe = band.B0 + (band.B1 * cos1) + (band.B2 * cos2);
            double numIm = -(band.B1 * sin1) - (band.B2 * sin2);
            double denRe = 1.0 + (band.A1 * cos1) + (band.A2 * cos2);
            double denIm = -(band.A1 * sin1) - (band.A2 * sin2);
            double magnitude = Math.Sqrt(((numRe * numRe) + (numIm * numIm)) / ((denRe * denRe) + (denIm * denIm)));
            return 20.0 * Math.Log10(magnitude);
        }

        /// <summary>A low shelf with a slope of 1. Pure.</summary>
        public static Biquad LowShelf(float hz, float decibels, int sampleRate)
        {
            if (decibels == 0f)
            {
                return Biquad.Identity;
            }

            (double a, double cos, double alpha) = Terms(hz, decibels, sampleRate, Math.Sqrt(2.0) / 2.0);
            double root = 2.0 * Math.Sqrt(a) * alpha;
            double a0 = (a + 1) + ((a - 1) * cos) + root;
            return Normalized(
                a * ((a + 1) - ((a - 1) * cos) + root),
                2 * a * ((a - 1) - ((a + 1) * cos)),
                a * ((a + 1) - ((a - 1) * cos) - root),
                a0,
                -2 * ((a - 1) + ((a + 1) * cos)),
                (a + 1) + ((a - 1) * cos) - root);
        }

        /// <summary>A high shelf with a slope of 1. Pure.</summary>
        public static Biquad HighShelf(float hz, float decibels, int sampleRate)
        {
            if (decibels == 0f)
            {
                return Biquad.Identity;
            }

            (double a, double cos, double alpha) = Terms(hz, decibels, sampleRate, Math.Sqrt(2.0) / 2.0);
            double root = 2.0 * Math.Sqrt(a) * alpha;
            double a0 = (a + 1) - ((a - 1) * cos) + root;
            return Normalized(
                a * ((a + 1) + ((a - 1) * cos) + root),
                -2 * a * ((a - 1) + ((a + 1) * cos)),
                a * ((a + 1) + ((a - 1) * cos) - root),
                a0,
                2 * ((a - 1) - ((a + 1) * cos)),
                (a + 1) - ((a - 1) * cos) - root);
        }

        /// <summary>A bell at <paramref name="hz"/>. Pure.</summary>
        public static Biquad Peaking(float hz, float decibels, float q, int sampleRate)
        {
            if (decibels == 0f)
            {
                return Biquad.Identity;
            }

            (double a, double cos, double alpha) = Terms(hz, decibels, sampleRate, q);
            return Normalized(1 + (alpha * a), -2 * cos, 1 - (alpha * a), 1 + (alpha / a), -2 * cos, 1 - (alpha / a));
        }

        // A: the amplitude; alpha from the bandwidth, as Q for a bell, or the slope's form for a shelf.
        private static (double A, double Cos, double Alpha) Terms(float hz, float decibels, int sampleRate, double q)
        {
            double a = Math.Pow(10.0, decibels / 40.0);
            double w0 = 2.0 * Math.PI * Math.Min(hz, sampleRate * 0.45) / sampleRate;
            return (a, Math.Cos(w0), Math.Sin(w0) / (2.0 * q));
        }

        private static Biquad Normalized(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            return new Biquad((float)(b0 / a0), (float)(b1 / a0), (float)(b2 / a0), (float)(a1 / a0), (float)(a2 / a0));
        }
    }
}
