using System;
using NUnit.Framework;
using VirtualVessel.Voice.Rvc;

namespace VirtualVessel.Voice.Tests
{
    public sealed class RvcDspTests
    {
        [Test]
        public void HannWindow_IsPeriodic()
        {
            float[] window = RvcDsp.HannWindow(8);

            Assert.That(window[0], Is.EqualTo(0f));
            Assert.That(window[4], Is.EqualTo(1f).Within(1e-6f));
            Assert.That(window[7], Is.EqualTo(window[1]).Within(1e-6f), "Periodic: the last sample is not back at 0.");
        }

        [Test]
        public void LogMel_PutsAToneInTheBandsAroundIt()
        {
            var audio = new float[RvcDsp.FftSize * 2];
            for (int i = 0; i < audio.Length; i++)
            {
                audio[i] = MathF.Sin(2f * MathF.PI * 440f * i / RvcDsp.ModelRate);
            }

            const int Frames = 4;
            var mel = new float[RvcDsp.MelBins * Frames];
            RvcDsp.LogMel(audio, Frames, 0, RvcDsp.HannWindow(RvcDsp.FftSize), RvcDsp.MelBasis(), mel, new float[RvcDsp.FftSize], new float[RvcDsp.FftSize]);

            int peak = 0;
            for (int row = 1; row < RvcDsp.MelBins; row++)
            {
                if (mel[row * Frames + 2] > mel[peak * Frames + 2])
                {
                    peak = row;
                }
            }

            Assert.That(peak, Is.InRange(20, 25), "2595 log10(1 + f / 700) puts 440 Hz about 23 bands up from 30 Hz.");
            Assert.That(mel[(RvcDsp.MelBins - 1) * Frames + 2], Is.LessThan(mel[peak * Frames + 2] - 5f));
        }

        [Test]
        public void ShiftMel_MovesEachRowLeft()
        {
            var mel = new float[RvcDsp.MelBins * 3];
            mel[0] = 1f;
            mel[1] = 2f;
            mel[2] = 3f;

            RvcDsp.ShiftMel(mel, 3, 2);

            Assert.That(mel[0], Is.EqualTo(3f));
        }

        [Test]
        public void DecodeF0_ReadsThePeak_AndCallsQuietFramesUnvoiced()
        {
            var salience = new float[RvcDsp.PitchBins * 2];
            salience[100] = 0.9f;
            var f0 = new float[2];

            RvcDsp.DecodeF0(salience, 2, 0.03f, f0);

            double expected = 10.0 * Math.Pow(2.0, (20.0 * 100 + 1997.3794084376191) / 1200.0);
            Assert.That(f0[0], Is.EqualTo((float)expected).Within(0.01f));
            Assert.That(f0[1], Is.Zero);
        }

        [TestCase(0f, 1)]
        [TestCase(50f, 1)]
        [TestCase(1100f, 255)]
        [TestCase(5000f, 255)]
        public void CoarsePitch_StaysInRvcsRange(float f0, int expected)
        {
            Assert.That(RvcDsp.CoarsePitch(f0), Is.EqualTo(expected));
        }

        [Test]
        public void FillUnvoiced_BridgesGaps_AndHoldsTheEnds()
        {
            float[] f0 = { 0f, 100f, 0f, 200f, 0f };

            RvcDsp.FillUnvoiced(f0, 0f);

            Assert.That(f0, Is.EqualTo(new[] { 100f, 100f, 150f, 200f, 200f }));
        }

        [Test]
        public void FillUnvoiced_LeavesSilenceAtZero()
        {
            float[] f0 = { 0f, 0f };

            RvcDsp.FillUnvoiced(f0, 0f);

            Assert.That(f0, Is.EqualTo(new[] { 0f, 0f }));
        }

        [Test]
        public void Transpose_AnOctave_Doubles()
        {
            Assert.That(RvcDsp.Transpose(100f, 12f), Is.EqualTo(200f).Within(0.001f));
        }

        [Test]
        public void DoubleFrames_RepeatsEachFrame_ThenTheLast()
        {
            float[] features = { 1f, 2f };
            var phone = new float[5];

            RvcDsp.DoubleFrames(features, 2, 1, phone, 5);

            Assert.That(phone, Is.EqualTo(new[] { 1f, 1f, 2f, 2f, 2f }));
        }

        [Test]
        public void BestOffset_FindsWhereTheAudioLinesUp()
        {
            var audio = new float[1200];
            for (int i = 0; i < audio.Length; i++)
            {
                audio[i] = MathF.Sin(i * 0.05f) * MathF.Sin(i * 0.013f + 1f);
            }

            var tail = new float[400];
            Array.Copy(audio, 137, tail, 0, tail.Length);

            Assert.That(RvcDsp.BestOffset(tail, audio, 300), Is.EqualTo(137).Within(1));
        }

        [Test]
        public void Crossfade_HoldsTheLevelOfUnrelatedAudio()
        {
            var random = new Random(1);
            var a = new float[4000];
            var b = new float[4000];
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = (float)(random.NextDouble() * 2.0 - 1.0);
                b[i] = (float)(random.NextDouble() * 2.0 - 1.0);
            }

            var output = new float[a.Length];
            RvcDsp.Crossfade(a, b, RvcDsp.Correlation(a, b), output);

            double middle = Rms(output, 1800, 400);
            Assert.That(middle / Rms(a, 1800, 400), Is.EqualTo(1.0).Within(0.1), "No dip in the middle of the fade.");
        }

        [Test]
        public void Crossfade_OfIdenticalAudio_ChangesNothing()
        {
            float[] a = { 0.5f, -0.25f, 0.75f, 0.1f };
            var output = new float[4];

            RvcDsp.Crossfade(a, a, 1f, output);

            Assert.That(output, Is.EqualTo(a).Within(1e-5f));
        }

        [Test]
        public void Resample_Down_KeepsAToneBelowTheNewNyquist()
        {
            var source = new float[4800];
            for (int i = 0; i < source.Length; i++)
            {
                source[i] = MathF.Sin(2f * MathF.PI * 1000f * i / 48000f);
            }

            int reach = RvcDsp.ResampleReach(3, 1);
            var output = new float[(source.Length - 2 * reach) / 3];
            RvcDsp.Resample(source, reach, 3, 1, RvcDsp.ResampleKernels(3, 1), output);

            for (int i = 0; i < output.Length; i++)
            {
                float expected = MathF.Sin(2f * MathF.PI * 1000f * (reach + 3 * i) / 48000f);
                Assert.That(output[i], Is.EqualTo(expected).Within(0.01f));
            }
        }

        [Test]
        public void Resample_Down_RemovesAToneAboveTheNewNyquist()
        {
            var source = new float[4800];
            for (int i = 0; i < source.Length; i++)
            {
                source[i] = MathF.Sin(2f * MathF.PI * 12000f * i / 48000f);
            }

            int reach = RvcDsp.ResampleReach(3, 1);
            var output = new float[(source.Length - 2 * reach) / 3];
            RvcDsp.Resample(source, reach, 3, 1, RvcDsp.ResampleKernels(3, 1), output);

            Assert.That(Rms(output, 0, output.Length), Is.LessThan(0.01));
        }

        [Test]
        public void Resample_Up_KeepsATone()
        {
            var source = new float[4000];
            for (int i = 0; i < source.Length; i++)
            {
                source[i] = MathF.Sin(2f * MathF.PI * 1000f * i / 40000f);
            }

            int reach = RvcDsp.ResampleReach(5, 6);
            var output = new float[4000];
            RvcDsp.Resample(source, reach, 5, 6, RvcDsp.ResampleKernels(5, 6), output);

            for (int i = 0; i < output.Length; i++)
            {
                float expected = MathF.Sin(2f * MathF.PI * 1000f * (reach + i * 5f / 6f) / 40000f);
                Assert.That(output[i], Is.EqualTo(expected).Within(0.01f));
            }
        }

        private static double Rms(float[] values, int start, int count)
        {
            double sum = 0.0;
            for (int i = start; i < start + count; i++)
            {
                sum += values[i] * values[i];
            }

            return Math.Sqrt(sum / count);
        }
    }
}
