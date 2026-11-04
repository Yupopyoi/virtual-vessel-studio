using System;
using System.Collections.Generic;
using System.Globalization;

namespace VirtualVessel.VoiceLab.Training
{
    /// <summary>A Training Run's state, the same for every engine (system design 13.15).</summary>
    public enum TrainingState
    {
        Pending,
        Preparing,
        Preprocessing,
        Training,
        PostProcessing,
        Completed,
        Failed,
        Cancelled,
    }

    /// <summary>The timbre steps a voice was built with, as run.json keeps them.</summary>
    public sealed class TimbreValues
    {
        public float Pitch { get; set; }

        public float Age { get; set; }

        public float Brightness { get; set; }

        public float Breathiness { get; set; }
    }

    /// <summary>What a voice was made from, so it can be made again (Voice Lab detailed design 8.3.3).</summary>
    public sealed class RunDesign
    {
        public string Instruction { get; set; }

        public int Seed { get; set; }

        /// <summary>The candidate's audio, the reference the training speech copies.</summary>
        public string Candidate { get; set; }

        public TimbreValues Timbre { get; set; } = new TimbreValues();
    }

    /// <summary>
    /// One Training Run as run.json keeps it (Voice Lab detailed design 6.3, system design 13.14), stored with
    /// ProjectData's settings files. Failed and cancelled runs stay (system design 13.22). The changing methods
    /// return a new run and leave this one as it is.
    /// </summary>
    public sealed class TrainingRun
    {
        public const int SchemaVersion = 1;

        public string RunId { get; set; }

        /// <summary>A <see cref="TrainingState"/> name, so run.json reads as words.</summary>
        public string State { get; set; } = nameof(TrainingState.Pending);

        /// <summary>ISO 8601, UTC.</summary>
        public string CreatedAtUtc { get; set; }

        public string UpdatedAtUtc { get; set; }

        public RunDesign Design { get; set; } = new RunDesign();

        /// <summary>The failing script's error code, or "cancelled"; null otherwise.</summary>
        public string ErrorCode { get; set; }

        /// <summary>For developers: the failing script's message.</summary>
        public string Error { get; set; }

        /// <summary>The versions the run depended on (torch, voxcpm, numpy), as the scripts reported them.</summary>
        public Dictionary<string, string> Versions { get; set; } = new Dictionary<string, string>();

        /// <summary>The state; an unknown name, from a newer version, counts as failed. Pure.</summary>
        public TrainingState GetState()
        {
            return Enum.TryParse(State, out TrainingState state) ? state : TrainingState.Failed;
        }

        public bool IsFinished()
        {
            TrainingState state = GetState();
            return state == TrainingState.Completed || state == TrainingState.Failed || state == TrainingState.Cancelled;
        }

        public DateTimeOffset CreatedAt()
        {
            return DateTimeOffset.TryParse(CreatedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at) ? at : DateTimeOffset.MinValue;
        }

        public static TrainingRun Start(string runId, RunDesign design, DateTimeOffset now)
        {
            string at = Time(now);
            return new TrainingRun { RunId = runId, CreatedAtUtc = at, UpdatedAtUtc = at, Design = design ?? new RunDesign() };
        }

        /// <summary>The run moved on to <paramref name="state"/>. Pure.</summary>
        public TrainingRun With(TrainingState state, DateTimeOffset now)
        {
            TrainingRun copy = Copy();
            copy.State = state.ToString();
            copy.UpdatedAtUtc = Time(now);
            return copy;
        }

        /// <summary>The run ended unsuccessfully. Pure.</summary>
        public TrainingRun Ended(TrainingState state, string errorCode, string error, DateTimeOffset now)
        {
            TrainingRun copy = With(state, now);
            copy.ErrorCode = errorCode;
            copy.Error = error;
            return copy;
        }

        /// <summary>The run with the versions it depended on. Pure.</summary>
        public TrainingRun WithVersions(IReadOnlyDictionary<string, string> versions)
        {
            TrainingRun copy = Copy();
            copy.Versions = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> version in versions)
            {
                copy.Versions[version.Key] = version.Value;
            }

            return copy;
        }

        /// <summary>
        /// A run ID from the time it started and a random part, sortable by time and readable in a folder list. Pure.
        /// </summary>
        public static string NewId(DateTimeOffset now, int random)
        {
            return $"{now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{random & 0xFFFF:x4}";
        }

        private static string Time(DateTimeOffset now)
        {
            return now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        }

        private TrainingRun Copy()
        {
            return new TrainingRun
            {
                RunId = RunId,
                State = State,
                CreatedAtUtc = CreatedAtUtc,
                UpdatedAtUtc = UpdatedAtUtc,
                Design = new RunDesign
                {
                    Instruction = Design?.Instruction,
                    Seed = Design?.Seed ?? 0,
                    Candidate = Design?.Candidate,
                    Timbre = new TimbreValues
                    {
                        Pitch = Design?.Timbre?.Pitch ?? 0f,
                        Age = Design?.Timbre?.Age ?? 0f,
                        Brightness = Design?.Timbre?.Brightness ?? 0f,
                        Breathiness = Design?.Timbre?.Breathiness ?? 0f,
                    },
                },
                ErrorCode = ErrorCode,
                Error = Error,
                Versions = new Dictionary<string, string>(Versions ?? new Dictionary<string, string>()),
            };
        }
    }
}
