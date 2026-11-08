using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualVessel.Core.Time;
using VirtualVessel.UI.Screens;
using VirtualVessel.UI.Voice;
using VirtualVessel.Voice.Models;

namespace VirtualVessel.Voice.Tests
{
    /// <summary>Finding and checking the voices the conversion runs (Voice detailed design 6.1, 8.2).</summary>
    public sealed class VoiceModelLibraryTests
    {
        private const string Fits = "{\"frames\":100,\"returned\":21,\"medianPitchHz\":350}";

        private string _folder;
        private string _root;
        private string _prototype;
        private VoiceModelStore _voices;
        private VoiceModelLibrary _library;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "vv-voice-" + Guid.NewGuid().ToString("N"));
            _root = Path.Combine(_folder, "VoiceModels");
            _prototype = Path.Combine(_root, VoiceModelLibrary.PrototypeFolder);
            Directory.CreateDirectory(_prototype);
            _voices = new VoiceModelStore(_root, new FixedClock());
            _library = new VoiceModelLibrary(_root, _prototype, _voices);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_folder, true);
        }

        [Test]
        public void Locate_TakesTheSharedModelsFromShared_AndElseFromThePrototype()
        {
            var files = new HashSet<string>
            {
                Path.Combine(_prototype, VoiceModelLibrary.FeaturesFile(100)),
                Path.Combine(_prototype, VoiceModelLibrary.PitchFile),
                Path.Combine(_root, VoiceModelLibrary.SharedFolder, VoiceModelLibrary.PitchFile),
                Path.Combine(_root, "voice-a", "voice.sentis"),
                Path.Combine(_root, "voice-a", "voice.spk"),
            };

            VoiceModelFiles voice = VoiceModelLibrary.Locate("voice-a", _root, _prototype, id => Path.Combine(_root, id), Fits, files.Contains);

            Assert.That(voice.Problem, Is.Null);
            Assert.That(voice.Features, Is.EqualTo(Path.Combine(_prototype, VoiceModelLibrary.FeaturesFile(100))));
            Assert.That(voice.Pitch, Is.EqualTo(Path.Combine(_root, VoiceModelLibrary.SharedFolder, VoiceModelLibrary.PitchFile)));
            Assert.That(voice.Generator, Is.EqualTo(Path.Combine(_root, "voice-a", "voice.sentis")));
            Assert.That(voice.Speaker, Is.EqualTo(Path.Combine(_root, "voice-a", "voice.spk")));
        }

        [Test]
        public void Locate_RefusesWhatCannotRun()
        {
            var shared = new HashSet<string>
            {
                Path.Combine(_prototype, VoiceModelLibrary.FeaturesFile(100)),
                Path.Combine(_prototype, VoiceModelLibrary.PitchFile),
            };
            Func<string, string> folderOf = id => Path.Combine(_root, id);

            Assert.That(VoiceModelLibrary.Locate("voice-a", _root, _prototype, folderOf, Fits, shared.Contains).Problem, Is.Not.Null, "No generator.");
            Assert.That(VoiceModelLibrary.Locate("voice-a", _root, _prototype, folderOf, Fits, _ => false).Problem, Is.Not.Null, "No shared models.");
            Assert.That(VoiceModelLibrary.Locate("voice-a", _root, _prototype, null, Fits, _ => true).Problem, Is.Not.Null, "No registered voices.");
            Assert.That(VoiceModelLibrary.Locate("../x", _root, _prototype, folderOf, Fits, _ => true).Problem, Is.Not.Null);
            Assert.That(VoiceModelLibrary.Locate("voice-a", _root, _prototype, folderOf, "{\"frames\":250,\"returned\":21}", _ => true).Problem, Is.Not.Null);
            Assert.That(VoiceModelLibrary.Locate(VoiceModelLibrary.PrototypeId, _root, null, folderOf, null, _ => true).Problem, Is.Not.Null);
        }

        [TestCase(Fits, true)]
        [TestCase("{\"frames\":100,\"returned\":18}", false)]
        [TestCase("{}", false)]
        [TestCase("not json", false)]
        [TestCase(null, false)]
        public void ShapeProblem_AcceptsOnlyThisVersionsWindow(string manifest, bool fits)
        {
            Assert.That(VoiceModelLibrary.ShapeProblem(manifest) == null, Is.EqualTo(fits));
        }

        [Test]
        public void List_PutsThePrototypeFirst_AndMarksAVoiceThatCannotRun()
        {
            Touch(Path.Combine(_prototype, VoiceModelLibrary.FeaturesFile(100)));
            Touch(Path.Combine(_prototype, VoiceModelLibrary.PitchFile));
            Touch(Path.Combine(_prototype, VoiceModelLibrary.GeneratorFile(100)));
            Touch(Path.Combine(_prototype, Path.ChangeExtension(VoiceModelLibrary.GeneratorFile(100), ".spk")));
            Register("voice-old", "Old", "{\"frames\":250,\"returned\":21}");
            Register("voice-new", "New", Fits);

            IReadOnlyList<VoiceConversionVoice> voices = _library.List().Value;

            Assert.That(voices.Select(v => v.Id), Is.EqualTo(new[] { VoiceModelLibrary.PrototypeId, "voice-new", "voice-old" }));
            Assert.That(voices.Select(v => v.Usable), Is.EqualTo(new[] { true, true, false }));
            Assert.That(voices[1].Name, Is.EqualTo("New"));
            Assert.That(voices[1].MedianPitchHz, Is.EqualTo(350));
            Assert.That(VoiceModelLibrary.FirstUsable(voices), Is.EqualTo(VoiceModelLibrary.PrototypeId));
        }

        [Test]
        public void FirstUsable_SkipsAVoiceThatCannotRun()
        {
            var voices = new[] { new VoiceConversionVoice("voice-a", "A", 0, false), new VoiceConversionVoice("voice-b", "B", 0, true) };
            Assert.That(VoiceModelLibrary.FirstUsable(voices), Is.EqualTo("voice-b"));
            Assert.That(VoiceModelLibrary.FirstUsable(Array.Empty<VoiceConversionVoice>()), Is.Null);
        }

        [TestCase(true, 3, 3, false, false, true)]
        [TestCase(true, 2, 3, false, false, false)]
        [TestCase(true, 3, 3, true, false, false)]
        [TestCase(true, 3, 3, false, true, false)]
        [TestCase(false, 3, 3, false, false, false)]
        public void ShouldSwap_OnlyBetweenBlocks_OnceWarm(bool hasNext, int warmed, int warmup, bool currentBusy, bool nextBusy, bool expected)
        {
            Assert.That(VoiceWarmup.ShouldSwap(hasNext, warmed, warmup, currentBusy, nextBusy), Is.EqualTo(expected));
        }

        [Test]
        public void AVoiceChosenByItsFolder_IsToldFromARegisteredOne()
        {
            Assert.That(VoiceService.FolderOf(VoiceService.FolderPrefix + "C:/runs/a/output"), Is.EqualTo("C:/runs/a/output"));
            Assert.That(VoiceService.FolderOf("voice-a"), Is.Null);
            Assert.That(VoiceService.FolderOf(VoiceModelLibrary.PrototypeId), Is.Null);
            Assert.That(VoiceService.FolderOf(null), Is.Null);
        }

        [Test]
        public void TheLog_NamesThePrototypesVoice_WhoseIdIsEmpty()
        {
            Assert.That(VoiceService.LogName(VoiceModelLibrary.PrototypeId), Is.EqualTo("prototype"));
            Assert.That(VoiceService.LogName("voice-a"), Is.EqualTo("voice-a"));
            Assert.That(VoiceService.LogName(null), Is.EqualTo("none"));
        }

        [Test]
        public void TheCardsChoices_NameThePrototype_AndMarkAVoiceThatCannotRun()
        {
            var choices = VoiceConversionCard.Choices(new[]
            {
                new VoiceConversionVoice(VoiceModelLibrary.PrototypeId, null, 0, true),
                new VoiceConversionVoice("voice-a", "Fuwari", 300, true),
                new VoiceConversionVoice("voice-b", "Old", 0, false),
            });

            Assert.That(choices.Select(c => c.Id), Is.EqualTo(new[] { "", "voice-a", "voice-b" }));
            Assert.That(choices[0].TextKey, Is.EqualTo(VoiceConversionStrings.Prototype));
            Assert.That(choices[1].Text, Is.EqualTo("Fuwari"));
            Assert.That(choices[2].TextKey, Is.EqualTo(VoiceConversionStrings.Unusable));
        }

        private void Register(string id, string name, string manifest)
        {
            string source = Path.Combine(_folder, "source-" + id);
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "voice.sentis"), "model");
            File.WriteAllText(Path.Combine(source, "voice.spk"), "speaker");
            File.WriteAllText(Path.Combine(source, "voice.json"), manifest);
            Assert.That(_voices.Add(new NewVoiceModel(id, name, "run", source, VoiceModelStore.RuntimeFiles, null)).Ok, Is.True);
        }

        private static void Touch(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "x");
        }

        private sealed class FixedClock : ISystemClock
        {
            private DateTimeOffset _now = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

            // Each voice a minute after the last, so the newest-first order is known.
            public DateTimeOffset UtcNow => _now = _now.AddMinutes(1);
        }
    }
}
