using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// A build running, stopped, or failed (screen spec voice-lab 2.1): a state chip, what to do for a failure, the four
    /// steps, the meter with the download's speed, the record of the work, Stop or Continue, and a note.
    /// </summary>
    internal sealed class VoiceLabSetupProgressView
    {
        // The speed is the average over this window, so it does not jump with each chunk.
        private const double RateWindowSeconds = 5d;

        private readonly IVoiceLabSetup _setup;
        private readonly StatusChip _chip;
        private readonly StatusChip _failureTitle;
        private readonly Label _failureBody;
        private readonly KeyedText _failureText;
        private readonly StatusChip[] _stepChips = new StatusChip[VoiceLabSetupText.StepKeys.Length];
        private readonly ProgressMeter _meter;
        private readonly ScrollView _log;
        private readonly KeyedText _note;
        private readonly List<(double Seconds, long Done)> _samples = new List<(double Seconds, long Done)>();
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private int _shownEntries;

        /// <param name="stop">Stops the build that runs.</param>
        /// <param name="resume">Continues a stopped or failed build.</param>
        public VoiceLabSetupProgressView(IVoiceLabSetup setup, Action stop, Action resume)
        {
            _setup = setup;
            Card = Ui.Card(VoiceLabStrings.SetupTitle);
            _chip = Ui.Chip(StatusKind.Idle, VoiceLabStrings.SetupRunning);
            _chip.AddToClassList(VoiceLabScreen.GapClass);
            Card.Add(_chip);
            _failureTitle = Ui.Chip(StatusKind.Error, VoiceLabStrings.SetupFailedSetupTitle);
            Card.Add(_failureTitle);
            _failureBody = VoiceLabSetupView.TextLabel(VoiceLabScreen.NoteClass);
            _failureText = new KeyedText(_failureBody, value => _failureBody.text = value);
            _failureBody.AddToClassList(VoiceLabScreen.GapClass);
            Card.Add(_failureBody);
            for (int i = 0; i < _stepChips.Length; i++)
            {
                var row = new VisualElement();
                row.AddToClassList(VoiceLabSetupView.StepClass);
                _stepChips[i] = Ui.Chip(StatusKind.Idle, VoiceLabStrings.SetupStepLater);
                row.Add(_stepChips[i]);
                row.Add(Ui.Text(VoiceLabSetupText.StepKeys[i], "vv-text-body"));
                Card.Add(row);
            }

            _meter = Ui.Progress(VoiceLabSetupText.StepKeys[0]);
            _meter.AddToClassList(VoiceLabScreen.GapClass);
            Card.Add(_meter);

            // What is being done, line by line in the app's words (the user's request, 2026-11-11).
            _log = new ScrollView(ScrollViewMode.Vertical);
            _log.AddToClassList(VoiceLabSetupView.LogClass);
            _log.AddToClassList(VoiceLabScreen.GapClass);
            Card.Add(_log);
            StopButton = Ui.Button(VoiceLabStrings.Stop);
            StopButton.clicked += stop;
            ResumeButton = Ui.Button(VoiceLabStrings.SetupResume, ButtonVariant.Primary);
            ResumeButton.clicked += resume;
            Card.Add(VoiceLabScreen.Actions(StopButton, ResumeButton));
            Label note = VoiceLabSetupView.TextLabel(VoiceLabScreen.NoteClass);
            _note = new KeyedText(note, value => note.text = value);
            Card.Add(note);
        }

        public Card Card { get; }

        public ActionButton StopButton { get; }

        public ActionButton ResumeButton { get; }

        /// <summary>Shows where the build is; <paramref name="stoppable"/> when this screen started it and can stop it.</summary>
        public void Show(VoiceLabSetupState state, bool stoppable)
        {
            bool running = state == VoiceLabSetupState.Preparing;
            VoiceLabSetupProgress progress = _setup.Progress;
            int current = (int)progress.Step;
            (StatusKind status, string key) = VoiceLabSetupText.StepStatusOf(current, current, state);
            _chip.Status = status;
            _chip.TextKey = key;
            bool failed = state == VoiceLabSetupState.Failed;
            (string title, string body) = VoiceLabSetupText.FailureKeys(_setup.LastFailure);
            _failureTitle.TextKey = title;
            _failureText.Key = body;
            _failureTitle.style.display = VoiceLabSetupView.Show(failed);
            _failureBody.style.display = VoiceLabSetupView.Show(failed);
            for (int i = 0; i < _stepChips.Length; i++)
            {
                (StatusKind stepStatus, string stepKey) = VoiceLabSetupText.StepStatusOf(i, current, state);
                _stepChips[i].Status = stepStatus;
                _stepChips[i].TextKey = stepKey;
            }

            // Only while it runs: a stopped build's last figures would be stale, and after a restart there are none.
            ILocalizer localizer = UiServices.Find(Card)?.Localizer;
            _meter.style.display = VoiceLabSetupView.Show(running);
            _meter.LabelKey = VoiceLabSetupText.StepKeys[current];
            _meter.Progress = progress.Fraction;
            _meter.Detail = VoiceLabSetupText.ProgressDetail(localizer, progress) + DownloadRate(localizer, progress, running);
            ShowEntries();
            StopButton.style.display = VoiceLabSetupView.Show(running);
            StopButton.SetEnabled(stoppable);
            ResumeButton.style.display = VoiceLabSetupView.Show(!running);
            _note.Key = running ? VoiceLabStrings.SetupRunningNote
                : _setup.PausedForLive ? VoiceLabStrings.SetupLiveNote
                : VoiceLabStrings.SetupResumeNote;
        }

        /// <summary>Adds the record's new lines and keeps the newest in view; a language change writes them all again.</summary>
        public void ShowEntries(bool rewrite = false)
        {
            IReadOnlyList<VoiceLabSetupEntry> entries = _setup.Entries;
            if (rewrite || entries.Count < _shownEntries)
            {
                _log.Clear();
                _shownEntries = 0;
            }

            if (entries.Count == _shownEntries)
            {
                return;
            }

            ILocalizer localizer = UiServices.Find(Card)?.Localizer;
            for (int i = _shownEntries; i < entries.Count; i++)
            {
                Label line = QuietText.Of(new Label(VoiceLabSetupText.EntryText(localizer, entries[i])));
                line.AddToClassList("vv-mono");
                _log.Add(line);
            }

            _shownEntries = entries.Count;
            _log.style.display = entries.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            // After layout, so the newest line is measured before it is scrolled to.
            Label newest = (Label)_log[_log.childCount - 1];
            _log.schedule.Execute(() => _log.ScrollTo(newest)).StartingIn(50);
        }

        // Samples the bytes done while downloading; the speed is read from the last few seconds.
        private string DownloadRate(ILocalizer localizer, VoiceLabSetupProgress progress, bool running)
        {
            if (!running || progress.Step != VoiceLabSetupStep.Download || progress.Total <= 0)
            {
                _samples.Clear();
                return string.Empty;
            }

            double now = _clock.Elapsed.TotalSeconds;
            if (_samples.Count > 0 && progress.Done < _samples[_samples.Count - 1].Done)
            {
                _samples.Clear();
            }

            _samples.Add((now, progress.Done));
            _samples.RemoveAll(sample => now - sample.Seconds > RateWindowSeconds);
            (double rate, double? left) = VoiceLabSetupText.Rate(_samples, progress.Total);
            return rate > 0d ? " · " + VoiceLabSetupText.RateText(localizer, rate, left) : string.Empty;
        }
    }
}
