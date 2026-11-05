using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ExternalServices.Process;
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
        private readonly TrainingRunStore _runs;
        private readonly VoiceModelRegistrar _registrar;
        private readonly ILog _log;
        private readonly Dictionary<string, VoiceCandidate> _candidates = new Dictionary<string, VoiceCandidate>();
        private VoiceLabBuildProgress _progress;
        private VoiceLabMaking _making;

        private VoiceLabService(VoiceDesigner designer, TrainingRunStore runs, VoiceModelRegistrar registrar, ILog log)
        {
            _designer = designer;
            _runs = runs;
            _registrar = registrar;
            _log = log;
        }

        public VoiceLabAvailability Availability => _designer != null ? VoiceLabAvailability.Ready : VoiceLabAvailability.NotSetUp;

        public VoiceLabMaking CandidatesProgress
        {
            get
            {
                lock (_gate)
                {
                    return _making;
                }
            }
        }

        public VoiceLabBuildProgress BuildProgress
        {
            get
            {
                lock (_gate)
                {
                    return _progress;
                }
            }
        }

        /// <summary>
        /// The service from the settings folder's environment file; one that is missing, unreadable, or points at
        /// what is not there leaves Voice Lab not set up, with the reason in the log.
        /// </summary>
        public static VoiceLabService Create(string configDirectory, string voiceLabFolder, string voiceModelsFolder, ISystemClock clock, ILog log, Random random = null)
        {
            string path = Path.Combine(configDirectory, EnvironmentFile);
            if (!File.Exists(path))
            {
                log.Information("Voice Lab is not set up: no training environment.");
                return new VoiceLabService(null, null, null, log);
            }

            VoiceLabEnvironment environment = VoiceLabEnvironment.Parse(File.ReadAllText(path), out string problem);
            IReadOnlyList<string> problems = environment?.Problems(File.Exists, Directory.Exists) ?? new[] { problem };
            if (problems.Count > 0)
            {
                log.Warning($"Voice Lab is not set up: {string.Join("; ", problems)}");
                return new VoiceLabService(null, null, null, log);
            }

            var runner = new ExternalProcessRunner(environment.AllowedFolders, log.WithContext("component", "process"), clock);
            var runs = new TrainingRunStore(voiceLabFolder, clock, random);
            var designer = new VoiceDesigner(new PythonScripts(environment, runner), runs, Path.Combine(voiceLabFolder, "Designs"), clock, log, (random ?? new Random()).Next(1, 1_000_000));
            log.Information($"Voice Lab is ready with the environment in {EnvironmentFolderName(environment)}.");
            return new VoiceLabService(designer, runs, new VoiceModelRegistrar(voiceModelsFolder, runs, clock), log);
        }

        public async Task<IReadOnlyList<VoiceLabCandidate>> MakeCandidatesAsync(VoiceLabMood mood, int count, VoiceLabCandidate near, CancellationToken cancellationToken)
        {
            VoiceCandidate reference = near != null ? Find(near) : null;
            SetMaking(default);
            var progress = new Forward<ScriptProgress>(p => SetMaking(ToMaking(p)));
            IReadOnlyList<VoiceCandidate> made;
            try
            {
                made = await Run(() => _designer.MakeCandidatesAsync(new VoiceDescription(mood.Sliders, mood.Words), count, reference, progress, cancellationToken)).ConfigureAwait(false);
            }
            finally
            {
                SetMaking(default);
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
            SetProgress(new VoiceLabBuildProgress(VoiceLabBuildState.Preparing, 0f));
            var progress = new Forward<BuildProgress>(p => SetProgress(new VoiceLabBuildProgress(ToScreen(p.State), p.Fraction)));
            try
            {
                TrainingRun run = await _designer.BuildVoiceAsync(Find(candidate), ToSettings(timbre), progress, cancellationToken).ConfigureAwait(false);
                return new VoiceLabBuild(run.RunId, ToScreen(run.GetState()), run.ErrorCode == "out_of_memory");
            }
            finally
            {
                // Also when the build threw, so the screen never shows a build that is not running.
                SetProgress(default);
            }
        }

        public void Register(string runId, string name)
        {
            Ensure();
            TrainingRun run = _runs.List().FirstOrDefault(r => r.RunId == runId) ?? throw new VoiceLabFailedException($"There is no run {runId}.");
            try
            {
                RegisteredVoice voice = _registrar.Register(run, name);
                _log.Information($"Registered voice {voice.Id} from run {runId}.");
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is UnauthorizedAccessException)
            {
                throw new VoiceLabFailedException($"Registering run {runId} failed: {exception.Message}", exception);
            }
        }

        /// <summary>A script's progress as the candidates' bar shows it: loading the model counts as no total. Pure.</summary>
        public static VoiceLabMaking ToMaking(ScriptProgress progress)
        {
            return progress.Stage == "candidates" ? new VoiceLabMaking(progress.Done, progress.Total) : default;
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

        /// <summary>The screen's timbre as the designer takes it. Pure.</summary>
        public static TimbreSettings ToSettings(VoiceLabTimbre timbre)
        {
            return new TimbreSettings(timbre.Pitch, timbre.Age, timbre.Brightness, timbre.Breathiness);
        }

        /// <summary>A candidate as the screen shows it. Pure.</summary>
        public static VoiceLabCandidate ToScreen(VoiceCandidate candidate)
        {
            return new VoiceLabCandidate(candidate.Id, candidate.File, candidate.Pitch.MedianHz, candidate.Pitch.LowHz, candidate.Pitch.HighHz);
        }

        // The environment's folder name only, so the log does not hold the user's paths.
        private static string EnvironmentFolderName(VoiceLabEnvironment environment)
        {
            return Path.GetFileName(VoiceLabEnvironment.EnvironmentFolder(environment.Python));
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
                throw new VoiceLabFailedException(exception.Message, exception);
            }
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

        private void SetProgress(VoiceLabBuildProgress progress)
        {
            lock (_gate)
            {
                _progress = progress;
            }
        }

        private void SetMaking(VoiceLabMaking making)
        {
            lock (_gate)
            {
                _making = making;
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
