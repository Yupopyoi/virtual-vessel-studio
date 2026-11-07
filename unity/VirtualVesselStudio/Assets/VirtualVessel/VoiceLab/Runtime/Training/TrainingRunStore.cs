using System;
using System.Collections.Generic;
using System.IO;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.ProjectData.Records;

namespace VirtualVessel.VoiceLab.Training
{
    /// <summary>
    /// Training Runs as folders under VoiceLab/TrainingRuns, each with its run.json (system design 13.14), kept
    /// with ProjectData's settings files: written whole or not at all, fields from newer versions kept, and a
    /// damaged file moved aside. Failures are reported as results (system design 6.31).
    /// </summary>
    public sealed class TrainingRunStore : ITrainingRunRepository
    {
        public const string FolderName = "TrainingRuns";
        public const string FileName = "run.json";

        private readonly string _root;
        private readonly ISystemClock _clock;
        private readonly Random _random;

        public TrainingRunStore(string voiceLabFolder, ISystemClock clock, Random random = null)
        {
            _root = Path.Combine(voiceLabFolder ?? throw new ArgumentNullException(nameof(voiceLabFolder)), FolderName);
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _random = random ?? new Random();
        }

        public string FolderOf(string runId)
        {
            if (!IsPlainName(runId))
            {
                throw new ArgumentException($"'{runId}' is not a run ID.", nameof(runId));
            }

            return Path.Combine(_root, runId);
        }

        public CrudResult<TrainingRun> Create(RunDesign design)
        {
            return CrudResult<TrainingRun>.Run(() =>
            {
                DateTimeOffset now = _clock.UtcNow;
                string id = TrainingRun.NewId(now, _random.Next());
                while (Directory.Exists(FolderOf(id)))
                {
                    id = TrainingRun.NewId(now, _random.Next());
                }

                Directory.CreateDirectory(FolderOf(id));
                TrainingRun run = TrainingRun.Start(id, design, now);
                Store(id).Save(run);
                return run;
            }, "Creating a run");
        }

        public CrudResult<TrainingRun> Save(TrainingRun run)
        {
            if (!IsPlainName(run?.RunId))
            {
                return CrudResult<TrainingRun>.Fail(CrudError.Invalid, $"'{run?.RunId}' is not a run ID.");
            }

            return CrudResult<TrainingRun>.Run(() =>
            {
                Store(run.RunId).Save(run);
                return run;
            }, $"Saving run {run.RunId}");
        }

        public CrudResult<TrainingRun> Get(string runId)
        {
            if (!IsPlainName(runId))
            {
                return CrudResult<TrainingRun>.Fail(CrudError.Invalid, $"'{runId}' is not a run ID.");
            }

            CrudResult<TrainingRun> read = CrudResult<TrainingRun>.Run(() => Load(runId), $"Reading run {runId}");
            return read.Ok && read.Value == null ? CrudResult<TrainingRun>.Fail(CrudError.NotFound, $"There is no run {runId}.") : read;
        }

        /// <summary>Every run that can be read, newest first. A folder without a usable run.json is skipped.</summary>
        public CrudResult<IReadOnlyList<TrainingRun>> List()
        {
            return CrudResult<IReadOnlyList<TrainingRun>>.Run(() =>
            {
                var runs = new List<TrainingRun>();
                if (!Directory.Exists(_root))
                {
                    return runs;
                }

                foreach (string folder in Directory.GetDirectories(_root))
                {
                    string runId = Path.GetFileName(folder);
                    TrainingRun run = IsPlainName(runId) ? Load(runId) : null;
                    if (run != null)
                    {
                        runs.Add(run);
                    }
                }

                runs.Sort((a, b) => b.CreatedAt().CompareTo(a.CreatedAt()));
                return runs;
            }, "Listing runs");
        }

        public CrudResult Delete(string runId)
        {
            if (!IsPlainName(runId))
            {
                return CrudResult.Fail(CrudError.Invalid, $"'{runId}' is not a run ID.");
            }

            return CrudResult.Run(() =>
            {
                if (Directory.Exists(FolderOf(runId)))
                {
                    Directory.Delete(FolderOf(runId), true);
                }
            }, $"Deleting run {runId}");
        }

        /// <summary>A folder name with no path in it, so a run ID never reaches outside the runs' folder. Pure.</summary>
        public static bool IsPlainName(string name)
        {
            return !string.IsNullOrEmpty(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name != "." && name != "..";
        }

        // The run, or null when there is none or its file could not be used.
        private TrainingRun Load(string runId)
        {
            if (!File.Exists(Path.Combine(FolderOf(runId), FileName)))
            {
                return null;
            }

            ConfigLoadResult<TrainingRun> loaded = Store(runId).Load();
            return loaded.Status != ConfigLoadStatus.Recovered && !string.IsNullOrEmpty(loaded.Value.RunId) ? loaded.Value : null;
        }

        private ConfigFileStore<TrainingRun> Store(string runId)
        {
            return new ConfigFileStore<TrainingRun>(FolderOf(runId), FileName, TrainingRun.SchemaVersion, () => new TrainingRun(), _clock);
        }
    }
}
