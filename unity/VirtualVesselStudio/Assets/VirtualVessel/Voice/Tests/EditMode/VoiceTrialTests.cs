using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using VirtualVessel.Voice.Models;

namespace VirtualVessel.Voice.Tests
{
    /// <summary>Converting a recording to hear a voice (Voice detailed design 8.5): its pure parts.</summary>
    public sealed class VoiceTrialTests
    {
        [Test]
        public void AWrittenWav_ReadsBackAsTheSameSamples()
        {
            float[] samples = { 0f, 0.5f, -0.5f, 1f, -1f, 2f };

            float[] read = WavFile.Read(WavFile.Write(samples, 48000), out int rate);

            Assert.That(rate, Is.EqualTo(48000));
            Assert.That(read, Is.EqualTo(new[] { 0f, 0.5f, -0.5f, 1f, -1f, 1f }).Within(1f / 16384f), "Clipped to -1 to 1, at 16 bits.");
        }

        [Test]
        public void AVoiceFoundByItsFolder_IsCheckedAsARegisteredOne_AndCarriesItsPitch()
        {
            string root = Path.Combine("x", "VoiceModels");
            string prototype = Path.Combine(root, VoiceModelLibrary.PrototypeFolder);
            string run = Path.Combine("x", "VoiceLab", "run", "output");
            var files = new HashSet<string>
            {
                Path.Combine(prototype, VoiceModelLibrary.FeaturesFile(100)),
                Path.Combine(prototype, VoiceModelLibrary.PitchFile),
                Path.Combine(run, "voice.sentis"),
                Path.Combine(run, "voice.spk"),
            };
            const string Fits = "{\"frames\":100,\"returned\":21,\"medianPitchHz\":285}";

            VoiceModelFiles found = VoiceModelLibrary.LocateFolder(run, root, prototype, Fits, files.Contains);

            Assert.That(found.Problem, Is.Null);
            Assert.That(found.Generator, Is.EqualTo(Path.Combine(run, "voice.sentis")));
            Assert.That(found.MedianPitchHz, Is.EqualTo(285));
            Assert.That(VoiceModelLibrary.LocateFolder(run, root, prototype, "{\"frames\":250,\"returned\":21}", files.Contains).Problem, Is.Not.Null);
            Assert.That(VoiceModelLibrary.LocateFolder(null, root, prototype, Fits, files.Contains).Problem, Is.Not.Null);
        }

        [TestCase(true, 0f, 0f, 285f, 0f, true)]
        [TestCase(true, 0f, 200f, 285f, 6f, false)]
        [TestCase(true, -2f, 200f, 285f, 4f, false)]
        [TestCase(false, 3f, 0f, 285f, 3f, false)]
        [TestCase(true, 0f, 0f, 0f, 0f, false)]
        public void ATrial_MeasuresTheUserFirst_OnlyWhenItMustMatchAndCannotYet(bool auto, float adjustment, float userHz, float voiceHz, float semitones, bool measure)
        {
            (float shift, bool first) = VoiceTrialService.Plan(auto, adjustment, userHz, voiceHz);
            Assert.That(shift, Is.EqualTo(semitones));
            Assert.That(first, Is.EqualTo(measure));
        }
    }
}
