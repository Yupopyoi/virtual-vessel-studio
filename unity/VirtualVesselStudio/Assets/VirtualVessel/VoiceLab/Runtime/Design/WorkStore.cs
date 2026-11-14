using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.VoiceLab.Training;

namespace VirtualVessel.VoiceLab.Design
{
    /// <summary>
    /// VoiceLab/work.json (Voice Lab detailed design 6.6): STEP 1 and STEP 2 left unfinished. Read and written with
    /// ProjectData's settings files (ConfigFileStore), which keep fields a newer version added and move a damaged
    /// file aside.
    /// </summary>
    public sealed class WorkFile
    {
        public DateTimeOffset SavedAtUtc { get; set; }

        /// <summary>The step that was open: 0 for STEP 1, 1 for STEP 2.</summary>
        public int Step { get; set; }

        public List<string> Words { get; set; } = new List<string>();

        public string FreeWords { get; set; } = string.Empty;

        public string Prompt { get; set; } = string.Empty;

        /// <summary>The line the candidates read; empty for the shelf's.</summary>
        public string Line { get; set; } = string.Empty;

        /// <summary>The seed the next candidates start at.</summary>
        public int FirstSeed { get; set; }

        public List<WorkCandidate> Candidates { get; set; } = new List<WorkCandidate>();

        /// <summary>The number of the candidate picked; 0 for none.</summary>
        public int Picked { get; set; }

        public TimbreValues Timbre { get; set; } = new TimbreValues();

        public List<TimbreValues> Held { get; set; } = new List<TimbreValues>();
    }

    /// <summary>One candidate of the unfinished work: what is needed to play it, build it, and show it again.</summary>
    public sealed class WorkCandidate
    {
        public int Number { get; set; }

        /// <summary>The number of the candidate it was made near; 0 for none.</summary>
        public int From { get; set; }

        public int Seed { get; set; }

        public string File { get; set; }

        public int MedianHz { get; set; }

        public int LowHz { get; set; }

        public int HighHz { get; set; }

        public string Instruction { get; set; } = string.Empty;

        /// <summary>The audio it was made near, or null.</summary>
        public string Reference { get; set; }

        public List<string> Words { get; set; } = new List<string>();

        public string FreeWords { get; set; } = string.Empty;

        public string Prompt { get; set; } = string.Empty;

        public string Line { get; set; } = string.Empty;
    }

    /// <summary>Reads, keeps, and deletes <see cref="WorkFile"/>; every operation reports failures as a CRUD result (system design 6.31).</summary>
    public sealed class WorkStore
    {
        public const int SchemaVersion = 1;
        public const string FileName = "work.json";

        private readonly ConfigFileStore<WorkFile> _store;

        public WorkStore(string voiceLabFolder, ISystemClock clock)
        {
            _store = new ConfigFileStore<WorkFile>(voiceLabFolder, FileName, SchemaVersion, () => new WorkFile(), clock);
        }

        public string FilePath => _store.FilePath;

        /// <summary>NotFound when there is no unfinished work, Invalid when the file was damaged and moved aside.</summary>
        public CrudResult<WorkFile> Load()
        {
            try
            {
                if (!File.Exists(_store.FilePath))
                {
                    return CrudResult<WorkFile>.Fail(CrudError.NotFound, "No unfinished work is kept.");
                }

                ConfigLoadResult<WorkFile> loaded = _store.Load();
                return loaded.MovedAsidePath != null
                    ? CrudResult<WorkFile>.Fail(CrudError.Invalid, $"The unfinished work could not be read and was moved to {loaded.MovedAsidePath}.")
                    : CrudResult<WorkFile>.Success(loaded.Value);
            }
            catch (Exception exception) when (CrudResult.IsStorage(exception))
            {
                return CrudResult<WorkFile>.Fail(CrudError.StorageFailed, "Reading the unfinished work failed: " + exception.Message);
            }
        }

        public CrudResult Save(WorkFile work)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            return CrudResult.Run(() => _store.Save(work), "Keeping the unfinished work failed");
        }

        /// <summary>Succeeds when there was nothing to delete too.</summary>
        public CrudResult Delete()
        {
            return CrudResult.Run(() => File.Delete(_store.FilePath), "Deleting the unfinished work failed");
        }

        /// <summary>
        /// The audio files the kept work needs, so deleting old candidates leaves them; none when nothing can be read.
        /// Pure but for reading the file.
        /// </summary>
        public static IReadOnlyList<string> KeptFiles(string voiceLabFolder, ISystemClock clock)
        {
            CrudResult<WorkFile> work = new WorkStore(voiceLabFolder, clock).Load();
            return work.Ok ? Files(work.Value) : Array.Empty<string>();
        }

        /// <summary>The candidates' audio and the audio they were made near. Pure.</summary>
        public static IReadOnlyList<string> Files(WorkFile work)
        {
            return work.Candidates.SelectMany(c => new[] { c.File, c.Reference }).Where(f => !string.IsNullOrEmpty(f)).ToArray();
        }

        /// <summary>A candidate as the work keeps it. Pure.</summary>
        public static WorkCandidate ToFile(int number, int from, VoiceCandidate candidate)
        {
            return new WorkCandidate
            {
                Number = number,
                From = from,
                Seed = candidate.Seed,
                File = candidate.File,
                MedianHz = candidate.Pitch.MedianHz,
                LowHz = candidate.Pitch.LowHz,
                HighHz = candidate.Pitch.HighHz,
                Instruction = candidate.Instruction ?? string.Empty,
                Reference = candidate.Reference,
                Words = new List<string>(candidate.Description?.Words ?? Array.Empty<string>()),
                FreeWords = candidate.Description?.FreeWords ?? string.Empty,
                Prompt = candidate.Description?.Prompt ?? string.Empty,
                Line = candidate.Description?.Line ?? string.Empty,
            };
        }

        /// <summary>
        /// A kept candidate made a candidate of this session again. Its id is new, so it never meets one made in this
        /// session. Pure.
        /// </summary>
        public static VoiceCandidate FromFile(WorkCandidate kept)
        {
            return new VoiceCandidate(
                $"work_{kept.Number}_{kept.Seed}",
                kept.Seed,
                kept.File,
                new PitchSummary(kept.MedianHz, kept.LowHz, kept.HighHz),
                kept.Instruction,
                kept.Reference,
                new VoiceDescription(kept.Words, kept.FreeWords, kept.Prompt, kept.Line));
        }
    }
}
