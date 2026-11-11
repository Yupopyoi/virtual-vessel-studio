using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.VoiceLab.Scripts;
using VirtualVessel.VoiceLab.Training;

namespace VirtualVessel.VoiceLab.Design
{
    /// <summary>How far building a voice is, in the states the screens show (system design 13.15).</summary>
    public readonly struct BuildProgress
    {
        public BuildProgress(TrainingState state, float fraction)
        {
            State = state;
            Fraction = fraction;
        }

        public TrainingState State { get; }

        /// <summary>The whole build, from 0 to 1.</summary>
        public float Fraction { get; }
    }

    /// <summary>How much a build makes: training epochs and lines of training speech (Voice Lab detailed design 8.3.3).</summary>
    public sealed class BuildPlan
    {
        /// <summary>The training speech's lines; a plan that takes them all says 0.</summary>
        public const int CorpusLines = 48;

        public BuildPlan(int epochs, int speechLines)
        {
            Epochs = epochs;
            SpeechLines = speechLines;
        }

        /// <summary>RVC's default and every line, as the experiment trained.</summary>
        public static BuildPlan Standard { get; } = new BuildPlan(VoiceDesigner.DefaultEpochs, 0);

        /// <summary>
        /// For checking the screens in Unity's Editor: a few lines and one epoch, minutes instead of tens of minutes.
        /// The voice is not one to use.
        /// </summary>
        public static BuildPlan EditorTest { get; } = new BuildPlan(1, 4);

        public int Epochs { get; }

        /// <summary>Lines of training speech; 0 for all of them.</summary>
        public int SpeechLines { get; }

        public static BuildPlan Training(int epochs)
        {
            return new BuildPlan(epochs, 0);
        }
    }

    /// <summary>
    /// The flow of designing a voice (Voice Lab detailed design 8.3): candidates from a description, timbre
    /// previews, and building one into a Training Run. Each step runs a script; a build runs four in turn.
    /// Progress arrives on a background thread; passing it to the main thread is the caller's part.
    /// </summary>
    public sealed class VoiceDesigner
    {
        /// <summary>RVC's default, as the experiment trained (Voice Lab detailed design 8.3.3).</summary>
        public const int DefaultEpochs = 150;

        // The build's steps and their minutes, from the measured run at 150 epochs (decision record, 2026-11-04):
        // speech 7, timbre 1, training 9, export 1. Training grows with the epochs; the rest does not.
        private static readonly BuildStep[] s_steps =
        {
            new BuildStep(TrainingState.Preparing, "generate.py", "speech", 7f),
            new BuildStep(TrainingState.Preprocessing, "timbre.py", "batch", 1f),
            new BuildStep(TrainingState.Training, "train.py", "run", 9f),
            new BuildStep(TrainingState.PostProcessing, "export_voice.py", "run", 1f),
        };

        private const int TrainingStep = 2;
        private const int PostProcessingStep = 3;

        private readonly PythonScripts _scripts;
        private readonly ITrainingRunRepository _runs;
        private readonly ISystemClock _clock;
        private readonly ILog _log;
        private readonly string _session;
        private int _nextSeed;

        /// <param name="designsFolder">Candidates and previews of this session go in a folder of their own here.</param>
        /// <summary>This session's folder of candidates and previews, which is never deleted while it runs.</summary>
        public string SessionFolder => _session;

        public VoiceDesigner(PythonScripts scripts, ITrainingRunRepository runs, string designsFolder, ISystemClock clock, ILog log, int firstSeed)
        {
            _scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _session = Path.Combine(designsFolder, clock.UtcNow.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            _nextSeed = firstSeed;
        }

        /// <param name="near">A candidate to make the new ones near, or null to make them from the description alone.</param>
        public async Task<IReadOnlyList<VoiceCandidate>> MakeCandidatesAsync(VoiceDescription description, int count, VoiceCandidate near, IProgress<ScriptProgress> progress, CancellationToken cancellationToken)
        {
            ConfigDocument request = ConfigDocument.Create();
            if (near != null)
            {
                request.Set("reference", near.File);
            }

            description.WriteTo(request);
            request.Set("count", count);
            request.Set("firstSeed", _nextSeed);
            request.Set("model", _scripts.Environment.VoxCpmModel);
            request.Set("output", Path.Combine(_session, "candidates"));
            _nextSeed += count;
            ConfigDocument result = await _scripts.RunAsync("generate.py", "candidates", request, _session, progress, cancellationToken).ConfigureAwait(false);
            return Candidates(result, description);
        }

        /// <summary>STEP 1's samples into <paramref name="folder"/>/&lt;language&gt;/ (generate.py samples).</summary>
        public async Task MakeSamplesAsync(IReadOnlyList<string> languages, string folder, IProgress<ScriptProgress> progress, CancellationToken cancellationToken)
        {
            ConfigDocument request = ConfigDocument.Create();
            request.Set("languages", new List<string>(languages));
            request.Set("model", _scripts.Environment.VoxCpmModel);
            request.Set("output", folder);
            await _scripts.RunAsync("generate.py", "samples", request, folder, progress, cancellationToken).ConfigureAwait(false);
        }

        public async Task<TimbrePreview> PreviewTimbreAsync(VoiceCandidate candidate, TimbreSettings timbre, CancellationToken cancellationToken)
        {
            ConfigDocument request = ConfigDocument.Create();
            request.Set("source", candidate.File);
            timbre.WriteTo(request, "timbre");
            request.Set("output", Path.Combine(_session, "previews", PreviewName(candidate.Id, timbre)));
            ConfigDocument result = await _scripts.RunAsync("timbre.py", "preview", request, _session, null, cancellationToken).ConfigureAwait(false);
            return new TimbrePreview(result.Get<string>("output"), PitchSummary.From(result));
        }

        /// <summary>
        /// Builds a voice from a candidate and timbre into a new Training Run. Failures and cancelling end the run
        /// in that state and come back as the run, not as an exception (system design 13.22).
        /// </summary>
        /// <param name="plan">How long to train, and on how much speech: longer keeps more of the voice's texture (8.3.3).</param>
        public async Task<TrainingRun> BuildVoiceAsync(VoiceCandidate candidate, TimbreSettings timbre, BuildPlan plan, IProgress<BuildProgress> progress, CancellationToken cancellationToken)
        {
            CrudResult<TrainingRun> created = _runs.Create(Design(candidate, timbre, plan));
            if (!created.Ok)
            {
                // No run to keep the failure in: it comes back as a failed run without an ID.
                _log.Error($"Building a voice could not start: {created.Detail}");
                return TrainingRun.Start(null, Design(candidate, timbre, plan), _clock.UtcNow).Ended(TrainingState.Failed, "io", created.Detail, _clock.UtcNow);
            }

            TrainingRun run = created.Value;
            string folder = _runs.FolderOf(run.RunId);
            ILog log = _log.WithContext("runId", run.RunId);
            log.Information("Building a voice.");
            try
            {
                for (int i = 0; i < s_steps.Length; i++)
                {
                    BuildStep step = s_steps[i];
                    run = Keep(run.With(step.State, _clock.UtcNow));
                    int index = i;
                    var scriptProgress = new Forward<ScriptProgress>(p => progress?.Report(new BuildProgress(step.State, Fraction(index, p.Done, p.Total, plan))));
                    ConfigDocument result = await _scripts.RunAsync(step.Script, step.Operation, StepRequest(index, candidate, timbre, folder, _scripts.Environment, plan), folder, scriptProgress, cancellationToken).ConfigureAwait(false);
                    Dictionary<string, string> versions = result.Get<Dictionary<string, string>>("versions");
                    if (versions != null)
                    {
                        run = run.WithVersions(versions);
                    }
                }

                run = Keep(run.With(TrainingState.Completed, _clock.UtcNow));
                progress?.Report(new BuildProgress(TrainingState.Completed, 1f));
                log.Information("The voice is built.");
            }
            catch (ScriptFailedException exception)
            {
                TrainingState state = exception.Code == "cancelled" ? TrainingState.Cancelled : TrainingState.Failed;
                run = KeepEnded(run.Ended(state, exception.Code, exception.Message, _clock.UtcNow), log);
                log.Write(state == TrainingState.Failed ? LogLevel.Warning : LogLevel.Information, $"Building the voice ended: {state}", exception);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // Writing a request or run.json failed (a full disk, a locked file): the run still ends as failed.
                run = KeepEnded(run.Ended(TrainingState.Failed, "io", exception.Message, _clock.UtcNow), log);
                log.Error("Building the voice failed on a file.", exception);
            }

            return run;
        }

        // The build is the caller of the run's CRUD (system design 6.31): a run that cannot be saved ends the
        // build as failed, through the IOException handler above.
        private TrainingRun Keep(TrainingRun run)
        {
            CrudResult<TrainingRun> saved = _runs.Save(run);
            return saved.Ok ? saved.Value : throw new IOException(saved.Detail);
        }

        // The ending is kept if it can be; when even that fails, the run still comes back ended, and the log says so.
        private TrainingRun KeepEnded(TrainingRun run, ILog log)
        {
            CrudResult<TrainingRun> saved = _runs.Save(run);
            if (!saved.Ok)
            {
                log.Error($"The run's ending could not be saved: {saved.Detail}");
            }

            return run;
        }

        /// <summary>Candidates from generate.py's result. Pure.</summary>
        public static IReadOnlyList<VoiceCandidate> Candidates(ConfigDocument result, VoiceDescription description = null)
        {
            string instruction = result.Get<string>("instruction");
            string reference = result.Get<string>("reference");
            var candidates = new List<VoiceCandidate>();
            foreach (CandidateFields item in result.Get("candidates", new List<CandidateFields>()))
            {
                candidates.Add(new VoiceCandidate($"candidate_{item.Seed}", item.Seed, item.File, new PitchSummary(item.MedianHz, item.LowHz, item.HighHz), instruction, reference, description));
            }

            return candidates;
        }

        /// <summary>The whole build's progress from a step and its own progress. Pure.</summary>
        public static float Fraction(int step, int done, int total, BuildPlan plan = null)
        {
            plan ??= BuildPlan.Standard;
            float whole = 0f;
            float before = 0f;
            for (int i = 0; i < s_steps.Length; i++)
            {
                float minutes = Minutes(i, plan);
                whole += minutes;
                if (i < step)
                {
                    before += minutes;
                }
            }

            float within = total > 0 ? Math.Max(0f, Math.Min(1f, done / (float)total)) : 0f;
            return Math.Min(1f, (before + (Minutes(step, plan) * within)) / whole);
        }

        /// <summary>A step's expected minutes: training scales with the epochs, and every step with the speech's lines. Pure.</summary>
        public static float Minutes(int step, BuildPlan plan)
        {
            float minutes = s_steps[step].Minutes;
            if (step == TrainingStep)
            {
                minutes *= Math.Max(1, plan.Epochs) / (float)DefaultEpochs;
            }

            float lines = plan.SpeechLines > 0 ? Math.Min(plan.SpeechLines, BuildPlan.CorpusLines) : BuildPlan.CorpusLines;
            return step == PostProcessingStep ? minutes : minutes * lines / BuildPlan.CorpusLines;
        }

        /// <summary>A preview's file name, one per candidate and timbre, so trying a timbre again plays the same file. Pure.</summary>
        public static string PreviewName(string candidateId, TimbreSettings timbre)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}_p{1:+0.#;-0.#;0}_a{2:+0.#;-0.#;0}_b{3:+0.#;-0.#;0}_r{4:+0.#;-0.#;0}_s{5:+0.#;-0.#;0}.wav",
                candidateId, timbre.Pitch, timbre.Age, timbre.Brightness, timbre.Breathiness, timbre.Softness);
        }

        /// <summary>What run.json keeps of how the voice was made. Pure.</summary>
        public static RunDesign Design(VoiceCandidate candidate, TimbreSettings timbre, BuildPlan plan = null)
        {
            plan ??= BuildPlan.Standard;
            return new RunDesign
            {
                Epochs = plan.Epochs,
                SpeechLines = plan.SpeechLines,
                Instruction = candidate.Instruction,
                Seed = candidate.Seed,
                Candidate = candidate.File,
                Reference = candidate.Reference,
                Timbre = timbre.ToValues(),
                Words = new List<string>(candidate.Description?.Words ?? Array.Empty<string>()),
                FreeWords = candidate.Description?.FreeWords ?? string.Empty,
                Prompt = candidate.Description?.Prompt ?? string.Empty,
                MedianHz = candidate.Pitch.MedianHz,
                LowHz = candidate.Pitch.LowHz,
                HighHz = candidate.Pitch.HighHz,
            };
        }

        /// <summary>The request of the build's step <paramref name="step"/>. Pure.</summary>
        public static ConfigDocument StepRequest(int step, VoiceCandidate candidate, TimbreSettings timbre, string folder, VoiceLabEnvironment environment, BuildPlan plan = null)
        {
            plan ??= BuildPlan.Standard;
            string speech = Path.Combine(folder, "speech");
            string edited = Path.Combine(folder, "speech-edited");
            string weights = Path.Combine(folder, "train");
            ConfigDocument request = ConfigDocument.Create();
            switch (step)
            {
                case 0:
                    request.Set("reference", candidate.File);
                    request.Set("model", environment.VoxCpmModel);
                    request.Set("output", speech);
                    if (plan.SpeechLines > 0)
                    {
                        request.Set("limit", plan.SpeechLines);
                    }

                    break;
                case 1:
                    request.Set("source", speech);
                    timbre.WriteTo(request, "timbre");
                    request.Set("output", edited);
                    break;
                case 2:
                    request.Set("rvc", environment.Rvc);
                    request.Set("data", edited);
                    request.Set("work", weights);
                    request.Set("epochs", plan.Epochs);
                    break;
                case 3:
                    request.Set("weights", Path.Combine(weights, "voice.pth"));
                    request.Set("rvc", environment.Rvc);
                    request.Set("template", environment.Template);
                    request.Set("manifest", environment.Manifest);
                    request.Set("frames", VoiceLabEnvironment.GeneratorFrames);
                    request.Set("returned", VoiceLabEnvironment.GeneratorReturned);
                    request.Set("speech", edited);
                    request.Set("output", Path.Combine(folder, "output"));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(step));
            }

            return request;
        }

        // Passes progress on at once, on the thread it arrives on. Progress<T> would post it to the caller's context,
        // and a caller waiting on that context would see it only at the end.
        private sealed class Forward<T> : IProgress<T>
        {
            private readonly Action<T> _report;

            public Forward(Action<T> report)
            {
                _report = report;
            }

            public void Report(T value)
            {
                _report(value);
            }
        }

        private readonly struct BuildStep
        {
            public BuildStep(TrainingState state, string script, string operation, float minutes)
            {
                State = state;
                Script = script;
                Operation = operation;
                Minutes = minutes;
            }

            public TrainingState State { get; }

            public string Script { get; }

            public string Operation { get; }

            public float Minutes { get; }
        }
    }
}
