using System;
using NUnit.Framework;

namespace VirtualVessel.Voice.Tests
{
    /// <summary>Keeping a finished voice under full scale (Voice detailed design 8.6).</summary>
    public sealed class PeakLimiterTests
    {
        private const int Rate = 48000;

        [Test]
        public void NothingGetsPastTheCeiling()
        {
            var samples = new float[4800];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = (float)Math.Sin(i * 0.05) * 1.6f;
            }

            new PeakLimiter(Rate).Process(samples);

            foreach (float sample in samples)
            {
                Assert.That(Math.Abs(sample), Is.LessThanOrEqualTo(PeakLimiter.Ceiling + 1e-6f));
            }
        }

        [Test]
        public void AQuietVoice_PassesAsItIs()
        {
            var samples = new float[4800];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = (float)Math.Sin(i * 0.05) * 0.5f;
            }

            float[] before = (float[])samples.Clone();
            var limiter = new PeakLimiter(Rate);
            limiter.Process(samples);

            Assert.That(samples, Is.EqualTo(before));
            Assert.That(limiter.Gain, Is.EqualTo(1f));
        }

        [Test]
        public void AfterAPeak_TheGainComesBack()
        {
            var limiter = new PeakLimiter(Rate);
            limiter.Process(new[] { 1.78f });
            Assert.That(limiter.Gain, Is.EqualTo(0.5f).Within(1e-3f), "Down at once to the ceiling.");

            limiter.Process(new float[(int)(Rate * PeakLimiter.ReleaseSeconds * 5)]);
            Assert.That(limiter.Gain, Is.GreaterThan(0.99f), "Back after five releases.");
        }

        [Test]
        public void Processing_AllocatesNothing()
        {
            var limiter = new PeakLimiter(Rate);
            var block = new float[4800];
            limiter.Process(block);
            long before = GC.GetAllocatedBytesForCurrentThread();
            limiter.Process(block);
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        }
    }
}
