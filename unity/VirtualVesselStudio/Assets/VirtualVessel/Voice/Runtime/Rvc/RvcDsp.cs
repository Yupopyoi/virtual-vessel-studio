using System;

namespace VirtualVessel.Voice.Rvc
{
    /// <summary>
    /// The signal work around the RVC models, without models or Unity. Pure: results depend only on the
    /// arguments, and the methods that run per block fill the caller's buffers so that the voice path
    /// allocates nothing (CLAUDE.md 15.1, 23).
    /// </summary>
    public static class RvcDsp
    {
        public const int ModelRate = 16000;
        public const int HopSamples = 160;
        public const int FftSize = 1024;
        public const int MelBins = 128;
        public const int PitchBins = 360;

        private const double MelMinHz = 30.0;
        private const double MelMaxHz = 8000.0;
        private const float MelFloor = 1e-5f;
        private const double CentsPerBin = 20.0;
        private const double CentsOffset = 1997.3794084376191;
        private const float CoarseMinHz = 50f;
        private const float CoarseMaxHz = 1100f;
        private const int LocalBins = 4;
        private const int KernelHalfWidth = 16;
        private const int CoarseStride = 8;
        private const int FineStride = 2;

        private static readonly int[] s_binStarts = MakeBins(BasisBinStart);
        private static readonly int[] s_binEnds = MakeBins(BasisBinEnd);

        public static float[] HannWindow(int size)
        {
            // Periodic, as torch.hann_window is by default.
            var window = new float[size];
            for (int n = 0; n < size; n++)
            {
                window[n] = (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * n / size));
            }

            return window;
        }

        /// <summary>
        /// librosa.filters.mel(sr=16000, n_fft=1024, n_mels=128, fmin=30, fmax=8000, htk=True) with its
        /// default Slaney area norm, by mel row.
        /// </summary>
        public static float[] MelBasis()
        {
            int bins = FftSize / 2 + 1;
            var basis = new float[MelBins * bins];
            double low = HzToMel(MelMinHz);
            double high = HzToMel(MelMaxHz);
            var edges = new double[MelBins + 2];
            for (int i = 0; i < edges.Length; i++)
            {
                edges[i] = MelToHz(low + (high - low) * i / (edges.Length - 1));
            }

            for (int mel = 0; mel < MelBins; mel++)
            {
                double left = edges[mel];
                double center = edges[mel + 1];
                double right = edges[mel + 2];
                double norm = 2.0 / (right - left);
                for (int bin = 0; bin < bins; bin++)
                {
                    double hz = bin * (double)ModelRate / FftSize;
                    double weight = Math.Max(0.0, Math.Min((hz - left) / (center - left), (right - hz) / (right - center)));
                    basis[mel * bins + bin] = (float)(weight * norm);
                }
            }

            return basis;
        }

        /// <summary>
        /// The log mel RMVPE expects, [mel, frame], with torch.stft's centered reflect padding.
        /// <paramref name="real"/> and <paramref name="imaginary"/> are scratch of FftSize samples.
        /// </summary>
        public static void LogMel(ReadOnlySpan<float> audio, int frames, int firstFrame, float[] window, float[] basis, Span<float> mel, float[] real, float[] imaginary)
        {
            int bins = FftSize / 2 + 1;
            for (int frame = firstFrame; frame < frames; frame++)
            {
                int start = frame * HopSamples - FftSize / 2;
                for (int n = 0; n < FftSize; n++)
                {
                    real[n] = audio[Reflect(start + n, audio.Length)] * window[n];
                    imaginary[n] = 0f;
                }

                Fft(real, imaginary);
                for (int bin = 0; bin < bins; bin++)
                {
                    real[bin] = MathF.Sqrt(real[bin] * real[bin] + imaginary[bin] * imaginary[bin]);
                }

                for (int row = 0; row < MelBins; row++)
                {
                    float sum = 0f;
                    int offset = row * bins;
                    int last = s_binEnds[row];
                    for (int bin = s_binStarts[row]; bin < last; bin++)
                    {
                        sum += basis[offset + bin] * real[bin];
                    }

                    mel[row * frames + frame] = MathF.Log(MathF.Max(sum, MelFloor));
                }
            }
        }

        /// <summary>
        /// Moves the [mel, frame] picture left by <paramref name="shift"/> frames, so that frames already
        /// computed need no second FFT: at a block every 10 ms frame would otherwise be done many times.
        /// </summary>
        public static void ShiftMel(Span<float> mel, int frames, int shift)
        {
            for (int row = 0; row < MelBins; row++)
            {
                Span<float> line = mel.Slice(row * frames, frames);
                line.Slice(shift).CopyTo(line);
            }
        }

        /// <summary>
        /// How much the sound changes over a [mel, frame] picture's frames <paramref name="first"/> to
        /// <paramref name="first"/> + <paramref name="steps"/>: the mean over the steps of the RMS over mel
        /// rows of each step. A held note changes little and speech much. Pure.
        /// </summary>
        public static float MelChange(ReadOnlySpan<float> mel, int frames, int first, int steps)
        {
            double total = 0.0;
            for (int step = 0; step < steps; step++)
            {
                double sum = 0.0;
                for (int row = 0; row < MelBins; row++)
                {
                    int at = row * frames + first + step;
                    float change = mel[at + 1] - mel[at];
                    sum += change * change;
                }

                total += Math.Sqrt(sum / MelBins);
            }

            return (float)(total / steps);
        }

        /// <summary>The root mean square of the samples. Pure.</summary>
        public static float Rms(ReadOnlySpan<float> samples)
        {
            double sum = 0.0;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += samples[i] * samples[i];
            }

            return (float)Math.Sqrt(sum / Math.Max(1, samples.Length));
        }

        /// <summary>Hz for each frame of RMVPE's salience, [frame, bin]; 0 for an unvoiced frame.</summary>
        public static void DecodeF0(ReadOnlySpan<float> salience, int frames, float threshold, Span<float> f0)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                ReadOnlySpan<float> bins = salience.Slice(frame * PitchBins, PitchBins);
                int peak = 0;
                for (int bin = 1; bin < PitchBins; bin++)
                {
                    if (bins[bin] > bins[peak])
                    {
                        peak = bin;
                    }
                }

                if (bins[peak] <= threshold)
                {
                    f0[frame] = 0f;
                    continue;
                }

                double weighted = 0.0;
                double total = 0.0;
                int last = Math.Min(PitchBins - 1, peak + LocalBins);
                for (int bin = Math.Max(0, peak - LocalBins); bin <= last; bin++)
                {
                    weighted += bins[bin] * (CentsPerBin * bin + CentsOffset);
                    total += bins[bin];
                }

                f0[frame] = (float)(10.0 * Math.Pow(2.0, weighted / total / 1200.0));
            }
        }

        /// <summary>RVC's 1 to 255 pitch token for the pitch embedding.</summary>
        public static int CoarsePitch(float f0)
        {
            if (f0 <= 0f)
            {
                return 1;
            }

            float low = Mel1127(CoarseMinHz);
            float high = Mel1127(CoarseMaxHz);
            float coarse = (Mel1127(f0) - low) * 254f / (high - low) + 1f;
            return Math.Clamp((int)MathF.Round(coarse), 1, 255);
        }

        /// <summary>
        /// Fills the unvoiced frames with the nearest voiced neighbours, as upstream's realtime path
        /// does, so a short gap does not silence the vocoder. A window with no voice stays at 0.
        /// </summary>
        public static void FillUnvoiced(Span<float> f0, float fallback)
        {
            int previous = -1;
            for (int i = 0; i < f0.Length; i++)
            {
                if (f0[i] <= 0f)
                {
                    continue;
                }

                float start = previous < 0 ? (fallback > 0f ? fallback : f0[i]) : f0[previous];
                for (int gap = previous + 1; gap < i; gap++)
                {
                    float t = (gap - previous) / (float)(i - previous);
                    f0[gap] = start + (f0[i] - start) * t;
                }

                previous = i;
            }

            if (previous >= 0)
            {
                for (int i = previous + 1; i < f0.Length; i++)
                {
                    f0[i] = f0[previous];
                }
            }
        }

        /// <summary>Applies a pitch shift to voiced frames.</summary>
        public static float Transpose(float f0, float semitones)
        {
            return f0 * MathF.Pow(2f, semitones / 12f);
        }

        /// <summary>
        /// Doubles [time, 768] HuBERT frames by repetition into RVC's 100 Hz rate,
        /// repeating the last frame for any frames left over.
        /// </summary>
        public static void DoubleFrames(ReadOnlySpan<float> features, int featureFrames, int channels, Span<float> phone, int frames)
        {
            for (int t = 0; t < frames; t++)
            {
                int source = Math.Min(t / 2, featureFrames - 1);
                features.Slice(source * channels, channels).CopyTo(phone.Slice(t * channels, channels));
            }
        }

        /// <summary>
        /// Where the new audio lines up best with the tail of the last output, by normalized correlation,
        /// so the crossfade joins in phase (upstream's SOLA).
        /// </summary>
        public static int BestOffset(ReadOnlySpan<float> previousTail, ReadOnlySpan<float> audio, int search)
        {
            // Coarse then fine, because the exhaustive search cost more than the rest of the block's
            // managed work. Every 8th sample still resolves the voice's period at 48 kHz.
            int best = Search(previousTail, audio, 0, search, CoarseStride, CoarseStride);
            return Search(previousTail, audio, Math.Max(0, best - CoarseStride), Math.Min(search, best + CoarseStride), 1, FineStride);
        }

        private static int Search(ReadOnlySpan<float> previousTail, ReadOnlySpan<float> audio, int first, int last, int step, int stride)
        {
            int best = first;
            float bestScore = float.NegativeInfinity;
            for (int offset = first; offset <= last; offset += step)
            {
                float product = 0f;
                float energy = 1e-8f;
                for (int i = 0; i < previousTail.Length; i += stride)
                {
                    float sample = audio[offset + i];
                    product += previousTail[i] * sample;
                    energy += sample * sample;
                }

                float score = product / MathF.Sqrt(energy);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = offset;
                }
            }

            return best;
        }

        /// <summary>Normalized correlation of two spans of the same length, from -1 to 1.</summary>
        public static float Correlation(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        {
            float product = 0f;
            float energyA = 1e-8f;
            float energyB = 1e-8f;
            for (int i = 0; i < a.Length; i++)
            {
                product += a[i] * b[i];
                energyA += a[i] * a[i];
                energyB += b[i] * b[i];
            }

            return product / MathF.Sqrt(energyA * energyB);
        }

        /// <summary>
        /// Crossfades from <paramref name="from"/> to <paramref name="to"/>, holding the level for how
        /// alike they are: two windows of the vocoder never quite match, and a fade that assumes they do
        /// dips by up to 3 dB in its middle, once per block.
        /// </summary>
        public static void Crossfade(ReadOnlySpan<float> from, ReadOnlySpan<float> to, float correlation, Span<float> output)
        {
            float alike = Math.Clamp(correlation, 0f, 1f);
            for (int i = 0; i < output.Length; i++)
            {
                float t = (i + 0.5f) / output.Length;
                float fade = MathF.Sin(t * MathF.PI * 0.5f);
                fade *= fade;
                float rest = 1f - fade;
                float gain = 1f / MathF.Sqrt(rest * rest + fade * fade + 2f * alike * rest * fade);
                output[i] = (from[i] * rest + to[i] * fade) * gain;
            }
        }

        /// <summary>
        /// Hann-windowed sinc kernels for a rational rate change: <paramref name="to"/>/<paramref name="from"/>
        /// of the source rate, one row per output phase. Trigonometry per sample took most of a block's
        /// managed time, so it is done once here.
        /// </summary>
        public static float[] ResampleKernels(int from, int to)
        {
            double step = from / (double)to;
            double cutoff = Math.Min(1.0, 1.0 / step);
            int width = 2 * ResampleReach(from, to);
            var kernels = new float[to * width];
            for (int phase = 0; phase < to; phase++)
            {
                double fraction = phase * step - Math.Floor(phase * step);
                double total = 0.0;
                for (int k = 0; k < width; k++)
                {
                    double x = (k - width / 2 + 1 - fraction) * cutoff;
                    double hann = Math.Abs(x) >= KernelHalfWidth ? 0.0 : 0.5 + 0.5 * Math.Cos(Math.PI * x / KernelHalfWidth);
                    double weight = (Math.Abs(x) < 1e-9 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x)) * hann;
                    kernels[phase * width + k] = (float)weight;
                    total += weight;
                }

                for (int k = 0; k < width; k++)
                {
                    kernels[phase * width + k] /= (float)total;
                }
            }

            return kernels;
        }

        /// <summary>Samples of context needed on either side of <see cref="Resample"/>.</summary>
        public static int ResampleReach(int from, int to)
        {
            return (int)Math.Ceiling(KernelHalfWidth * Math.Max(1.0, from / (double)to)) + 1;
        }

        /// <summary>
        /// Fills <paramref name="output"/> from <paramref name="source"/>, starting at source sample
        /// <paramref name="start"/> on phase 0, with kernels from <see cref="ResampleKernels"/>.
        /// </summary>
        public static void Resample(ReadOnlySpan<float> source, int start, int from, int to, float[] kernels, Span<float> output)
        {
            int width = 2 * ResampleReach(from, to);
            for (int i = 0; i < output.Length; i++)
            {
                int phase = i % to;
                int center = start + (int)((long)i * from / to);
                ReadOnlySpan<float> kernel = new ReadOnlySpan<float>(kernels, phase * width, width);
                ReadOnlySpan<float> samples = source.Slice(center - width / 2 + 1, width);
                float sum = 0f;
                for (int k = 0; k < width; k++)
                {
                    sum += kernel[k] * samples[k];
                }

                output[i] = sum;
            }
        }

        // Which FFT bins a mel row covers: it is zero elsewhere, and most of the row's work is skipped.
        private static int BasisBinStart(int row)
        {
            double low = HzToMel(MelMinHz);
            double high = HzToMel(MelMaxHz);
            return (int)Math.Floor(MelToHz(low + (high - low) * row / (MelBins + 1)) * FftSize / ModelRate);
        }

        private static int BasisBinEnd(int row)
        {
            double low = HzToMel(MelMinHz);
            double high = HzToMel(MelMaxHz);
            return Math.Min(FftSize / 2 + 1, (int)Math.Ceiling(MelToHz(low + (high - low) * (row + 2) / (MelBins + 1)) * FftSize / ModelRate) + 1);
        }

        private static int[] MakeBins(Func<int, int> bin)
        {
            var bins = new int[MelBins];
            for (int row = 0; row < MelBins; row++)
            {
                bins[row] = bin(row);
            }

            return bins;
        }

        private static int Reflect(int index, int length)
        {
            int period = 2 * (length - 1);
            int folded = Math.Abs(index) % period;
            return folded < length ? folded : period - folded;
        }

        private static double HzToMel(double hz)
        {
            return 2595.0 * Math.Log10(1.0 + hz / 700.0);
        }

        private static double MelToHz(double mel)
        {
            return 700.0 * (Math.Pow(10.0, mel / 2595.0) - 1.0);
        }

        private static float Mel1127(float hz)
        {
            return 1127f * MathF.Log(1f + hz / 700f);
        }

        private static void Fft(float[] real, float[] imaginary)
        {
            int n = real.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1)
                {
                    j ^= bit;
                }

                j ^= bit;
                if (i < j)
                {
                    (real[i], real[j]) = (real[j], real[i]);
                    (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
                }
            }

            for (int size = 2; size <= n; size <<= 1)
            {
                double angle = -2.0 * Math.PI / size;
                float stepReal = (float)Math.Cos(angle);
                float stepImaginary = (float)Math.Sin(angle);
                for (int start = 0; start < n; start += size)
                {
                    float wr = 1f;
                    float wi = 0f;
                    for (int k = 0; k < size / 2; k++)
                    {
                        int a = start + k;
                        int b = a + size / 2;
                        float tr = wr * real[b] - wi * imaginary[b];
                        float ti = wr * imaginary[b] + wi * real[b];
                        real[b] = real[a] - tr;
                        imaginary[b] = imaginary[a] - ti;
                        real[a] += tr;
                        imaginary[a] += ti;
                        float next = wr * stepReal - wi * stepImaginary;
                        wi = wr * stepImaginary + wi * stepReal;
                        wr = next;
                    }
                }
            }
        }
    }
}
