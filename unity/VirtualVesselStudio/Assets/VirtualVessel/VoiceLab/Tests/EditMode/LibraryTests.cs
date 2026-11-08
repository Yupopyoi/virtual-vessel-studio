using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.VoiceLab;
using VirtualVessel.VoiceLab.Design;
using VirtualVessel.Voice.Models;
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
            _library = new VoiceLabLibrary(_runs, _voices, _clock, () => _building);
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
            }).Value;
            return _runs.Save(run.With(state, _clock.UtcNow)).Value;
        }

        // Registers the run's voice as Voice Lab does, with stand-in files.
        private void Voice(TrainingRun run, string name)
        {
            string output = Path.Combine(_runs.FolderOf(run.RunId), "output");
            Directory.CreateDirectory(output);
            foreach (string file in VoiceModelStore.RuntimeFiles)
            {
                File.WriteAllText(Path.Combine(output, file), "{}");
            }

            Assert.That(new VoiceModelRegistrar(_voices, _runs, _clock).Register(run, name).Ok, Is.True);
        }

        [Test]
        public void Runs_ShowTheirWordsTimbreAndVoices_AndABaseOnlyWhenItIsThere()
        {
            string wav = Path.Combine(_folder, "c.wav");
            File.WriteAllText(wav, "x");
            TrainingRun run = Run(TrainingState.Completed, wav);
            Voice(run, "Fuwari");

            VoiceLabRunRecord shown = _library.ListRuns().Value.Single();
            Assert.That(shown.State, Is.EqualTo(VoiceLabBuildState.Completed));
            Assert.That(shown.Words, Is.EqualTo(new[] { "male" }));
            Assert.That(shown.Timbre.Softness, Is.EqualTo(2f));
            Assert.That(shown.VoiceNames, Is.EqualTo(new[] { "Fuwari" }));
            Assert.That(shown.BaseFile, Is.EqualTo(wav));
            Assert.That(_library.ListVoices().Value.Single().BaseFile, Is.EqualTo(wav), "The design goes with the voice.");

            File.Delete(wav);
            Assert.That(_library.ListRuns().Value.Single().BaseFile, Is.Null);
            Assert.That(_library.ListVoices().Value.Single().Name, Is.EqualTo("Fuwari"));
        }

        [Test]
        public void Notes_AndNames_AreSaved_AndANameMustBeOneToSixtyCharacters()
        {
            TrainingRun run = Run(TrainingState.Completed);
            Voice(run, "A");
            _library.SetComment(run.RunId, " bright ");
            _library.RenameVoice(VoiceModelRegistrar.VoiceId(run.RunId), " Fuwari ");

            Assert.That(_library.ListRuns().Value.Single().Comment, Is.EqualTo("bright"));
            Assert.That(_library.ListVoices().Value.Single().Name, Is.EqualTo("Fuwari"));
            Assert.That(_library.RenameVoice(VoiceModelRegistrar.VoiceId(run.RunId), "  ").Error, Is.EqualTo(CrudError.Invalid));
            Assert.That(_library.RenameVoice(VoiceModelRegistrar.VoiceId(run.RunId), new string('a', 61)).Error, Is.EqualTo(CrudError.Invalid));
            Assert.That(_library.SetComment("missing", "x").Error, Is.EqualTo(CrudError.NotFound));
            Assert.That(_library.RenameVoice("voice-none", "x").Error, Is.EqualTo(CrudError.NotFound));
        }

        [Test]
        public void DeletingAVoiceKeepsItsRun_AndDeletingARunKeepsItsVoices()
        {
            TrainingRun run = Run(TrainingState.Completed);
            Voice(run, "A");

            _library.DeleteRun(run.RunId);
            Assert.That(_library.ListRuns().Value, Is.Empty);
            Assert.That(_library.ListVoices().Value.Single().Name, Is.EqualTo("A"));

            _library.DeleteVoice(VoiceModelRegistrar.VoiceId(run.RunId));
            Assert.That(_library.ListVoices().Value, Is.Empty);
        }

        [Test]
        public void ARunBeingMade_CannotBeDeleted_ButOneLeftUnfinishedCan()
        {
            TrainingRun run = Run(TrainingState.Training);
            _building = true;
            Assert.That(_library.DeleteRun(run.RunId).Error, Is.EqualTo(CrudError.Busy));

            // A run left unfinished when the app closed is not being made.
            _building = false;
            Assert.That(_library.DeleteRun(run.RunId).Ok, Is.True);
            Assert.That(_library.ListRuns().Value, Is.Empty);
            Assert.That(_library.DeleteRun(run.RunId).Ok, Is.True, "Gone either way.");
        }

        [TestCase(VoiceLabTrainingLength.Short, 150)]
        [TestCase(VoiceLabTrainingLength.Normal, 300)]
        [TestCase(VoiceLabTrainingLength.Long, 600)]
        public void ATrainingLength_HasItsEpochs(VoiceLabTrainingLength length, int epochs)
        {
            Assert.That(VoiceLabService.EpochsOf(length), Is.EqualTo(epochs));
        }

        [Test]
        public void TheTestPlan_RunsOnlyInTheEditor()
        {
            Assert.That(VoiceLabService.PlanOf(VoiceLabTrainingLength.EditorTest, true), Is.SameAs(BuildPlan.EditorTest));
            Assert.That(VoiceLabService.PlanOf(VoiceLabTrainingLength.EditorTest, false), Is.SameAs(BuildPlan.Standard), "Never a test voice outside the Editor.");
            Assert.That(VoiceLabService.PlanOf(VoiceLabTrainingLength.Long, false).Epochs, Is.EqualTo(600));
            Assert.That(VoiceLabService.PlanOf(VoiceLabTrainingLength.Long, false).SpeechLines, Is.EqualTo(0));
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
