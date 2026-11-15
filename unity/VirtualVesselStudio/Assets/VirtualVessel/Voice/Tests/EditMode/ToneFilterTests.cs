using System;
using NUnit.Framework;

namespace VirtualVessel.Voice.Tests
{
    /// <summary>The finish on a converted voice (Voice detailed design 8.6): four bands, each where it says.</summary>
    public sealed class ToneFilterTests
    {
        private const int Rate = 48000;

        [Test]
        public void AFlatTone_LeavesTheVoiceAsItIs()
        {
            float[] samples = Noise(4800, 1);
            float[] before = (float[])samples.Clone();
            new ToneFilter(default, Rate).Process(samples);
            Assert.That(samples, Is.EqualTo(before));
            Assert.That(new VoiceTone(0f, 0f, 0f, 0f).IsFlat, Is.True);
        }

        [Test]
        public void EachBand_ReachesItsGainInItsRange_AndLeavesTheRest()
        {
            Biquad[] bands = ToneFilter.Bands(new VoiceTone(6f, 6f, 6f, 6f), Rate);

            Assert.That(ToneFilter.GainDb(bands[0], 40, Rate), Is.EqualTo(6.0).Within(0.5), "Body lifts the lows.");
            Assert.That(ToneFilter.GainDb(bands[0], 4000, Rate), Is.EqualTo(0.0).Within(0.3));
            Assert.That(ToneFilter.GainDb(bands[1], ToneFilter.ClarityHz, Rate), Is.EqualTo(-6.0).Within(0.1), "Clearer cuts the low middle.");
            Assert.That(ToneFilter.GainDb(bands[1], 8000, Rate), Is.EqualTo(0.0).Within(0.3));
            Assert.That(ToneFilter.GainDb(bands[2], 16000, Rate), Is.EqualTo(6.0).Within(0.5), "Brightness lifts the highs.");
            Assert.That(ToneFilter.GainDb(bands[2], 200, Rate), Is.EqualTo(0.0).Within(0.3));
            Assert.That(ToneFilter.GainDb(bands[3], 20000, Rate), Is.EqualTo(6.0).Within(1.0), "Air lifts the top.");
            Assert.That(ToneFilter.GainDb(bands[3], 1000, Rate), Is.EqualTo(0.0).Within(0.3));
        }

        [Test]
        public void ANegativeTone_CutsAsMuchAsAPositiveOneLifts()
        {
            Biquad dark = ToneFilter.Bands(new VoiceTone(0f, 0f, -6f, 0f), Rate)[2];
            Assert.That(ToneFilter.GainDb(dark, 16000, Rate), Is.EqualTo(-6.0).Within(0.5));
        }

        [Test]
        public void BlockByBlock_SoundsAsOne()
        {
            var tone = new VoiceTone(3f, -2f, 4f, 5f);
            float[] samples = Noise(9600, 2);
            float[] whole = ToneFilter.Apply(samples, tone, Rate);

            var filter = new ToneFilter(tone, Rate);
            float[] blocks = (float[])samples.Clone();
            for (int start = 0; start < blocks.Length; start += 4800)
            {
                filter.Process(new Span<float>(blocks, start, Math.Min(4800, blocks.Length - start)));
            }

            for (int i = 0; i < whole.Length; i++)
            {
                Assert.That(blocks[i], Is.EqualTo(whole[i]).Within(1e-6f), $"Sample {i}.");
            }
        }

        [Test]
        public void Processing_AllocatesNothing()
        {
            var filter = new ToneFilter(new VoiceTone(3f, 3f, 3f, 3f), Rate);
            float[] block = Noise(4800, 3);
            filter.Process(block);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++)
            {
                filter.Process(block);
            }

            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero, "It runs on every block of the stream.");
        }

        [TestCase(9f, 6f)]
        [TestCase(-7.5f, -6f)]
        [TestCase(float.NaN, 0f)]
        public void ATone_StaysWithinItsLimits(float given, float kept)
        {
            Assert.That(new VoiceTone(given, given, given, given).Body, Is.EqualTo(kept));
        }

        private static float[] Noise(int count, int seed)
        {
            var random = new Random(seed);
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                samples[i] = (float)((random.NextDouble() * 2.0) - 1.0) * 0.5f;
            }

            return samples;
        }
    }
}
