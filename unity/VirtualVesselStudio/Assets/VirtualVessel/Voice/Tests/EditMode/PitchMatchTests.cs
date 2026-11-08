using System.Linq;
using NUnit.Framework;
using VirtualVessel.UI.Screens;

namespace VirtualVessel.Voice.Tests
{
    /// <summary>Matching the conversion's pitch to the user (Voice detailed design 8.4).</summary>
    public sealed class PitchMatchTests
    {
        [TestCase(285f, 190f, 7f)]
        [TestCase(190f, 285f, -7f)]
        [TestCase(300f, 150f, 12f)]
        [TestCase(300f, 300f, 0f)]
        [TestCase(0f, 190f, 0f)]
        [TestCase(285f, 0f, 0f)]
        [TestCase(2000f, 50f, 24f)]
        public void Semitones_BringTheUsersMedianToTheVoices(float voiceHz, float userHz, float expected)
        {
            Assert.That(PitchMatch.Semitones(voiceHz, userHz), Is.EqualTo(expected));
        }

        [TestCase(true, 7f, -2f, 5f)]
        [TestCase(false, 7f, -2f, -2f)]
        [TestCase(true, 20f, 10f, 24f)]
        [TestCase(true, -20f, -10f, -24f)]
        public void Applied_AddsTheAdjustmentToTheAutomaticShift_WhenOn(bool auto, float automatic, float adjustment, float expected)
        {
            Assert.That(PitchMatch.Applied(auto, automatic, adjustment), Is.EqualTo(expected));
        }

        [Test]
        public void TheMeter_GivesAMedianAfterTwoSecondsOfVoice_AndKeepsItAfterTen()
        {
            var meter = new PitchMeter();
            // Voiced frames vary a little; each block is new pitch.
            float[] block = Enumerable.Range(0, 10).Select(i => 195f + i).ToArray();
            for (int i = 0; i < 19; i++)
            {
                meter.Add(block);
            }

            Assert.That(meter.MedianHz, Is.EqualTo(0f), "Under 2 s of voice.");
            Assert.That(meter.Add(block), Is.True, "The median appears.");
            Assert.That(meter.MedianHz, Is.EqualTo(199.5f));

            for (int i = 0; i < 80; i++)
            {
                meter.Add(block);
            }

            Assert.That(meter.IsLocked, Is.True);
            Assert.That(meter.Add(Enumerable.Range(0, 10).Select(i => 600f + i).ToArray()), Is.False, "Singing high later does not move it.");
            Assert.That(meter.MedianHz, Is.EqualTo(199.5f));

            meter.Reset();
            Assert.That(meter.MedianHz, Is.EqualTo(0f));
            Assert.That(meter.IsLocked, Is.False);
        }

        [Test]
        public void TheMeter_LeavesOutUnvoicedAndImplausibleFrames()
        {
            var meter = new PitchMeter();
            float[] block = { 0f, 0f, 30f, 2000f, 180f, 181f, 182f, 183f, 184f, 185f };
            for (int i = 0; i < 40; i++)
            {
                meter.Add(block);
            }

            Assert.That(meter.MedianHz, Is.EqualTo(182.5f), "Only the six voiced frames of each block count.");
        }

        [Test]
        public void TheMeter_LeavesOutFramesHeldThroughSilence()
        {
            // A phrase at about 200 Hz falling to 120 Hz, then silence that the generator fills with the last 120 Hz.
            var meter = new PitchMeter();
            float[] phrase = Enumerable.Range(0, 10).Select(i => 200f + i - (i == 9 ? 89f : 0f)).ToArray();
            float[] silence = Enumerable.Repeat(120f, 10).ToArray();
            for (int i = 0; i < 30; i++)
            {
                meter.Add(phrase);
                meter.Add(silence);
                meter.Add(silence);
            }

            Assert.That(meter.MedianHz, Is.InRange(200f, 210f), "Held frames would pull it to 120 Hz.");
        }

        [Test]
        public void Median_OfOddAndEvenCounts()
        {
            var scratch = new float[4];
            Assert.That(PitchMeter.Median(new[] { 3f, 1f, 2f }, scratch), Is.EqualTo(2f));
            Assert.That(PitchMeter.Median(new[] { 4f, 1f, 3f, 2f }, scratch), Is.EqualTo(2.5f));
            Assert.That(PitchMeter.Median(new float[0], scratch), Is.EqualTo(0f));
        }

        [Test]
        public void TheCard_ShowsBothPitchesAndTheShift()
        {
            Assert.That(VoiceConversionCard.FormatPitch(190f, 285f, 7f), Is.EqualTo("190 Hz -> 285 Hz  (+7)"));
            Assert.That(VoiceConversionCard.FormatPitch(0f, 0f, 0f), Is.EqualTo("? Hz -> ? Hz  (0)"));
        }
    }
}
