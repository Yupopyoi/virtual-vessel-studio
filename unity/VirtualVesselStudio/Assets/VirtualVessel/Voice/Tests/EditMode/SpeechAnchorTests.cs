using System;
using NUnit.Framework;
using VirtualVessel.Voice.Rvc;

namespace VirtualVessel.Voice.Tests
{
    public sealed class SpeechAnchorTests
    {
        private const int Block = 1600;

        [Test]
        public void Anchor_IsQuietNoiseBeforeAnySpeech()
        {
            var anchor = new SpeechAnchor(3);

            Assert.That(anchor.Anchor.Length, Is.EqualTo(SpeechAnchor.Samples));
            Assert.That(RvcDsp.Rms(anchor.Anchor), Is.EqualTo(0.01f).Within(0.001f));
        }

        [Test]
        public void Anchor_BecomesTheLatestUnbrokenSecondOfSpeechThatLeftTheWindow()
        {
            var anchor = new SpeechAnchor(3);
            float[] noise = anchor.Anchor.ToArray();

            // Twelve speech blocks enter; a block's audio counts when it leaves, three blocks later.
            for (int i = 0; i < 12; i++)
            {
                anchor.Advance(Filled(i - 3), true);
            }

            Assert.That(anchor.Anchor.ToArray(), Is.EqualTo(noise), "Only nine speech blocks have left so far.");

            anchor.Advance(Filled(9), false);
            Assert.That(anchor.Anchor[0], Is.EqualTo(0f), "The first ten speech blocks to leave, 0 to 9.");
            Assert.That(anchor.Anchor[SpeechAnchor.Samples - 1], Is.EqualTo(9f));

            anchor.Advance(Filled(10), false);
            Assert.That(anchor.Anchor[0], Is.EqualTo(1f), "The latest ten, 1 to 10.");
            Assert.That(anchor.Anchor[SpeechAnchor.Samples - 1], Is.EqualTo(10f));
        }

        [Test]
        public void Anchor_IsKeptThroughAHeldNote()
        {
            var anchor = new SpeechAnchor(1);
            for (int i = 0; i < 11; i++)
            {
                anchor.Advance(Filled(i - 1), i < 10);
            }

            // A held note: no block is speech, so the speech from before it stays.
            for (int i = 0; i < 50; i++)
            {
                anchor.Advance(Filled(100), false);
            }

            Assert.That(anchor.Anchor[SpeechAnchor.Samples - 1], Is.EqualTo(9f));
        }

        [Test]
        public void Anchor_IsNotJoinedAcrossABreak()
        {
            var anchor = new SpeechAnchor(1);
            float[] noise = anchor.Anchor.ToArray();
            bool[] speech = { true, true, true, true, true, false, true, true, true, true, true, true, true };
            for (int i = 0; i < speech.Length; i++)
            {
                anchor.Advance(Filled(i - 1), speech[i]);
            }

            Assert.That(anchor.Anchor.ToArray(), Is.EqualTo(noise), "Five blocks, a break, then six: no unbroken second.");
        }

        [Test]
        public void IsSpeech_NeedsChangeAndLevel()
        {
            Assert.That(SpeechAnchor.IsSpeech(0.5f, 0.1f), Is.True);
            Assert.That(SpeechAnchor.IsSpeech(0.25f, 0.1f), Is.False, "A held note changes little.");
            Assert.That(SpeechAnchor.IsSpeech(0.5f, 0.001f), Is.False, "Quiet noise is not speech.");
        }

        [Test]
        public void MelChange_IsZeroForASteadyPictureAndGrowsWithChange()
        {
            const int Frames = 4;
            var mel = new float[RvcDsp.MelBins * Frames];
            Assert.That(RvcDsp.MelChange(mel, Frames, 0, 3), Is.EqualTo(0f));

            for (int row = 0; row < RvcDsp.MelBins; row++)
            {
                mel[row * Frames + 3] = 2f;
            }

            Assert.That(RvcDsp.MelChange(mel, Frames, 0, 3), Is.EqualTo(2f / 3f).Within(1e-6f), "One step of 2 in every row, over three steps.");
        }

        private static float[] Filled(float value)
        {
            var block = new float[Block];
            Array.Fill(block, value);
            return block;
        }
    }
}
