using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ExternalServices.Process;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.VoiceLab;
using VirtualVessel.VoiceLab.Design;
using VirtualVessel.VoiceLab.Registration;
using VirtualVessel.VoiceLab.Scripts;
using VirtualVessel.VoiceLab.Training;

namespace VirtualVessel.VoiceLab
{
    /// <summary>
    /// <see cref="IVoiceLab"/> for the Voice Lab screen (Voice Lab detailed design 4, 5): the designer, the runs,
    /// and registration behind one face. Without a training environment it says so and does nothing.
    /// </summary>
    public sealed class VoiceLabService : IVoiceLab
    {
        /// <summary>The developer's environment file in the settings folder, until activation writes it (Voice Lab detailed design 6.4).</summary>
        public const string EnvironmentFile = "voice-lab-environment.json";

        private readonly object _gate = new object();
        private readonly VoiceDesigner _designer;
        private readonly ITrainingRunRepository _runs;
        private readonly IVoiceModelRepository _voices;
        private readonly VoiceModelRegistrar _registrar;
        private readonly ILog _log;
        private readonly string _samplesFolder;
        private readonly Dictionary<string, VoiceCandidate> _candidates = new Dictionary<string, VoiceCandidate>();
        // Written by the scripts' reading threads, read by the screen on the main thread.
        private readonly Shared<VoiceLabBuildProgress> _progress = new Shared<VoiceLabBuildProgress>();
        private readonly Shared<VoiceLabMaking> _making = new Shared<VoiceLabMaking>();
        private readonly Shared<VoiceLabMaking> _samplesMaking = new Shared<VoiceLabMaking>();

        private VoiceLabService(VoiceDesigner designer, ITrainingRunRepository runs, IVoiceModelRepository voices, VoiceModelRegistrar registrar, VoiceLabShelf shelf, string samplesFolder, ILog log, IVoiceLabLibrary library)
        {
            Library = library;
            _designer = designer;
            _runs = runs;
            _voices = voices;
            _registrar = registrar;
            Shelf = shelf;
            _samplesFolder = samplesFolder;
            _log = log;
        }

        /// <summary>The languages samples are made in. Only Japanese for now; the files are kept per language so others can follow.</summary>
        public static IReadOnlyList<string> SampleLanguages { get; } = new[] { "ja" };

        public VoiceLabAvailability Availability => _designer != null ? VoiceLabAvailability.Ready : VoiceLabAvailability.NotSetUp;

        public VoiceLabShelf Shelf { get; }

        /// <summary>What was made; there even when Voice Lab is not set up, so it can still be listed.</summary>
        public IVoiceLabLibrary Library { get; }

        public VoiceLabMaking SamplesProgress => _samplesMaking.Value;

        public VoiceLabMaking CandidatesProgress => _making.Value;

        public VoiceLabBuildProgress BuildProgress => _progress.Value;

        /// <summary>
        /// The service from the settings folder's environment file; one that is missing, unreadable, or points at
        /// what is not there leaves Voice Lab not set up, with the reason in the log.
        /// </summary>
        public static VoiceLabService Create(string configDirectory, string voiceLabFolder, string voiceModelsFolder, ISystemClock clock, ILog log, Random random = null)
        {
            // The library asks the service whether a build runs, so an unfinished run is not deleted under it.
            VoiceLabService created = null;
            var library = VoiceLabLibrary.Create(voiceLabFolder, voiceModelsFolder, clock, () => created != null && created.BuildProgress.State != VoiceLabBuildState.None);
            string path = Path.Combine(configDirectory, EnvironmentFile);
            if (!File.Exists(path))
            {
                log.Information("Voice Lab is not set up: no training environment.");
                return created = new VoiceLabService(null, null, null, null, null, null, log, library);
            }

            VoiceLabEnvironment environment = VoiceLabEnvironment.Parse(File.ReadAllText(path), out string problem);
            IReadOnlyList<string> problems = environment?.Problems(File.Exists, Directory.Exists) ?? new[] { problem };
            VoiceLabShelf shelf = null;
            if (problems.Count == 0)
            {
                string shelfPath = Path.Combine(environment.Scripts, "voicelab", "shelf.json");
                string shelfProblem = "The scripts have no voicelab/shelf.json.";
                shelf = File.Exists(shelfPath) ? ShelfFile.Parse(File.ReadAllText(shelfPath), out shelfProblem) : null;
                problems = shelf == null ? new[] { shelfProblem } : problems;
            }

            if (problems.Count > 0)
            {
                log.Warning($"Voice Lab is not set up: {string.Join("; ", problems)}");
                return created = new VoiceLabService(null, null, null, null, null, null, log, library);
            }

            var runner = new ExternalProcessRunner(environment.AllowedFolders, log.WithContext("component", "process"), clock);
            var runs = new TrainingRunStore(voiceLabFolder, clock, random);
            var designer = new VoiceDesigner(new PythonScripts(environment, runner), runs, Path.Combine(voiceLabFolder, "Designs"), clock, log, (random ?? new Random()).Next(1, 1_000_000));
            log.Information($"Voice Lab is ready with the environment in {EnvironmentFolderName(environment)}.");
            var voices = new VoiceModelStore(voiceModelsFolder, clock);
            return created = new VoiceLabService(designer, runs, voices, new VoiceModelRegistrar(voices, runs, clock), shelf, Path.Combine(voiceLabFolder, "Samples"), log, library);
        }

        public string SampleFile(string language, string name)
        {
            if (_samplesFolder == null || !IsPlainName(language) || !IsPlainName(name))
            {
                return null;
            }

            string path = Path.Combine(_samplesFolder, language, name);
            return File.Exists(path) ? path : null;
        }

        public async Task MakeSamplesAsync(CancellationToken cancellationToken)
        {
            _samplesMaking.Value = default;
            var progress = new Forward<ScriptProgress>(p => _samplesMaking.Value = ToMaking(p, "samples"));
            try
            {
                await Run(() => _designer.MakeSamplesAsync(SampleLanguages, _samplesFolder, progress, cancellationToken)).ConfigureAwait(false);
                _log.Information("Made Voice Lab's samples.");
            }
            finally
            {
                _samplesMaking.Value = default;
            }
        }

        /// <summary>A file or folder name with no path in it, so a sample is only ever read from the samples folder. Pure.</summary>
        public static bool IsPlainName(string name)
        {
            return !string.IsNullOrEmpty(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name != "." && name != "..";
        }

        public async Task<IReadOnlyList<VoiceLabCandidate>> MakeCandidatesAsync(VoiceLabRequest request, int count, VoiceLabCandidate near, CancellationToken cancellationToken)
        {
            VoiceCandidate reference = near != null ? Find(near) : null;
            _making.Value = default;
            var progress = new Forward<ScriptProgress>(p => _making.Value = ToMaking(p, "candidates"));
            IReadOnlyList<VoiceCandidate> made;
            try
            {
                made = await Run(() => _designer.MakeCandidatesAsync(new VoiceDescription(request.Words, request.FreeWords), count, reference, progress, cancellationToken)).ConfigureAwait(false);
            }
            finally
            {
                _making.Value = default;
            }

            lock (_gate)
            {
                foreach (VoiceCandidate candidate in made)
                {
                    _candidates[candidate.Id] = candidate;
                }
            }

            return made.Select(ToScreen).ToArray();
        }

        public async Task<VoiceLabPreview> PreviewAsync(VoiceLabCandidate candidate, VoiceLabTimbre timbre, CancellationToken cancellationToken)
        {
            TimbrePreview preview = await Run(() => _designer.PreviewTimbreAsync(Find(candidate), ToSettings(timbre), cancellationToken)).ConfigureAwait(false);
            return new VoiceLabPreview(preview.File, preview.Pitch.MedianHz);
        }

        public async Task<VoiceLabBuild> BuildAsync(VoiceLabCandidate candidate, VoiceLabTimbre timbre, CancellationToken cancellationToken)
        {
            Ensure();
            _progress.Value = new VoiceLabBuildProgress(VoiceLabBuildState.Preparing, 0f);
            var progress = new Forward<BuildProgress>(p => _progress.Value = new VoiceLabBuildProgress(ToScreen(p.State), p.Fraction));
            try
            {
                TrainingRun run = await _designer.BuildVoiceAsync(Find(candidate), ToSettings(timbre), progress, cancellationToken).ConfigureAwait(false);
                return new VoiceLabBuild(run.RunId, ToScreen(run.GetState()), run.ErrorCode == "out_of_memory");
            }
            finally
            {
                // Also when the build threw, so the screen never shows a build that is not running.
                _progress.Value = default;
            }
        }

        public CrudResult<VoiceLabRemake> Remake(string runId)
        {
            if (_designer == null)
            {
                return CrudResult<VoiceLabRemake>.Fail(CrudError.Invalid, "Voice Lab is not set up.");
            }

            CrudResult<TrainingRun> read = _runs.Get(runId);
            if (!read.Ok)
            {
                return CrudResult<VoiceLabRemake>.From(read.WithoutValue);
            }

            TrainingRun run = read.Value;
            VoiceCandidate candidate = FromRun(run.Design ?? new RunDesign(), File.Exists);
            if (candidate != null)
            {
                lock (_gate)
                {
                    _candidates[candidate.Id] = candidate;
                }
            }

            RunDesign design = run.Design ?? new RunDesign();
            return CrudResult<VoiceLabRemake>.Success(new VoiceLabRemake(design.Words, design.FreeWords, candidate == null ? null : ToScreen(candidate), ToScreen(design.Timbre)));
        }

        /// <summary>
        /// The run's base as a candidate again, with the words it was asked with; null when its audio is gone.
        /// Pure but for <paramref name="exists"/>.
        /// </summary>
        public static VoiceCandidate FromRun(RunDesign design, Func<string, bool> exists)
        {
            if (string.IsNullOrEmpty(design.Candidate) || !exists(design.Candidate))
            {
                return null;
            }

            return new VoiceCandidate(
                $"candidate_{design.Seed}",
                design.Seed,
                design.Candidate,
                new PitchSummary(design.MedianHz, design.LowHz, design.HighHz),
                design.Instruction,
                design.Reference,
                new VoiceDescription(design.Words, design.FreeWords));
        }

        public CrudResult Register(string runId, string name)
        {
            if (_designer == null)
            {
                return CrudResult.Fail(CrudError.Invalid, "Voice Lab is not set up.");
            }

            CrudResult<TrainingRun> run = _runs.Get(runId);
            return run.Ok ? _registrar.Register(run.Value, name).WithoutValue : run.WithoutValue;
        }

        /// <summary>A script's progress as a making bar shows it: any stage but <paramref name="stage"/> (loading the model) has no total. Pure.</summary>
        public static VoiceLabMaking ToMaking(ScriptProgress progress, string stage)
        {
            return progress.Stage == stage ? new VoiceLabMaking(progress.Done, progress.Total) : default;
        }

        /// <summary>A training state as the screen shows it. Pure.</summary>
        public static VoiceLabBuildState ToScreen(TrainingState state)
        {
            switch (state)
            {
                case TrainingState.Preparing:
                    return VoiceLabBuildState.Preparing;
                case TrainingState.Preprocessing:
                    return VoiceLabBuildState.Preprocessing;
                case TrainingState.Training:
                    return VoiceLabBuildState.Training;
                case TrainingState.PostProcessing:
                    return VoiceLabBuildState.PostProcessing;
                case TrainingState.Completed:
                    return VoiceLabBuildState.Completed;
                case TrainingState.Cancelled:
                    return VoiceLabBuildState.Cancelled;
                case TrainingState.Pending:
                    return VoiceLabBuildState.Preparing;
                default:
                    return VoiceLabBuildState.Failed;
            }
        }

        /// <summary>A run's timbre as the screen shows it; a missing one is no change. Pure.</summary>
        public static VoiceLabTimbre ToScreen(TimbreValues timbre)
        {
            return timbre == null ? default : new VoiceLabTimbre(timbre.Pitch, timbre.Age, timbre.Brightness, timbre.Breathiness, timbre.Softness);
        }

        /// <summary>The screen's timbre as the designer takes it. Pure.</summary>
        public static TimbreSettings ToSettings(VoiceLabTimbre timbre)
        {
            return new TimbreSettings(timbre.Pitch, timbre.Age, timbre.Brightness, timbre.Breathiness, timbre.Softness);
        }

        /// <summary>A candidate as the screen shows it. Pure.</summary>
        public static VoiceLabCandidate ToScreen(VoiceCandidate candidate)
        {
            return new VoiceLabCandidate(candidate.Id, candidate.File, candidate.Pitch.MedianHz, candidate.Pitch.LowHz, candidate.Pitch.HighHz, candidate.Instruction);
        }

        // The environment's folder name only, so the log does not hold the user's paths.
        private static string EnvironmentFolderName(VoiceLabEnvironment environment)
        {
            return Path.GetFileName(VoiceLabEnvironment.EnvironmentFolder(environment.Python));
        }

        private async Task Run(Func<Task> work)
        {
            await Run(async () =>
            {
                await work().ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }

        private async Task<T> Run<T>(Func<Task<T>> work)
        {
            Ensure();
            try
            {
                return await work().ConfigureAwait(false);
            }
            catch (ScriptFailedException exception)
            {
                throw ToScreen(exception);
            }
        }

        /// <summary>
        /// What the screen is told of a script that did not finish. A stopped script is a cancel, not a failure:
        /// the screen stops a preview when another is asked for, and must not show it as failed. Pure.
        /// </summary>
        public static Exception ToScreen(ScriptFailedException exception)
        {
            return exception.Code == "cancelled"
                ? new OperationCanceledException(exception.Message, exception)
                : new VoiceLabFailedException(exception.Message, exception);
        }

        private VoiceCandidate Find(VoiceLabCandidate candidate)
        {
            lock (_gate)
            {
                if (candidate != null && _candidates.TryGetValue(candidate.Id, out VoiceCandidate found))
                {
                    return found;
                }
            }

            throw new VoiceLabFailedException($"Candidate {candidate?.Id} was not made in this session.");
        }

        private void Ensure()
        {
            if (_designer == null)
            {
                throw new VoiceLabFailedException("Voice Lab is not set up.");
            }
        }

        /// <summary>A value one thread writes and another reads.</summary>
        private sealed class Shared<T>
        {
            private readonly object _gate = new object();
            private T _value;

            public T Value
            {
                get
                {
                    lock (_gate)
                    {
                        return _value;
                    }
                }

                set
                {
                    lock (_gate)
                    {
                        _value = value;
                    }
                }
            }
        }

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
    }
}
