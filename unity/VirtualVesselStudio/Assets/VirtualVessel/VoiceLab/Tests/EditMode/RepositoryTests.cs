using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.VoiceLab.Design;
using VirtualVessel.VoiceLab.Training;

namespace VirtualVessel.VoiceLab.Tests
{
    /// <summary>The run repository's file implementation (Voice Lab detailed design 6.3, 8.6).</summary>
    public sealed class RepositoryTests
    {
        private string _folder;
        private FixedClock _clock;
        private TrainingRunStore _runs;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "vv-voicelab-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            _clock = new FixedClock();
            _runs = new TrainingRunStore(Path.Combine(_folder, "VoiceLab"), _clock, new Random(1));
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
        public void ADesignsCopy_CarriesEveryField_AndSharesNothingToChange()
        {
            RunDesign design = Design();
            design.Epochs = 600;
            design.SpeechLines = 4;
            design.MedianHz = 300;
            design.LowHz = 200;
            design.HighHz = 400;

            RunDesign copy = design.Copy();

            // Every property, so a field added later cannot be left out of the copy unnoticed.
            foreach (System.Reflection.PropertyInfo property in typeof(RunDesign).GetProperties())
            {
                object value = property.GetValue(design);
                Assert.That(value, Is.Not.EqualTo(property.PropertyType.IsValueType ? Activator.CreateInstance(property.PropertyType) : null), $"Set {property.Name} in this test.");
                if (property.PropertyType == typeof(TimbreValues))
                {
                    Assert.That(((TimbreValues)property.GetValue(copy)).Softness, Is.EqualTo(3f));
                }
                else
                {
                    Assert.That(property.GetValue(copy), Is.EqualTo(value), property.Name);
                }
            }

            Assert.That(copy.Words, Is.Not.SameAs(design.Words));
            Assert.That(copy.Timbre, Is.Not.SameAs(design.Timbre));
        }

        [Test]
        public void ARunKeepsFieldsThisVersionDoesNotKnow_ThroughASave()
        {
            TrainingRun run = _runs.Create(Design()).Value;
            string file = Path.Combine(_runs.FolderOf(run.RunId), TrainingRunStore.FileName);
            File.WriteAllText(file, File.ReadAllText(file).TrimEnd().TrimEnd('}') + ",\"fromANewerVersion\":\"kept\"}");

            _runs.Save(_runs.Get(run.RunId).Value.With(TrainingState.Training, _clock.UtcNow));

            Assert.That(File.ReadAllText(file), Does.Contain("fromANewerVersion"));
            Assert.That(_runs.Get(run.RunId).Value.State, Is.EqualTo(nameof(TrainingState.Training)));
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
        public void ADesign_KeepsTheWordsTheCandidateWasAskedWith()
        {
            var candidate = new VoiceCandidate("candidate_7", 7, "c.wav", default, "i", null, new VoiceDescription(new[] { "male" }, "husky"));
            RunDesign design = VoiceDesigner.Design(candidate, new TimbreSettings(0f, 0f, 0f, 0f));
            Assert.That(design.Words, Is.EqualTo(new[] { "male" }));
            Assert.That(design.FreeWords, Is.EqualTo("husky"));
        }
    }
}
