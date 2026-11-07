using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.VoiceLab.Training;

namespace VirtualVessel.VoiceLab.Registration
{
    /// <summary>A registered voice, as the runtime will find it in VoiceModels/&lt;id&gt;/.</summary>
    public sealed class RegisteredVoice
    {
        public RegisteredVoice(string id, string name, string folder, int medianPitchHz)
        {
            Id = id;
            Name = name;
            Folder = folder;
            MedianPitchHz = medianPitchHz;
        }

        public string Id { get; }

        public string Name { get; }

        public string Folder { get; }

        /// <summary>For matching the user's pitch (Voice Lab detailed design 8.3.4); 0 when unknown.</summary>
        public int MedianPitchHz { get; }
    }

    /// <summary>model.json: what a registered voice is called and where it came from (Voice Lab detailed design 6.5).</summary>
    public sealed class VoiceModelRecord
    {
        public const int SchemaVersion = 1;

        public string Id { get; set; }

        public string Name { get; set; }

        public string RunId { get; set; }

        public string RegisteredAtUtc { get; set; }

        public int MedianPitchHz { get; set; }

        public RunDesign Design { get; set; } = new RunDesign();

        public Dictionary<string, string> Versions { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Copies a built voice from its Training Run into the runtime's voices (system design 13.21), so deleting
    /// or retraining in Voice Lab never breaks a voice in use. The run's files stay where they are.
    /// </summary>
    public sealed class VoiceModelRegistrar
    {
        public static readonly IReadOnlyList<string> VoiceFiles = new[] { "voice.sentis", "voice.spk", "voice.json" };

        private readonly IVoiceModelRepository _voices;
        private readonly ITrainingRunRepository _runs;
        private readonly ISystemClock _clock;

        public VoiceModelRegistrar(IVoiceModelRepository voices, ITrainingRunRepository runs, ISystemClock clock)
        {
            _voices = voices ?? throw new ArgumentNullException(nameof(voices));
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <exception cref="InvalidOperationException">The run did not complete, or its output is missing.</exception>
        public RegisteredVoice Register(TrainingRun run, string name)
        {
            if (run.GetState() != TrainingState.Completed)
            {
                throw new InvalidOperationException($"Run {run.RunId} is {run.State}; only a completed run can be registered.");
            }

            string output = Path.Combine(_runs.FolderOf(run.RunId), "output");
            foreach (string file in VoiceFiles)
            {
                if (!File.Exists(Path.Combine(output, file)))
                {
                    throw new InvalidOperationException($"Run {run.RunId} has no {file}.");
                }
            }

            string id = VoiceId(run.RunId);
            string folder = _voices.FolderOf(id);
            // Copied into a folder beside the target and then moved, so the runtime never sees half a voice.
            string staging = folder + ".part";
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }

            Directory.CreateDirectory(staging);
            foreach (string file in VoiceFiles)
            {
                File.Copy(Path.Combine(output, file), Path.Combine(staging, file));
            }

            int medianPitch = MedianPitch(File.ReadAllText(Path.Combine(output, "voice.json")));
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            Directory.Move(staging, folder);
            // Recorded once the files are in place; until then the voice is not listed.
            _voices.Add(Record(id, name, run, medianPitch, _clock.UtcNow));
            return new RegisteredVoice(id, name, folder, medianPitch);
        }

        /// <summary>A voice's ID from the run it came from: one voice per run. Pure.</summary>
        public static string VoiceId(string runId)
        {
            return VoiceModelStore.IdPrefix + runId;
        }

        /// <summary>The median pitch export_voice.py wrote in voice.json; 0 when it is not there. Pure.</summary>
        public static int MedianPitch(string voiceJson)
        {
            try
            {
                return ConfigDocument.Parse(voiceJson).Get("medianPitchHz", 0);
            }
            catch (FormatException)
            {
                return 0;
            }
        }

        /// <summary>model.json's contents. Pure.</summary>
        public static VoiceModelRecord Record(string id, string name, TrainingRun run, int medianPitchHz, DateTimeOffset now)
        {
            return new VoiceModelRecord
            {
                Id = id,
                Name = name,
                RunId = run.RunId,
                RegisteredAtUtc = now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                MedianPitchHz = medianPitchHz,
                Design = run.Design,
                Versions = new Dictionary<string, string>(run.Versions ?? new Dictionary<string, string>()),
            };
        }
    }
}
