using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine.UIElements;
using VirtualVessel.Core.State;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Recording;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of the record control (docs/&lt;lang&gt;/ui/screens/live.md 3).</summary>
    [StringKeys]
    public static class RecordStrings
    {
        public const string Start = "live.record.start";
        public const string Starting = "live.record.starting";
        public const string Finishing = "live.record.finishing";
        public const string Stop = "live.record.stop";
        public const string Busy = "live.record.busy";
        public const string Recording = "live.record.recording";
        public const string Failed = "live.record.failed";
        public const string ConfirmTitle = "live.record.confirm.title";
        public const string ConfirmBody = "live.record.confirm.body";
        public const string ConfirmStop = "live.record.confirm.stop";
        public const string Saved = "live.record.saved";
        public const string OpenFolder = "live.record.open-folder";
        public const string FolderUnavailable = "live.record.problem.folder";
        public const string DiskFull = "live.record.problem.disk";
        public const string EncoderFailed = "live.record.problem.encoder";
        public const string AudioUnavailable = "live.record.problem.audio";
    }

    /// <summary>What the record control shows for a state (recording detailed design 18.1). Pure data.</summary>
    public readonly struct RecordView
    {
        public RecordView(bool showStart, string startKey, bool showStop, StatusKind? chip, string chipKey, string problemKey)
        {
            ShowStart = showStart;
            StartKey = startKey;
            ShowStop = showStop;
            Chip = chip;
            ChipKey = chipKey;
            ProblemKey = problemKey;
        }

        public bool ShowStart { get; }

        /// <summary>"● Record", or what is happening while it cannot be pressed.</summary>
        public string StartKey { get; }

        public bool ShowStop { get; }

        /// <summary>The status chip's look; null hides it.</summary>
        public StatusKind? Chip { get; }

        public string ChipKey { get; }

        /// <summary>What went wrong and what to do; null hides it.</summary>
        public string ProblemKey { get; }
    }

    /// <summary>
    /// Starting and stopping a recording from the live screen, under the stream preview (recording detailed
    /// design 18.1). Stopping is a Danger action behind a confirmation dialog; starting needs none, since a
    /// stray start loses nothing. Neither button answers a second press while starting or stopping.
    /// </summary>
    public sealed class RecordControl : VisualElement
    {
        public const string UssClass = "vv-record";
        public const string ButtonsClass = UssClass + "__buttons";
        public const string ProblemClass = UssClass + "__problem";
        public const string SavedClass = UssClass + "__saved";

        /// <summary>How often the elapsed time is redrawn; a second is the smallest unit shown.</summary>
        public const long ElapsedIntervalMs = 250;

        private readonly IRecordingService _recording;
        private readonly IFileExplorer _explorer;
        private readonly ObservableValue<string> _busy = new ObservableValue<string>(null);
        private readonly KeyedText _problemText;
        private UiServices _services;

        public RecordControl(IRecordingService recording, IFileExplorer explorer)
        {
            _recording = recording ?? throw new ArgumentNullException(nameof(recording));
            _explorer = explorer;
            AddToClassList(UssClass);

            var buttons = new VisualElement();
            buttons.AddToClassList(ButtonsClass);
            Chip = Ui.Chip(StatusKind.Live, RecordStrings.Recording);
            buttons.Add(Chip);
            StartButton = Ui.Button(RecordStrings.Start).Bind(new UiCommand(_recording.Start, _busy));
            buttons.Add(StartButton);
            StopButton = Ui.Button(RecordStrings.Stop, ButtonVariant.Danger).Bind(new DangerCommand(
                new UiCommand(_recording.Stop),
                new ConfirmationRequest(RecordStrings.ConfirmTitle, RecordStrings.ConfirmBody, RecordStrings.ConfirmStop)));
            buttons.Add(StopButton);
            Add(buttons);

            Problem = new Label();
            Problem.AddToClassList(ProblemClass);
            Problem.AddToClassList("vv-text-body");
            _problemText = new KeyedText(Problem, text => Problem.text = text);
            Add(Problem);

            var saved = new VisualElement();
            saved.AddToClassList(SavedClass);
            SavedText = new Label();
            SavedText.AddToClassList("vv-text-body");
            saved.Add(SavedText);
            OpenFolderButton = Ui.Button(RecordStrings.OpenFolder, ButtonVariant.Secondary).Bind(new UiCommand(() => _explorer?.Show(_recording.LastFile)));
            OpenFolderButton.style.display = explorer != null ? DisplayStyle.Flex : DisplayStyle.None;
            saved.Add(OpenFolderButton);
            Saved = saved;
            Add(saved);

            Show();
            PanelLifetime.WhileAttached(this, Attach);
        }

        public StatusChip Chip { get; }

        public ActionButton StartButton { get; }

        public ActionButton StopButton { get; }

        public Label Problem { get; }

        /// <summary>The last finished file and "Open folder"; shown once a recording is finished.</summary>
        public VisualElement Saved { get; }

        public Label SavedText { get; }

        public ActionButton OpenFolderButton { get; }

        /// <summary>What to show in <paramref name="state"/>. Pure.</summary>
        public static RecordView ViewOf(RecordingState state, RecordingProblem problem)
        {
            return state switch
            {
                RecordingState.Starting => new RecordView(true, RecordStrings.Starting, false, null, null, null),
                RecordingState.Recording => new RecordView(false, RecordStrings.Start, true, StatusKind.Live, RecordStrings.Recording, ProblemKey(problem)),
                RecordingState.Stopping => new RecordView(true, RecordStrings.Finishing, false, null, null, null),
                RecordingState.Failed => new RecordView(true, RecordStrings.Start, false, StatusKind.Error, RecordStrings.Failed, ProblemKey(problem)),
                _ => new RecordView(true, RecordStrings.Start, false, null, null, null),
            };
        }

        /// <summary>The message for <paramref name="problem"/>, which says what to do; null for none. Pure.</summary>
        public static string ProblemKey(RecordingProblem problem)
        {
            return problem switch
            {
                RecordingProblem.FolderUnavailable => RecordStrings.FolderUnavailable,
                RecordingProblem.DiskFull => RecordStrings.DiskFull,
                RecordingProblem.EncoderFailed => RecordStrings.EncoderFailed,
                RecordingProblem.AudioUnavailable => RecordStrings.AudioUnavailable,
                _ => null,
            };
        }

        /// <summary>"01:02:03": hours always shown, so the width does not jump at an hour. Pure.</summary>
        public static string FormatElapsed(TimeSpan elapsed)
        {
            long seconds = Math.Max(0, (long)elapsed.TotalSeconds);
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", seconds / 3600, seconds / 60 % 60, seconds % 60);
        }

        private IDisposable Attach(UiServices services)
        {
            _services = services;
            IVisualElementScheduledItem elapsed = schedule.Execute(ShowElapsed).Every(ElapsedIntervalMs);
            var subscriptions = new List<IDisposable>
            {
                _recording.State.Subscribe(_ => Show()),
                _recording.Problem.Subscribe(_ => Show()),
                new DisposeAction(elapsed.Pause),
            };
            if (services?.Localizer != null)
            {
                Action<LocaleInfo> relabel = _ => Show();
                services.Localizer.LocaleChanged += relabel;
                subscriptions.Add(new DisposeAction(() => services.Localizer.LocaleChanged -= relabel));
            }

            Show();
            return new DisposeAll(subscriptions);
        }

        private void Show()
        {
            RecordingState state = _recording.State.Value;
            RecordView view = ViewOf(state, _recording.Problem.Value);
            bool busy = state == RecordingState.Starting || state == RecordingState.Stopping;
            _busy.Set(busy ? RecordStrings.Busy : null);
            StartButton.TextKey = view.StartKey;
            StartButton.style.display = view.ShowStart ? DisplayStyle.Flex : DisplayStyle.None;
            StopButton.style.display = view.ShowStop ? DisplayStyle.Flex : DisplayStyle.None;
            Chip.style.display = view.Chip != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (view.Chip is StatusKind chip)
            {
                Chip.Status = chip;
                Chip.TextKey = view.ChipKey;
            }

            _problemText.Key = view.ProblemKey;
            Problem.style.display = view.ProblemKey != null ? DisplayStyle.Flex : DisplayStyle.None;

            string last = _recording.LastFile;
            bool showSaved = last != null && !busy && state != RecordingState.Recording;
            Saved.style.display = showSaved ? DisplayStyle.Flex : DisplayStyle.None;
            SavedText.text = showSaved ? _services?.Localizer?.Get(RecordStrings.Saved, ("name", Path.GetFileName(last))) ?? Path.GetFileName(last) : string.Empty;
            ShowElapsed();
        }

        private void ShowElapsed()
        {
            Chip.Number = _recording.State.Value == RecordingState.Recording ? FormatElapsed(_recording.Elapsed) : null;
        }
    }
}
