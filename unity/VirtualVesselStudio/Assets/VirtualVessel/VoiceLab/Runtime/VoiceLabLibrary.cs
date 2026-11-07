using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.VoiceLab;
using VirtualVessel.VoiceLab.Registration;
using VirtualVessel.VoiceLab.Training;

namespace VirtualVessel.VoiceLab
{
    /// <summary>
    /// <see cref="IVoiceLabLibrary"/> over the run and voice repositories (Voice Lab detailed design 8.6). It
    /// needs no training environment, so what was made is listed even when Voice Lab is not set up. Like every
    /// CRUD it only reports failures; the screen handles them (system design 6.31).
    /// </summary>
    public sealed class VoiceLabLibrary : IVoiceLabLibrary
    {
        public const int MaxVoiceName = 60;

        private readonly ITrainingRunRepository _runs;
        private readonly IVoiceModelRepository _voices;
        private readonly Func<bool> _building;

        /// <param name="building">Whether a build is running now, so an unfinished run is not deleted under it.</param>
        public VoiceLabLibrary(ITrainingRunRepository runs, IVoiceModelRepository voices, Func<bool> building)
        {
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _voices = voices ?? throw new ArgumentNullException(nameof(voices));
            _building = building ?? (() => false);
        }

        /// <summary>The library over the Data Root's folders.</summary>
        public static VoiceLabLibrary Create(string voiceLabFolder, string voiceModelsFolder, ISystemClock clock, Func<bool> building)
        {
            return new VoiceLabLibrary(new TrainingRunStore(voiceLabFolder, clock), new VoiceModelStore(voiceModelsFolder, clock), building);
        }

        public CrudResult<IReadOnlyList<VoiceLabRunRecord>> ListRuns()
        {
            CrudResult<IReadOnlyList<TrainingRun>> runs = _runs.List();
            if (!runs.Ok)
            {
                return CrudResult<IReadOnlyList<VoiceLabRunRecord>>.From(runs.WithoutValue);
            }

            CrudResult<IReadOnlyList<VoiceModelRecord>> voices = _voices.List();
            if (!voices.Ok)
            {
                return CrudResult<IReadOnlyList<VoiceLabRunRecord>>.From(voices.WithoutValue);
            }

            return CrudResult<IReadOnlyList<VoiceLabRunRecord>>.Success(runs.Value.Select(run => ToRecord(run, voices.Value, File.Exists)).ToArray());
        }

        public CrudResult<IReadOnlyList<VoiceLabVoiceRecord>> ListVoices()
        {
            CrudResult<IReadOnlyList<VoiceModelRecord>> voices = _voices.List();
            return voices.Ok
                ? CrudResult<IReadOnlyList<VoiceLabVoiceRecord>>.Success(voices.Value.Select(voice => ToRecord(voice, File.Exists)).ToArray())
                : CrudResult<IReadOnlyList<VoiceLabVoiceRecord>>.From(voices.WithoutValue);
        }

        public CrudResult SetComment(string runId, string comment)
        {
            CrudResult<TrainingRun> run = _runs.Get(runId);
            return run.Ok ? _runs.Save(run.Value.WithComment(comment)).WithoutValue : run.WithoutValue;
        }

        public CrudResult RenameVoice(string voiceId, string name)
        {
            string clean = CleanName(name);
            return clean == null
                ? CrudResult.Fail(CrudError.Invalid, $"A voice name must be 1 to {MaxVoiceName} characters.")
                : _voices.Rename(voiceId, clean);
        }

        public CrudResult DeleteRun(string runId)
        {
            CrudResult<TrainingRun> run = _runs.Get(runId);
            if (run.Error == CrudError.NotFound)
            {
                // Gone either way.
                return CrudResult.Success;
            }

            if (!run.Ok)
            {
                return run.WithoutValue;
            }

            if (!run.Value.IsFinished() && _building())
            {
                return CrudResult.Fail(CrudError.Busy, $"Run {runId} is being made.");
            }

            return _runs.Delete(runId);
        }

        public CrudResult DeleteVoice(string voiceId)
        {
            return _voices.Delete(voiceId);
        }

        /// <summary>A voice name as kept: trimmed, or null when empty or longer than <see cref="MaxVoiceName"/>. Pure.</summary>
        public static string CleanName(string name)
        {
            string trimmed = name?.Trim();
            return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxVoiceName ? null : trimmed;
        }

        /// <summary>A run as the tab shows it, with the voices registered from it. Pure but for <paramref name="exists"/>.</summary>
        public static VoiceLabRunRecord ToRecord(TrainingRun run, IReadOnlyList<VoiceModelRecord> voices, Func<string, bool> exists)
        {
            RunDesign design = run.Design ?? new RunDesign();
            return new VoiceLabRunRecord(
                run.RunId,
                run.CreatedAt(),
                VoiceLabService.ToScreen(run.GetState()),
                design.Words,
                design.FreeWords,
                VoiceLabService.ToScreen(design.Timbre),
                run.Comment,
                ExistingFile(design.Candidate, exists),
                voices.Where(v => v.RunId == run.RunId).Select(v => v.Name).ToArray());
        }

        /// <summary>A voice as the tab shows it. Pure but for <paramref name="exists"/>.</summary>
        public static VoiceLabVoiceRecord ToRecord(VoiceModelRecord voice, Func<string, bool> exists)
        {
            DateTimeOffset.TryParse(voice.RegisteredAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at);
            return new VoiceLabVoiceRecord(voice.Id, voice.Name, voice.RunId, at, voice.MedianPitchHz, ExistingFile(voice.Design?.Candidate, exists));
        }

        private static string ExistingFile(string path, Func<string, bool> exists)
        {
            return !string.IsNullOrEmpty(path) && exists(path) ? path : null;
        }
    }
}
