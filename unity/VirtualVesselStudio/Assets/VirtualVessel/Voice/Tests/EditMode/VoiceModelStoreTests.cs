using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.Voice.Models;

namespace VirtualVessel.Voice.Tests
{
    /// <summary>Registered voices' file implementation (Voice detailed design 6.1).</summary>
    public sealed class VoiceModelStoreTests
    {
        private string _folder;
        private FixedClock _clock;
        private VoiceModelStore _voices;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "vv-voice-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            _clock = new FixedClock();
            _voices = new VoiceModelStore(Path.Combine(_folder, "VoiceModels"), _clock);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_folder, true);
        }

        [Test]
        public void Add_CopiesTheFiles_AndReadsTheMedianPitch()
        {
            VoiceModelRecord voice = _voices.Add(Voice("voice-a", "A", "{\"medianPitchHz\":360}", "design.json")).Value;

            string folder = _voices.FolderOf("voice-a");
            Assert.That(voice.MedianPitchHz, Is.EqualTo(360));
            Assert.That(voice.RegisteredAtUtc, Is.EqualTo("2026-11-08T12:00:00.0000000Z"));
            Assert.That(Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n), Is.EqualTo(new[] { "design.json", "model.json", "voice.json", "voice.sentis", "voice.spk" }));
            Assert.That(Directory.Exists(folder + ".part"), Is.False);
            Assert.That(_voices.Get("voice-a").Value.Name, Is.EqualTo("A"));
        }

        [Test]
        public void Add_RefusesAVoiceWithoutItsRuntimeFiles()
        {
            NewVoiceModel voice = Voice("voice-a", "A", "{}");
            File.Delete(Path.Combine(voice.SourceFolder, "voice.spk"));

            Assert.That(_voices.Add(voice).Error, Is.EqualTo(CrudError.Invalid));
            Assert.That(_voices.List().Value, Is.Empty);
        }

        [Test]
        public void Problem_RefusesIdsAndFilesThatReachOutside()
        {
            Assert.That(VoiceModelStore.Problem(New("voice-a", "voice.sentis", "voice.spk", "voice.json")), Is.Null);
            Assert.That(VoiceModelStore.Problem(New("prototype", "voice.sentis", "voice.spk", "voice.json")), Is.Not.Null);
            Assert.That(VoiceModelStore.Problem(New("voice-a", "voice.sentis", "voice.spk")), Is.Not.Null, "voice.json is not listed.");
            Assert.That(VoiceModelStore.Problem(New("voice-a", "voice.sentis", "voice.spk", "voice.json", "../x")), Is.Not.Null);
            Assert.That(VoiceModelStore.Problem(New("voice-a", "voice.sentis", "voice.spk", "voice.json", "model.json")), Is.Not.Null, "model.json is the store's own.");
            Assert.That(VoiceModelStore.Problem(null), Is.Not.Null);
        }

        [Test]
        public void Voices_AreListedNewestFirst_RenamedAndDeleted_AndOtherFoldersAreNotVoices()
        {
            Directory.CreateDirectory(Path.Combine(_folder, "VoiceModels", "prototype"));
            _voices.Add(Voice("voice-a", "A", "{}"));
            _clock.UtcNow = _clock.UtcNow.AddDays(1);
            _voices.Add(Voice("voice-b", "B", "{}"));

            Assert.That(_voices.List().Value.Select(v => v.Id), Is.EqualTo(new[] { "voice-b", "voice-a" }));
            _voices.Rename("voice-a", "Fuwari");
            Assert.That(_voices.Get("voice-a").Value.Name, Is.EqualTo("Fuwari"));
            Assert.That(_voices.Rename("voice-none", "x").Error, Is.EqualTo(CrudError.NotFound));

            _voices.Delete("voice-a");
            Assert.That(_voices.List().Value.Select(v => v.Id), Is.EqualTo(new[] { "voice-b" }));
            Assert.That(_voices.Delete("voice-a").Ok, Is.True, "Gone either way.");
            Assert.That(Directory.Exists(Path.Combine(_folder, "VoiceModels", "prototype")), Is.True);
            Assert.That(VoiceModelStore.IsVoiceId("prototype"), Is.False);
            Assert.That(VoiceModelStore.IsVoiceId("voice-../x"), Is.False);
        }

        [Test]
        public void AVoicesFinish_IsKeptWithIt_AndAnOlderVoiceIsFlat()
        {
            var tone = new UI.Voice.VoiceTone(2f, -1f, 3f, 0f);
            NewVoiceModel source = Voice("voice-a", "A", "{\"medianPitchHz\":280}");
            var withTone = new NewVoiceModel(source.Id, source.Name, source.RunId, source.SourceFolder, source.Files, source.Versions, tone);
            Assert.That(_voices.Add(withTone).Ok, Is.True);
            Assert.That(_voices.Get("voice-a").Value.GetTone(), Is.EqualTo(tone), "Registered with the finish chosen in Voice Lab.");

            var brighter = new UI.Voice.VoiceTone(2f, -1f, 5f, 1f);
            Assert.That(_voices.SetTone("voice-a", brighter).Ok, Is.True);
            Assert.That(_voices.Get("voice-a").Value.GetTone(), Is.EqualTo(brighter));
            Assert.That(_voices.SetTone("voice-none", brighter).Error, Is.EqualTo(ProjectData.Records.CrudError.NotFound));

            Assert.That(_voices.Add(Voice("voice-b", "B", "{}")).Ok, Is.True);
            Assert.That(_voices.Get("voice-b").Value.GetTone().IsFlat, Is.True, "A voice without a finish is flat, as every voice was before.");
        }

        [Test]
        public void AVoicesFinish_KeepsFieldsThisVersionDoesNotKnow()
        {
            string folder = _voices.FolderOf("voice-old");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, VoiceModelStore.ModelFile),
                "{\"schemaVersion\":1,\"id\":\"voice-old\",\"name\":\"Old\",\"registeredAtUtc\":\"2026-11-05T00:00:00.0000000Z\",\"design\":{\"seed\":7}}");

            Assert.That(_voices.SetTone("voice-old", new UI.Voice.VoiceTone(1f, 0f, 0f, 0f)).Ok, Is.True);

            Assert.That(File.ReadAllText(Path.Combine(folder, VoiceModelStore.ModelFile)), Does.Contain("seed"));
        }

        [Test]
        public void AVoiceRegisteredBeforeTheMove_KeepsItsDesignThroughARename()
        {
            // model.json used to hold Voice Lab's design; a field Voice does not know is kept, not dropped (CLAUDE.md 25).
            string folder = _voices.FolderOf("voice-old");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, VoiceModelStore.ModelFile),
                "{\"schemaVersion\":1,\"id\":\"voice-old\",\"name\":\"Old\",\"runId\":\"r\",\"registeredAtUtc\":\"2026-11-05T00:00:00.0000000Z\",\"medianPitchHz\":300,\"design\":{\"seed\":7}}");

            Assert.That(_voices.Rename("voice-old", "New").Ok, Is.True);

            string json = File.ReadAllText(Path.Combine(folder, VoiceModelStore.ModelFile));
            Assert.That(json, Does.Contain("seed"));
            Assert.That(_voices.Get("voice-old").Value.MedianPitchHz, Is.EqualTo(300));
        }

        [TestCase("{\"medianPitchHz\":353}", 353)]
        [TestCase("{}", 0)]
        [TestCase("not json", 0)]
        public void MedianPitch_IsReadFromVoiceJson(string json, int expected)
        {
            Assert.That(VoiceModelStore.MedianPitch(json), Is.EqualTo(expected));
        }

        private NewVoiceModel Voice(string id, string name, string voiceJson, params string[] others)
        {
            string source = Path.Combine(_folder, "source-" + id);
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "voice.sentis"), "model");
            File.WriteAllText(Path.Combine(source, "voice.spk"), "speaker");
            File.WriteAllText(Path.Combine(source, "voice.json"), voiceJson);
            foreach (string file in others)
            {
                File.WriteAllText(Path.Combine(source, file), "{}");
            }

            return new NewVoiceModel(id, name, "run", source, VoiceModelStore.RuntimeFiles.Concat(others).ToArray(), new Dictionary<string, string> { ["rvc"] = "1" });
        }

        private static NewVoiceModel New(string id, params string[] files)
        {
            return new NewVoiceModel(id, "n", "r", "source", files, null);
        }

        private sealed class FixedClock : ISystemClock
        {
            public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
        }
    }
}
