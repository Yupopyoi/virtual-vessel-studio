using NUnit.Framework;
using VirtualVessel.UI.Screens;
using VirtualVessel.UI.Voice;

namespace VirtualVessel.Voice.Tests
{
    public sealed class VoiceServiceTests
    {
        [TestCase(4800, 4800, 2, 0)]
        [TestCase(9600, 4800, 2, 0)]
        [TestCase(9601, 4800, 2, 4800)]
        [TestCase(20000, 4800, 2, 14400)]
        public void SamplesToDrop_KeepsTheWaitBounded(int waiting, int block, int maxBlocks, int expected)
        {
            Assert.That(VoiceService.SamplesToDrop(waiting, block, maxBlocks), Is.EqualTo(expected));
        }

        [Test]
        public void ToMono_AveragesTheChannels()
        {
            float[] stereo = { 1f, 0f, 0.5f, 0.5f };
            var mono = new float[2];

            VoiceService.ToMono(stereo, mono);

            Assert.That(mono, Is.EqualTo(new[] { 0.5f, 0.5f }));
        }

        [Test]
        public void ToStereo_CopiesToBothChannels()
        {
            float[] mono = { 0.25f, -0.5f };
            var stereo = new float[4];

            VoiceService.ToStereo(mono, stereo);

            Assert.That(stereo, Is.EqualTo(new[] { 0.25f, 0.25f, -0.5f, -0.5f }));
        }

        [TestCase(VoiceConversionState.Off, VoiceConversionStrings.StateOff)]
        [TestCase(VoiceConversionState.Running, VoiceConversionStrings.StateRunning)]
        [TestCase(VoiceConversionState.Failed, VoiceConversionStrings.StateFailed)]
        public void Card_NamesEachState(VoiceConversionState state, string key)
        {
            Assert.That(VoiceConversionCard.StateKey(state), Is.EqualTo(key));
        }
    }
}
