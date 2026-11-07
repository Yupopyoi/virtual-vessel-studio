using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.UI.VoiceLab;
using VirtualVessel.VoiceLab.Registration;
using VirtualVessel.VoiceLab.Training;

namespace VirtualVessel.VoiceLab
{
    /// <summary>
    /// <see cref="IVoiceLabLibrary"/> over the run and voice repositories (Voice Lab detailed design 8.6). It
    /// needs no training environment, so what was made is listed even when Voice Lab is not set up.
    /// </summary>
    public sealed class VoiceLabLibrary : IVoiceLabLibrary
    {
        public const int MaxVoiceName = 60;

        private readonly ITrainingRunRepository _runs;
        private readonly IVoiceModelRepository _voices;
        private readonly Func<bool> _building;
        private readonly ILog _log;

        /// <param name="building">Whether a build is running now, so an unfinished run is not deleted under it.</param>
        public VoiceLabLibrary(ITrainingRunRepository runs, IVoiceModelRepository voices, Func<bool> building, ILog log)
        {
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _voices = voices ?? throw new ArgumentNullException(nameof(voices));
            _building = building ?? (() => false);
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>The library over the Data Root's folders.</summary>
        public static VoiceLabLibrary Create(string voiceLabFolder, string voiceModelsFolder, ISystemClock clock, Func<bool> building, ILog log)
        {
            return new VoiceLabLibrary(new TrainingRunStore(voiceLabFolder, clock), new VoiceModelStore(voiceModelsFolder, clock), building, log);
        }

        public IReadOnlyList<VoiceLabRunRecord> ListRuns()
        {
            IReadOnlyList<VoiceModelRecord> voices = _voices.List();
            return _runs.List().Select(run => ToRecord(run, voices, File.Exists)).ToArray();
        }

        public IReadOnlyList<VoiceLabVoiceRecord> ListVoices()
        {
            return _voices.List().Select(voice => ToRecord(voice, File.Exists)).ToArray();
        }

        public void SetComment(string runId, string comment)
        {
            TrainingRun run = _runs.Get(runId) ?? throw new VoiceLabFailedException($"There is no run {runId}.");
            Guard(() => _runs.Save(run.WithComment(comment)), $"Saving the note of run {runId}");
        }

        public void RenameVoice(string voiceId, string name)
        {
            string clean = CleanName(name) ?? throw new VoiceLabFailedException($"A voice name must be 1 to {MaxVoiceName} characters.");
            Guard(() => _voices.Rename(voiceId, clean), $"Renaming voice {voiceId}");
        }

        public void DeleteRun(string runId)
        {
            TrainingRun run = _runs.Get(runId);
            if (run == null)
            {
                return;
            }

            if (!run.IsFinished() && _building())
            {
                throw new VoiceLabFailedException($"Run {runId} is being made.");
            }

            Guard(() => _runs.Delete(runId), $"Deleting run {runId}");
            _log.Information($"Deleted run {runId}.");
        }

        public void DeleteVoice(string voiceId)
        {
            Guard(() => _voices.Delete(voiceId), $"Deleting voice {voiceId}");
            _log.Information($"Deleted voice {voiceId}.");
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

        private static void Guard(Action work, string what)
        {
            try
            {
                work();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException || exception is ArgumentException)
            {
                throw new VoiceLabFailedException($"{what} failed: {exception.Message}", exception);
            }
        }
    }
}
