using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualVessel.UI.VoiceLab;
using VirtualVessel.VoiceLab.Design;
using VirtualVessel.VoiceLab.Registration;
using VirtualVessel.VoiceLab.Training;

namespace VirtualVessel.VoiceLab.Tests
{
    /// <summary>The Made voices tab's library and making a run again (Voice Lab detailed design 8.6).</summary>
    public sealed class LibraryTests
    {
        private string _folder;
        private FixedClock _clock;
        private TrainingRunStore _runs;
        private VoiceModelStore _voices;
        private bool _building;
        private VoiceLabLibrary _library;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "vv-voicelab-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            _clock = new FixedClock();
            _runs = new TrainingRunStore(Path.Combine(_folder, "VoiceLab"), _clock, new Random(1));
            _voices = new VoiceModelStore(Path.Combine(_folder, "VoiceModels"), _clock);
            _building = false;
            _library = new VoiceLabLibrary(_runs, _voices, () => _building, new NullLog());
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_folder, true);
        }

        private TrainingRun Run(TrainingState state, string candidate = null)
        {
            TrainingRun run = _runs.Create(new RunDesign
            {
                Seed = 7,
                Candidate = candidate,
                Timbre = new TimbreValues { Pitch = 4f, Softness = 2f },
                Words = new List<string> { "male" },
                FreeWords = "husky",
                MedianHz = 140,
            });
            return _runs.Save(run.With(state, _clock.UtcNow));
        }

        private void Voice(string runId, string name)
        {
            string id = VoiceModelRegistrar.VoiceId(runId);
            Directory.CreateDirectory(_voices.FolderOf(id));
            _voices.Add(new VoiceModelRecord { Id = id, Name = name, RunId = runId, RegisteredAtUtc = "2026-11-07T00:00:00.0000000Z" });
        }

        [Test]
        public void Runs_ShowTheirWordsTimbreAndVoices_AndABaseOnlyWhenItIsThere()
        {
            string wav = Path.Combine(_folder, "c.wav");
            File.WriteAllText(wav, "x");
            TrainingRun run = Run(TrainingState.Completed, wav);
            Voice(run.RunId, "Fuwari");

            VoiceLabRunRecord shown = _library.ListRuns().Single();
            Assert.That(shown.State, Is.EqualTo(VoiceLabBuildState.Completed));
            Assert.That(shown.Words, Is.EqualTo(new[] { "male" }));
            Assert.That(shown.Timbre.Softness, Is.EqualTo(2f));
            Assert.That(shown.VoiceNames, Is.EqualTo(new[] { "Fuwari" }));
            Assert.That(shown.BaseFile, Is.EqualTo(wav));

            File.Delete(wav);
            Assert.That(_library.ListRuns().Single().BaseFile, Is.Null);
            Assert.That(_library.ListVoices().Single().Name, Is.EqualTo("Fuwari"));
        }

        [Test]
        public void Notes_AndNames_AreSaved_AndANameMustBeOneToSixtyCharacters()
        {
            TrainingRun run = Run(TrainingState.Completed);
            Voice(run.RunId, "A");
            _library.SetComment(run.RunId, " bright ");
            _library.RenameVoice(VoiceModelRegistrar.VoiceId(run.RunId), " Fuwari ");

            Assert.That(_library.ListRuns().Single().Comment, Is.EqualTo("bright"));
            Assert.That(_library.ListVoices().Single().Name, Is.EqualTo("Fuwari"));
            Assert.Throws<VoiceLabFailedException>(() => _library.RenameVoice(VoiceModelRegistrar.VoiceId(run.RunId), "  "));
            Assert.Throws<VoiceLabFailedException>(() => _library.RenameVoice(VoiceModelRegistrar.VoiceId(run.RunId), new string('a', 61)));
            Assert.Throws<VoiceLabFailedException>(() => _library.SetComment("missing", "x"));
        }

        [Test]
        public void DeletingAVoiceKeepsItsRun_AndDeletingARunKeepsItsVoices()
        {
            TrainingRun run = Run(TrainingState.Completed);
            Voice(run.RunId, "A");

            _library.DeleteRun(run.RunId);
            Assert.That(_library.ListRuns(), Is.Empty);
            Assert.That(_library.ListVoices().Single().Name, Is.EqualTo("A"));

            _library.DeleteVoice(VoiceModelRegistrar.VoiceId(run.RunId));
            Assert.That(_library.ListVoices(), Is.Empty);
        }

        [Test]
        public void ARunBeingMade_CannotBeDeleted_ButOneLeftUnfinishedCan()
        {
            TrainingRun run = Run(TrainingState.Training);
            _building = true;
            Assert.Throws<VoiceLabFailedException>(() => _library.DeleteRun(run.RunId));

            // A run left unfinished when the app closed is not being made.
            _building = false;
            _library.DeleteRun(run.RunId);
            Assert.That(_library.ListRuns(), Is.Empty);
        }

        [Test]
        public void ARun_ComesBackAsACandidate_OnlyWhileItsBaseIsThere()
        {
            var design = new RunDesign { Seed = 7, Candidate = "c.wav", Instruction = "i", Words = new List<string> { "male" }, FreeWords = "husky", MedianHz = 140, LowHz = 100, HighHz = 200 };
            VoiceCandidate candidate = VoiceLabService.FromRun(design, _ => true);
            Assert.That(candidate.Id, Is.EqualTo("candidate_7"));
            Assert.That(candidate.Pitch.MedianHz, Is.EqualTo(140));
            Assert.That(candidate.Description.Words, Is.EqualTo(new[] { "male" }));
            Assert.That(VoiceLabService.FromRun(design, _ => false), Is.Null);
            Assert.That(VoiceLabService.ToScreen(new TimbreValues { Pitch = 4f, Softness = 2f }).Softness, Is.EqualTo(2f));
            Assert.That(VoiceLabService.ToScreen((TimbreValues)null).Pitch, Is.EqualTo(0f));
        }
    }
}
