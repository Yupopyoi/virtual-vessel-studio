using System;
using System.Collections.Generic;
using System.IO;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.Voice.Models;
using VirtualVessel.VoiceLab.Training;

namespace VirtualVessel.VoiceLab.Registration
{
    /// <summary>
    /// Hands a built voice from its Training Run over to Voice's registered voices (system design 13.21), so deleting
    /// or retraining in Voice Lab never breaks a voice in use. The run's files stay where they are. How the voice was
    /// made goes with it as design.json, which only Voice Lab reads (Voice Lab detailed design 6.5).
    /// </summary>
    public sealed class VoiceModelRegistrar
    {
        public const string DesignFile = "design.json";
        public const int DesignSchemaVersion = 1;

        private readonly IVoiceModelRepository _voices;
        private readonly ITrainingRunRepository _runs;
        private readonly ISystemClock _clock;

        public VoiceModelRegistrar(IVoiceModelRepository voices, ITrainingRunRepository runs, ISystemClock clock)
        {
            _voices = voices ?? throw new ArgumentNullException(nameof(voices));
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>
        /// Creates the voice (system design 6.31): Invalid when the run did not complete or its output is missing,
        /// StorageFailed when the files could not be written or copied.
        /// </summary>
        public CrudResult<VoiceModelRecord> Register(TrainingRun run, string name)
        {
            if (run.GetState() != TrainingState.Completed)
            {
                return CrudResult<VoiceModelRecord>.Fail(CrudError.Invalid, $"Run {run.RunId} is {run.State}; only a completed run can be registered.");
            }

            string output = Path.Combine(_runs.FolderOf(run.RunId), "output");
            CrudResult written = CrudResult.Run(() => DesignStore(output, _clock).Save(run.Design ?? new RunDesign()), $"Writing run {run.RunId}'s design");
            return written.Ok ? _voices.Add(Handover(run, name, output)) : CrudResult<VoiceModelRecord>.From(written);
        }

        /// <summary>What Voice is given: the runtime's files and the design, from the run's output. Pure.</summary>
        public static NewVoiceModel Handover(TrainingRun run, string name, string output)
        {
            var files = new List<string>(VoiceModelStore.RuntimeFiles) { DesignFile };
            return new NewVoiceModel(VoiceId(run.RunId), name, run.RunId, output, files, run.Versions, run.Design?.Tone?.ToTone() ?? default);
        }

        /// <summary>A voice's ID from the run it came from: one voice per run. Pure.</summary>
        public static string VoiceId(string runId)
        {
            return VoiceModelStore.IdPrefix + runId;
        }

        /// <summary>
        /// How a registered voice was made, from its folder; null when it has no design.json, such as a voice registered
        /// before the design moved out of model.json.
        /// </summary>
        public static RunDesign ReadDesign(string voiceFolder, ISystemClock clock)
        {
            if (!File.Exists(Path.Combine(voiceFolder, DesignFile)))
            {
                return null;
            }

            ConfigLoadResult<RunDesign> loaded = DesignStore(voiceFolder, clock).Load();
            return loaded.Status == ConfigLoadStatus.Recovered ? null : loaded.Value;
        }

        private static ConfigFileStore<RunDesign> DesignStore(string folder, ISystemClock clock)
        {
            return new ConfigFileStore<RunDesign>(folder, DesignFile, DesignSchemaVersion, () => new RunDesign(), clock);
        }
    }
}
