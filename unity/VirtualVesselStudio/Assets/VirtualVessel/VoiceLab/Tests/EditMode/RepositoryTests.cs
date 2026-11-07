using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.VoiceLab.Design;
using VirtualVessel.VoiceLab.Registration;
using VirtualVessel.VoiceLab.Training;

namespace VirtualVessel.VoiceLab.Tests
{
    /// <summary>The run and voice repositories' file implementations (Voice Lab detailed design 6.3, 6.5, 8.6).</summary>
    public sealed class RepositoryTests
    {
        private string _folder;
        private FixedClock _clock;
        private TrainingRunStore _runs;
        private VoiceModelStore _voices;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "vv-voicelab-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            _clock = new FixedClock();
            _runs = new TrainingRunStore(Path.Combine(_folder, "VoiceLab"), _clock, new Random(1));
            _voices = new VoiceModelStore(Path.Combine(_folder, "VoiceModels"), _clock);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_folder, true);
        }

        private static RunDesign Design()
        {
            return new RunDesign
            {
                Instruction = "i",
                Seed = 7,
                Candidate = "c.wav",
                Reference = "r.wav",
                Timbre = new TimbreValues { Pitch = 4f, Softness = 3f },
                Words = new List<string> { "male", "airy" },
                FreeWords = "husky",
            };
        }

        [Test]
        public void ARunKeepsEveryField_ThroughStateChanges()
        {
            // Copy() dropped the reference and softness, added after it was written, on every state change.
            TrainingRun run = _runs.Create(Design()).Value;
            run = _runs.Save(run.With(TrainingState.Training, _clock.UtcNow).WithComment("  good  ")).Value;

            TrainingRun read = _runs.Get(run.RunId).Value;
            Assert.That(read.Design.Reference, Is.EqualTo("r.wav"));
            Assert.That(read.Design.Timbre.Softness, Is.EqualTo(3f));
            Assert.That(read.Design.Words, Is.EqualTo(new[] { "male", "airy" }));
            Assert.That(read.Design.FreeWords, Is.EqualTo("husky"));
            Assert.That(read.Comment, Is.EqualTo("good"));
        }

        [Test]
        public void ARunFromBeforeTheWords_ReadsAsNone()
        {
            string folder = _runs.FolderOf("20261101-120000-0001");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, TrainingRunStore.FileName),
                "{\"schemaVersion\":1,\"runId\":\"20261101-120000-0001\",\"state\":\"Completed\",\"createdAtUtc\":\"2026-11-01T12:00:00Z\",\"design\":{\"instruction\":\"i\",\"seed\":1}}");

            TrainingRun read = _runs.Get("20261101-120000-0001").Value;
            Assert.That(read.Design.Words, Is.Empty);
            Assert.That(read.Design.FreeWords, Is.Empty);
            Assert.That(read.Comment, Is.Empty);
        }

        [Test]
        public void Comments_AreTrimmedAndCut()
        {
            Assert.That(new TrainingRun().WithComment(new string('a', 600)).Comment.Length, Is.EqualTo(TrainingRun.MaxComment));
            Assert.That(new TrainingRun().WithComment(null).Comment, Is.Empty);
        }

        [Test]
        public void Runs_CanBeFoundAndDeleted_ButNeverOutsideTheirFolder()
        {
            TrainingRun run = _runs.Create(Design()).Value;
            Assert.That(_runs.Get(run.RunId).Value.RunId, Is.EqualTo(run.RunId));
            Assert.That(_runs.Get("missing").Error, Is.EqualTo(CrudError.NotFound));
            Assert.That(_runs.Get("..").Error, Is.EqualTo(CrudError.Invalid));
            Assert.Throws<ArgumentException>(() => _runs.FolderOf("../x"));

            Assert.That(_runs.Delete(run.RunId).Ok, Is.True);
            Assert.That(_runs.Get(run.RunId).Error, Is.EqualTo(CrudError.NotFound));
            Assert.That(_runs.List().Value, Is.Empty);
            Assert.That(_runs.Delete(run.RunId).Ok, Is.True, "Gone either way.");
            Assert.That(_runs.Delete("../x").Error, Is.EqualTo(CrudError.Invalid));
        }

        [Test]
        public void Voices_AreListedNewestFirst_RenamedAndDeleted_AndOtherFoldersAreNotVoices()
        {
            Directory.CreateDirectory(Path.Combine(_folder, "VoiceModels", "prototype"));
            Add("voice-a", "A", "2026-11-01T00:00:00.0000000Z");
            Add("voice-b", "B", "2026-11-02T00:00:00.0000000Z");

            Assert.That(_voices.List().Value.Select(v => v.Id), Is.EqualTo(new[] { "voice-b", "voice-a" }));
            _voices.Rename("voice-a", "Fuwari");
            Assert.That(_voices.Get("voice-a").Value.Name, Is.EqualTo("Fuwari"));
            Assert.That(_voices.Rename("voice-none", "x").Error, Is.EqualTo(CrudError.NotFound));

            _voices.Delete("voice-a");
            Assert.That(_voices.List().Value.Select(v => v.Id), Is.EqualTo(new[] { "voice-b" }));
            Assert.That(Directory.Exists(Path.Combine(_folder, "VoiceModels", "prototype")), Is.True);
            Assert.That(VoiceModelStore.IsVoiceId("prototype"), Is.False);
            Assert.That(VoiceModelStore.IsVoiceId("voice-../x"), Is.False);
        }

        [Test]
        public void ADesign_KeepsTheWordsTheCandidateWasAskedWith()
        {
            var candidate = new VoiceCandidate("candidate_7", 7, "c.wav", default, "i", null, new VoiceDescription(new[] { "male" }, "husky"));
            RunDesign design = VoiceDesigner.Design(candidate, new TimbreSettings(0f, 0f, 0f, 0f));
            Assert.That(design.Words, Is.EqualTo(new[] { "male" }));
            Assert.That(design.FreeWords, Is.EqualTo("husky"));
        }

        private void Add(string id, string name, string at)
        {
            Directory.CreateDirectory(_voices.FolderOf(id));
            _voices.Add(new VoiceModelRecord { Id = id, Name = name, RunId = id.Substring(6), RegisteredAtUtc = at });
        }
    }
}
