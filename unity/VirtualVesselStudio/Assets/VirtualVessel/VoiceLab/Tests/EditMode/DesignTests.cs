using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.ExternalServices.Process;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.Voice.Models;
using VirtualVessel.VoiceLab.Design;
using VirtualVessel.VoiceLab.Registration;
using VirtualVessel.VoiceLab.Scripts;
using VirtualVessel.VoiceLab.Training;

namespace VirtualVessel.VoiceLab.Tests
{
    public sealed class DesignTests
    {
        private string _folder;
        private FixedClock _clock;
        private TrainingRunStore _runs;
        private VoiceLabEnvironment _environment;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "vv-voicelab-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            _clock = new FixedClock();
            _runs = new TrainingRunStore(Path.Combine(_folder, "VoiceLab"), _clock, new Random(1));
            _environment = new VoiceLabEnvironment(@"C:\env\Scripts\python.exe", @"C:\s", @"C:\rvc", @"C:\m", @"C:\t.sentis", @"C:\t.json");
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_folder, true);
        }

        [Test]
        public void Candidates_ComeWithSeedPitchAndInstruction()
        {
            var runner = new ScriptedRunner((_, __) => new[]
            {
                "{\"type\":\"progress\",\"stage\":\"candidates\",\"done\":0,\"total\":2}",
                "{\"type\":\"result\",\"instruction\":\"calm\",\"candidates\":[{\"seed\":7,\"file\":\"a.wav\",\"medianHz\":268,\"lowHz\":203,\"highHz\":338},{\"seed\":8,\"file\":\"b.wav\"}]}",
            });

            IReadOnlyList<VoiceCandidate> candidates = Designer(runner).MakeCandidatesAsync(new VoiceDescription(null, ""), 2, 7, null, null, CancellationToken.None).Result;

            Assert.That(candidates.Select(c => c.Seed), Is.EqualTo(new[] { 7, 8 }));
            Assert.That(candidates.Select(c => c.Id), Is.EqualTo(new[] { "candidate_1_7", "candidate_1_8" }));
            Assert.That(candidates[0].Pitch.MedianHz, Is.EqualTo(268));
            Assert.That(candidates[1].Pitch.MedianHz, Is.EqualTo(0));
            Assert.That(candidates[0].Instruction, Is.EqualTo("calm"));
        }

        [Test]
        public void Candidates_RequestCarriesTheShelfWordsAndTheGivenSeed()
        {
            var runner = new ScriptedRunner((_, __) => new[] { "{\"type\":\"result\",\"instruction\":\"x\",\"candidates\":[]}" });
            VoiceDesigner designer = Designer(runner);
            var mood = new VoiceDescription(new[] { "male", "airy" }, "husky");

            designer.MakeCandidatesAsync(mood, 6, 100, null, null, CancellationToken.None).Wait();
            designer.MakeCandidatesAsync(mood, 6, 100, null, null, CancellationToken.None).Wait();

            ConfigDocument[] requests = runner.Requests.Select(r => ConfigDocument.Parse(File.ReadAllText(r.ScriptArgument(0)))).ToArray();
            Assert.That(requests.Select(r => r.Get<int>("firstSeed")), Is.EqualTo(new[] { 100, 100 }), "The screen decides the seed, also to make the same candidates again.");
            Assert.That(requests[0].Get<string>("output"), Is.Not.EqualTo(requests[1].Get<string>("output")), "Each call has its own folder, so a seed given again does not overwrite a candidate.");
            Assert.That(requests[0].Get<List<string>>("shelf"), Is.EqualTo(new[] { "male", "airy" }));
            Assert.That(requests[0].Get<string>("words"), Is.EqualTo("husky"), "The script cleans the words and builds the instruction.");
        }

        [Test]
        public void AWrittenPrompt_IsSentAsTheRequestsPrompt_AndOnlyThen()
        {
            var runner = new ScriptedRunner((_, __) => new[] { "{\"type\":\"result\",\"instruction\":\"x\",\"candidates\":[]}" });
            VoiceDesigner designer = Designer(runner);

            designer.MakeCandidatesAsync(new VoiceDescription(null, null, "A calm woman."), 1, 100, null, null, CancellationToken.None).Wait();
            designer.MakeCandidatesAsync(new VoiceDescription(new[] { "male" }, ""), 1, 100, null, null, CancellationToken.None).Wait();

            ConfigDocument[] requests = runner.Requests.Select(r => ConfigDocument.Parse(File.ReadAllText(r.ScriptArgument(0)))).ToArray();
            Assert.That(requests[0].Get<string>("prompt"), Is.EqualTo("A calm woman."), "The script cleans it and uses it in place of the words.");
            Assert.That(requests[1].Contains("prompt"), Is.False, "Words alone send no prompt.");
        }

        [Test]
        public void AWrittenLine_IsSent_AndKeptInTheRun_ButTheDefaultIsNotSent()
        {
            var runner = new ScriptedRunner((_, __) => new[] { "{\"type\":\"result\",\"instruction\":\"x\",\"candidates\":[{\"seed\":3,\"file\":\"a.wav\"}]}" });
            VoiceDesigner designer = Designer(runner);

            VoiceCandidate made = designer.MakeCandidatesAsync(new VoiceDescription(null, "", null, "おはよう。"), 1, 3, null, null, CancellationToken.None).Result.Single();
            designer.MakeCandidatesAsync(new VoiceDescription(null, ""), 1, 3, null, null, CancellationToken.None).Wait();

            ConfigDocument[] requests = runner.Requests.Select(r => ConfigDocument.Parse(File.ReadAllText(r.ScriptArgument(0)))).ToArray();
            Assert.That(requests[0].Get<string>("line"), Is.EqualTo("おはよう。"));
            Assert.That(requests[1].Contains("line"), Is.False, "Without one, the script reads the shelf's line.");
            Assert.That(VoiceDesigner.Design(made, default).Line, Is.EqualTo("おはよう。"), "Kept in run.json, to make the candidate again.");
        }

        [Test]
        public void Samples_RequestNamesTheLanguagesAndFolder()
        {
            var runner = new ScriptedRunner((_, __) => new[] { "{\"type\":\"result\",\"count\":27}" });
            string folder = Path.Combine(_folder, "Samples");
            Designer(runner).MakeSamplesAsync(new[] { "ja" }, folder, null, CancellationToken.None).Wait();

            ConfigDocument request = ConfigDocument.Parse(File.ReadAllText(runner.Requests.Single().ScriptArgument(0)));
            Assert.That(runner.Requests.Single().ScriptArgument(1), Is.EqualTo("samples"));
            Assert.That(request.Get<List<string>>("languages"), Is.EqualTo(new[] { "ja" }));
            Assert.That(request.Get<string>("output"), Is.EqualTo(folder));
        }

        [Test]
        public void CandidatesNearAPickedOne_SendItAsTheReference_AndRememberIt()
        {
            var runner = new ScriptedRunner((_, __) => new[] { "{\"type\":\"result\",\"instruction\":\"x\",\"reference\":\"C:/d/candidate_7.wav\",\"candidates\":[{\"seed\":13,\"file\":\"c.wav\"}]}" });
            VoiceDesigner designer = Designer(runner);

            VoiceCandidate made = designer.MakeCandidatesAsync(new VoiceDescription(null, ""), 1, 13, Candidate(), null, CancellationToken.None).Result.Single();

            ConfigDocument request = ConfigDocument.Parse(File.ReadAllText(runner.Requests.Single().ScriptArgument(0)));
            Assert.That(request.Get<string>("reference"), Is.EqualTo(Candidate().File));
            Assert.That(made.Reference, Is.EqualTo("C:/d/candidate_7.wav"));
            Assert.That(VoiceDesigner.Design(made, default).Reference, Is.EqualTo("C:/d/candidate_7.wav"), "Kept in run.json, to make the voice again.");
        }

        [Test]
        public void CandidatesFromTheMoodAlone_HaveNoReference()
        {
            var runner = new ScriptedRunner((_, __) => new[] { "{\"type\":\"result\",\"instruction\":\"x\",\"candidates\":[{\"seed\":1,\"file\":\"a.wav\"}]}" });

            VoiceCandidate made = Designer(runner).MakeCandidatesAsync(new VoiceDescription(null, ""), 1, 1, null, null, CancellationToken.None).Result.Single();

            Assert.That(ConfigDocument.Parse(File.ReadAllText(runner.Requests.Single().ScriptArgument(0))).Get<string>("reference"), Is.Null);
            Assert.That(made.Reference, Is.Null);
        }

        [Test]
        public void Build_RunsTheFourStepsAndCompletes()
        {
            var runner = new ScriptedRunner((script, _) => script == "generate.py"
                ? new[] { "{\"type\":\"result\",\"versions\":{\"torch\":\"2.7.1\"}}" }
                : new[] { "{\"type\":\"progress\",\"stage\":\"s\",\"done\":1,\"total\":2}", "{\"type\":\"result\"}" });
            var reported = new List<BuildProgress>();

            TrainingRun run = Designer(runner).BuildVoiceAsync(Candidate(), new TimbreSettings(4, 4, 0, 0), BuildPlan.Training(600), new SynchronousProgress<BuildProgress>(reported.Add), CancellationToken.None).Result;

            Assert.That(runner.Ran, Is.EqualTo(new[] { "generate.py speech", "timbre.py batch", "train.py run", "export_voice.py run" }));
            Assert.That(run.GetState(), Is.EqualTo(TrainingState.Completed));
            Assert.That(run.Versions["torch"], Is.EqualTo("2.7.1"));
            Assert.That(run.Design.Timbre.Age, Is.EqualTo(4f));
            Assert.That(run.Design.Epochs, Is.EqualTo(600), "Kept in run.json, to make the voice again.");
            Assert.That(run.Design.SpeechLines, Is.EqualTo(0));
            Assert.That(reported.Select(p => p.Fraction), Is.Ordered);
            Assert.That(reported.Last().State, Is.EqualTo(TrainingState.Completed));
            TrainingRun stored = _runs.List().Value.Single();
            Assert.That(stored.GetState(), Is.EqualTo(TrainingState.Completed));
            Assert.That(stored.Design.Seed, Is.EqualTo(7));
            Assert.That(File.ReadAllText(Path.Combine(_runs.FolderOf(run.RunId), TrainingRunStore.FileName)), Does.Contain("\"state\": \"Completed\""));
        }

        [Test]
        public void Build_AFailedStepEndsTheRunAsFailedAndKeepsIt()
        {
            var runner = new ScriptedRunner(
                (script, _) => script == "train.py" ? new[] { "{\"type\":\"error\",\"code\":\"out_of_memory\",\"message\":\"GPU\"}" } : new[] { "{\"type\":\"result\"}" },
                (script, _) => script == "train.py" ? ExternalProcessState.Failed : ExternalProcessState.Completed);

            TrainingRun run = Designer(runner).BuildVoiceAsync(Candidate(), default, BuildPlan.Standard, null, CancellationToken.None).Result;

            Assert.That(run.GetState(), Is.EqualTo(TrainingState.Failed));
            Assert.That(run.ErrorCode, Is.EqualTo("out_of_memory"));
            Assert.That(runner.Ran.Last(), Is.EqualTo("train.py run"));
            Assert.That(_runs.List().Value.Single().ErrorCode, Is.EqualTo("out_of_memory"));
        }

        [Test]
        public void Build_CancellingEndsTheRunAsCancelled()
        {
            var runner = new ScriptedRunner(
                (script, _) => script == "generate.py" ? new[] { "{\"type\":\"result\"}" } : Array.Empty<string>(),
                (script, _) => script == "timbre.py" ? ExternalProcessState.Cancelled : ExternalProcessState.Completed);

            TrainingRun run = Designer(runner).BuildVoiceAsync(Candidate(), default, BuildPlan.Standard, null, CancellationToken.None).Result;

            Assert.That(run.GetState(), Is.EqualTo(TrainingState.Cancelled));
            Assert.That(run.ErrorCode, Is.EqualTo("cancelled"));
        }

        [Test]
        public void Fraction_GivesTrainingMoreOfTheBar_WhenItTrainsLonger()
        {
            // At 600 epochs training takes 36 of 45 minutes, so less of the bar comes before it.
            Assert.That(VoiceDesigner.Minutes(2, BuildPlan.Training(600)), Is.EqualTo(36f).Within(1e-5));
            Assert.That(VoiceDesigner.Minutes(0, BuildPlan.Training(600)), Is.EqualTo(7f).Within(1e-5));
            Assert.That(VoiceDesigner.Fraction(2, 0, 600, BuildPlan.Training(600)), Is.EqualTo(8f / 45f).Within(1e-5));
            Assert.That(VoiceDesigner.Fraction(3, 1, 1, BuildPlan.Training(600)), Is.EqualTo(1f).Within(1e-5));

            // The Editor's test plan: 4 of 48 lines and one epoch, so speech and training shrink but export does not.
            Assert.That(VoiceDesigner.Minutes(0, BuildPlan.EditorTest), Is.EqualTo(7f * 4f / 48f).Within(1e-5));
            Assert.That(VoiceDesigner.Minutes(3, BuildPlan.EditorTest), Is.EqualTo(1f).Within(1e-5));
        }

        [Test]
        public void Fraction_RisesThroughTheStepsToOne()
        {
            Assert.That(VoiceDesigner.Fraction(0, 0, 10), Is.EqualTo(0f));
            // Speech 7 and timbre 1 of 18 minutes come before training at 150 epochs.
            Assert.That(VoiceDesigner.Fraction(2, 0, 150), Is.EqualTo(8f / 18f).Within(1e-5));
            Assert.That(VoiceDesigner.Fraction(3, 3, 3), Is.EqualTo(1f).Within(1e-5));
            Assert.That(VoiceDesigner.Fraction(1, 5, 0), Is.EqualTo(VoiceDesigner.Fraction(1, 0, 1)));
        }

        [Test]
        public void StepRequests_ChainEachStepsOutputIntoTheNext()
        {
            string folder = @"C:\runs\r1";
            ConfigDocument speech = VoiceDesigner.StepRequest(0, Candidate(), default, folder, _environment);
            ConfigDocument timbre = VoiceDesigner.StepRequest(1, Candidate(), new TimbreSettings(4, 0, 0, 0, 2), folder, _environment);
            ConfigDocument train = VoiceDesigner.StepRequest(2, Candidate(), default, folder, _environment);
            Assert.That(VoiceDesigner.StepRequest(2, Candidate(), default, folder, _environment, BuildPlan.Training(600)).Get<int>("epochs"), Is.EqualTo(600));
            Assert.That(VoiceDesigner.StepRequest(0, Candidate(), default, folder, _environment, BuildPlan.EditorTest).Get<int>("limit"), Is.EqualTo(4));
            Assert.That(speech.Contains("limit"), Is.False, "Every line unless the plan says fewer.");
            ConfigDocument export = VoiceDesigner.StepRequest(3, Candidate(), default, folder, _environment);

            Assert.That(timbre.Get<string>("source"), Is.EqualTo(speech.Get<string>("output")));
            Assert.That(timbre.Get<float>("timbre.pitch"), Is.EqualTo(4f));
            Assert.That(timbre.Get<float>("timbre.softness"), Is.EqualTo(2f));
            Assert.That(VoiceDesigner.Design(Candidate(), new TimbreSettings(0, 0, 0, 0, 2)).Timbre.Softness, Is.EqualTo(2f), "Kept in run.json, to make the voice again.");
            Assert.That(train.Get<string>("data"), Is.EqualTo(timbre.Get<string>("output")));
            Assert.That(export.Get<string>("weights"), Is.EqualTo(Path.Combine(train.Get<string>("work"), "voice.pth")));
            Assert.That(export.Get<string>("speech"), Is.EqualTo(timbre.Get<string>("output")));
            Assert.That(export.Get<int>("frames"), Is.EqualTo(100));
            Assert.That(train.Get<int>("epochs"), Is.EqualTo(150));
        }

        [Test]
        public void PreviewName_IsTheSameForTheSameTimbre()
        {
            Assert.That(VoiceDesigner.PreviewName("candidate_7", new TimbreSettings(4, -2.5f, 0, 0)), Is.EqualTo("candidate_7_p+4_a-2.5_b0_r0_s0.wav"));
            Assert.That(VoiceDesigner.PreviewName("candidate_7", new TimbreSettings(0, 0, 0, 0, 3)), Is.EqualTo("candidate_7_p0_a0_b0_r0_s+3.wav"));
            Assert.That(new TimbreSettings(0, 0, 0, 0, -2).Softness, Is.EqualTo(0f), "There is no pressed side.");
            Assert.That(new TimbreSettings(0, 0, 0, 0, 9).Softness, Is.EqualTo(6f));
            Assert.That(new TimbreSettings(9, 0, 0, 0).Pitch, Is.EqualTo(6f));
        }

        [Test]
        public void Run_ChangesMakeCopies()
        {
            TrainingRun start = TrainingRun.Start("20261104-120000-0001", new RunDesign { Seed = 7 }, _clock.UtcNow);

            TrainingRun later = start.With(TrainingState.Training, _clock.UtcNow.AddMinutes(1)).WithVersions(new Dictionary<string, string> { ["torch"] = "2.7.1" });

            Assert.That(start.GetState(), Is.EqualTo(TrainingState.Pending));
            Assert.That(start.Versions, Is.Empty);
            Assert.That(later.GetState(), Is.EqualTo(TrainingState.Training));
            Assert.That(later.Design.Seed, Is.EqualTo(7));
            Assert.That(later.Design, Is.Not.SameAs(start.Design));
            Assert.That(later.UpdatedAtUtc, Is.Not.EqualTo(start.UpdatedAtUtc));
        }

        [Test]
        public void Run_AnUnknownStateCountsAsFailed()
        {
            Assert.That(new TrainingRun { State = "Distilling" }.GetState(), Is.EqualTo(TrainingState.Failed));
        }

        [Test]
        public void NewId_SortsByTime()
        {
            Assert.That(TrainingRun.NewId(new DateTimeOffset(2026, 11, 4, 1, 2, 3, TimeSpan.Zero), 0x1abcd), Is.EqualTo("20261104-010203-abcd"));
        }

        [Test]
        public void Store_ListsNewestFirstAndSkipsUnreadableRuns()
        {
            TrainingRun first = _runs.Create(new RunDesign()).Value;
            _clock.UtcNow = _clock.UtcNow.AddMinutes(5);
            TrainingRun second = _runs.Create(new RunDesign()).Value;
            string broken = Path.Combine(_folder, "VoiceLab", TrainingRunStore.FolderName, "broken");
            Directory.CreateDirectory(broken);
            File.WriteAllText(Path.Combine(broken, TrainingRunStore.FileName), "{");

            Assert.That(_runs.List().Value.Select(r => r.RunId), Is.EqualTo(new[] { second.RunId, first.RunId }));
        }

        [Test]
        public void Register_CopiesACompletedRunsVoice()
        {
            TrainingRun run = _runs.Save(_runs.Create(new RunDesign { Seed = 7 }).Value.With(TrainingState.Completed, _clock.UtcNow)).Value;
            string output = Path.Combine(_runs.FolderOf(run.RunId), "output");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "voice.sentis"), "model");
            File.WriteAllText(Path.Combine(output, "voice.spk"), "speaker");
            File.WriteAllText(Path.Combine(output, "voice.json"), "{\"medianPitchHz\":360}");
            var voices = new VoiceModelStore(Path.Combine(_folder, "VoiceModels"), _clock);
            var registrar = new VoiceModelRegistrar(voices, _runs, _clock);

            VoiceModelRecord voice = registrar.Register(run, "My voice").Value;

            string folder = voices.FolderOf(voice.Id);
            Assert.That(voice.MedianPitchHz, Is.EqualTo(360));
            Assert.That(voice.RunId, Is.EqualTo(run.RunId));
            Assert.That(Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n), Is.EqualTo(new[] { "design.json", "model.json", "voice.json", "voice.sentis", "voice.spk" }));
            ConfigDocument model = ConfigDocument.Parse(File.ReadAllText(Path.Combine(folder, VoiceModelStore.ModelFile)));
            Assert.That(model.Get<string>("name"), Is.EqualTo("My voice"));
            Assert.That(model.Contains("design"), Is.False, "Voice does not keep Voice Lab's design.");
            Assert.That(VoiceModelRegistrar.ReadDesign(folder, _clock).Seed, Is.EqualTo(7));
            Assert.That(Directory.Exists(folder + ".part"), Is.False);
        }

        [Test]
        public void Register_RefusesARunThatDidNotComplete()
        {
            TrainingRun run = _runs.Create(new RunDesign()).Value;
            var registrar = new VoiceModelRegistrar(new VoiceModelStore(Path.Combine(_folder, "VoiceModels"), _clock), _runs, _clock);

            Assert.That(registrar.Register(run, "x").Error, Is.EqualTo(CrudError.Invalid));
        }

        [Test]
        public void Register_RefusesARunWithoutItsVoice()
        {
            TrainingRun run = _runs.Save(_runs.Create(new RunDesign()).Value.With(TrainingState.Completed, _clock.UtcNow)).Value;
            var registrar = new VoiceModelRegistrar(new VoiceModelStore(Path.Combine(_folder, "VoiceModels"), _clock), _runs, _clock);

            Assert.That(registrar.Register(run, "x").Error, Is.EqualTo(CrudError.Invalid));
        }

        [Test]
        public void AVoiceWithoutADesign_HasNone()
        {
            Assert.That(VoiceModelRegistrar.ReadDesign(_folder, _clock), Is.Null);
        }

        private VoiceDesigner Designer(IExternalProcessRunner runner)
        {
            return new VoiceDesigner(new PythonScripts(_environment, runner), _runs, Path.Combine(_folder, "VoiceLab", "Designs"), _clock, new NullLog());
        }

        private static VoiceCandidate Candidate()
        {
            return new VoiceCandidate("candidate_7", 7, @"C:\d\candidate_7.wav", new PitchSummary(268, 203, 338), "calm");
        }

        // Progress<T> posts to a synchronization context; this reports at once, so a test sees every value.
        private sealed class SynchronousProgress<T> : IProgress<T>
        {
            private readonly Action<T> _report;

            public SynchronousProgress(Action<T> report)
            {
                _report = report;
            }

            public void Report(T value)
            {
                _report(value);
            }
        }
    }
}
