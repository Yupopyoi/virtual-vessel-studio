using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Tracking;
using VirtualVessel.Tracking.Recording;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of the tracking recordings Card (UI foundation detailed design 7.7).</summary>
    [StringKeys]
    public static class RecordingStrings
    {
        public const string Title = "diagnostics.recordings.title";
        public const string StateIdle = "diagnostics.recordings.state.idle";
        public const string StateRecording = "diagnostics.recordings.state.recording";
        public const string StateReplaying = "diagnostics.recordings.state.replaying";
        public const string Description = "diagnostics.recordings.description";
        public const string DescriptionPlaceholder = "diagnostics.recordings.description.placeholder";
        public const string Start = "diagnostics.recordings.start";
        public const string Stop = "diagnostics.recordings.stop";
        public const string StartNeedsTracking = "diagnostics.recordings.start.needs-tracking";
        public const string StartDuringReplay = "diagnostics.recordings.start.during-replay";
        public const string ColumnDescription = "diagnostics.recordings.column.description";
        public const string ColumnDate = "diagnostics.recordings.column.date";
        public const string ColumnLength = "diagnostics.recordings.column.length";
        public const string ColumnSize = "diagnostics.recordings.column.size";
        public const string Unreadable = "diagnostics.recordings.unreadable";
        public const string NoDescription = "diagnostics.recordings.no-description";
        public const string Choice = "diagnostics.recordings.choice";
        public const string Replay = "diagnostics.recordings.replay";
        public const string StopReplay = "diagnostics.recordings.replay.stop";
        public const string Delete = "diagnostics.recordings.delete";
        public const string DeleteAll = "diagnostics.recordings.delete-all";
        public const string DeleteTitle = "diagnostics.recordings.delete.title";
        public const string DeleteBody = "diagnostics.recordings.delete.body";
        public const string DeleteAllTitle = "diagnostics.recordings.delete-all.title";
        public const string DeleteAllBody = "diagnostics.recordings.delete-all.body";
        public const string Note = "diagnostics.recordings.note";
        public const string Total = "diagnostics.recordings.total";
        public const string Failed = "diagnostics.recordings.failed";
    }

    /// <summary>
    /// Records and replays tracking observations for developers (UI foundation detailed design 7.7,
    /// tracking detailed design 12): the state, recording with a description, the list, and replaying
    /// or deleting the chosen recording. Shown on the Diagnostics screen, so only in Developer Mode.
    /// </summary>
    public sealed class TrackingRecordingsCard : VisualElement
    {
        public const string UssClass = "vv-recordings";
        public const string RowClass = UssClass + "__row";
        public const string NoteClass = UssClass + "__note";

        private const long ElapsedIntervalMs = 1000;

        private static readonly TableColumn[] s_columns =
        {
            TableColumn.Filling(RecordingStrings.ColumnDescription, 200),
            TableColumn.Text(RecordingStrings.ColumnDate, 160, monospace: true),
            TableColumn.Number(RecordingStrings.ColumnLength, 80),
            TableColumn.Number(RecordingStrings.ColumnSize, 96),
        };

        private readonly ITrackingRecordings _recordings;
        private readonly ITrackingSource _tracking;
        private readonly Label _total;
        private UiServices _services;
        private bool _busy;

        /// <param name="tracking">Tracking, to know whether recording can start; null allows only replay.</param>
        public TrackingRecordingsCard(ITrackingRecordings recordings, ITrackingSource tracking)
        {
            _recordings = recordings ?? throw new ArgumentNullException(nameof(recordings));
            _tracking = tracking;
            AddToClassList(UssClass);
            Card card = Ui.Card(RecordingStrings.Title);
            Add(card);

            State = Ui.Chip(StatusKind.Idle, RecordingStrings.StateIdle);
            card.Add(State);

            DescriptionField = Ui.TextField(RecordingStrings.Description);
            DescriptionField.PlaceholderKey = RecordingStrings.DescriptionPlaceholder;

            // No length limit on the field itself. With one, committing a Japanese word left the caret one
            // character early, so what followed went in before the word's last character ("じゃんけん" became
            // "じんけんゃ", the user's video, 2026-10-25); the limit is the only thing this field does unlike
            // the others. The description is cut to its length when the recording starts
            // (TrackingRecordingFormat.NormalizeDescription).
            RecordButton = Ui.Button(RecordingStrings.Start, ButtonVariant.Primary);
            RecordButton.clicked += () => _ = Guarded(ToggleRecordingAsync);
            card.Add(Row(DescriptionField, RecordButton));

            Table = Ui.Table(s_columns);
            card.Add(Table);

            Choice = Ui.Select(RecordingStrings.Choice);
            Choice.RegisterValueChangedCallback(_ => Show());
            ReplayButton = Ui.Button(RecordingStrings.Replay);
            ReplayButton.clicked += () => _ = Guarded(ToggleReplayAsync);
            DeleteButton = Ui.Button(RecordingStrings.Delete, ButtonVariant.Danger);
            DeleteButton.clicked += () => _ = Guarded(DeleteAsync);
            DeleteAllButton = Ui.Button(RecordingStrings.DeleteAll, ButtonVariant.Danger);
            DeleteAllButton.clicked += () => _ = Guarded(DeleteAllAsync);
            card.Add(Row(Choice, ReplayButton, DeleteButton, DeleteAllButton));

            card.Add(Ui.Text(RecordingStrings.Note, NoteClass, "vv-text-caption"));
            _total = new Label();
            _total.AddToClassList(NoteClass);
            _total.AddToClassList("vv-text-caption");
            card.Add(_total);

            PanelLifetime.WhileAttached(this, Attach);
        }

        public StatusChip State { get; }

        public TextInput DescriptionField { get; }

        public ActionButton RecordButton { get; }

        public DataTable Table { get; }

        /// <summary>The recording to replay or delete.</summary>
        public SelectField Choice { get; }

        public ActionButton ReplayButton { get; }

        public ActionButton DeleteButton { get; }

        public ActionButton DeleteAllButton { get; }

        /// <summary>The total line, such as "3 recordings, 12.4 MB".</summary>
        public string TotalText => _total.text;

        /// <summary>A length such as "1:05". Pure.</summary>
        public static string Length(double seconds)
        {
            int whole = (int)Math.Max(0, Math.Floor(seconds));
            return $"{whole / 60}:{whole % 60:00}";
        }

        /// <summary>A size such as "15.2 MB" or "640 KB". Pure.</summary>
        public static string Size(long bytes)
        {
            const double Mega = 1024 * 1024;
            return bytes >= Mega
                ? (bytes / Mega).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
                : Math.Ceiling(bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
        }

        /// <summary>
        /// A recording's row: its description (or <paramref name="unreadable"/>, or
        /// <paramref name="noDescription"/>), when it started in <paramref name="zone"/>, length, and
        /// size. Pure.
        /// </summary>
        public static string[] Row(TrackingRecordingInfo info, TimeZoneInfo zone, string unreadable, string noDescription)
        {
            if (!info.Readable)
            {
                return new[] { $"{unreadable} ({info.Id})", string.Empty, string.Empty, Size(info.Bytes) };
            }

            string description = info.Description.Length > 0 ? info.Description : noDescription;
            string date = TimeZoneInfo.ConvertTimeFromUtc(info.StartedAtUtc, zone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            return new[] { description, date, Length(info.Seconds), Size(info.Bytes) };
        }

        /// <summary>The text a recording is chosen by: its date and description. Pure.</summary>
        public static string ChoiceText(TrackingRecordingInfo info, TimeZoneInfo zone, string unreadable)
        {
            if (!info.Readable)
            {
                return $"{unreadable} ({info.Id})";
            }

            string date = TimeZoneInfo.ConvertTimeFromUtc(info.StartedAtUtc, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            return info.Description.Length > 0 ? $"{date}  {info.Description}" : date;
        }

        private static VisualElement Row(params VisualElement[] items)
        {
            var row = new VisualElement();
            row.AddToClassList(RowClass);
            foreach (VisualElement item in items)
            {
                row.Add(item);
            }

            return row;
        }

        private IDisposable Attach(UiServices services)
        {
            _services = services;
            IVisualElementScheduledItem elapsed = schedule.Execute(ShowElapsed).Every(ElapsedIntervalMs);
            var subscriptions = new List<IDisposable>
            {
                _recordings.Activity.Subscribe(_ => Show()),
                _recordings.Recordings.Subscribe(_ => ShowList()),
                new DisposeAction(elapsed.Pause),
            };
            if (_tracking != null)
            {
                subscriptions.Add(_tracking.State.Subscribe(_ => Show()));
            }

            if (services?.Localizer != null)
            {
                Action<LocaleInfo> relabel = _ => ShowList();
                services.Localizer.LocaleChanged += relabel;
                subscriptions.Add(new DisposeAction(() => services.Localizer.LocaleChanged -= relabel));
            }

            ShowList();
            _ = Guarded(() => _recordings.RefreshAsync(CancellationToken.None));
            return new DisposeAll(subscriptions);
        }

        private void ShowList()
        {
            IReadOnlyList<TrackingRecordingInfo> list = _recordings.Recordings.Value;
            string unreadable = Text(RecordingStrings.Unreadable);
            string noDescription = Text(RecordingStrings.NoDescription);
            var rows = new List<string[]>(list.Count);
            var choices = new List<SelectChoice>(list.Count);
            long bytes = 0;
            foreach (TrackingRecordingInfo info in list)
            {
                rows.Add(Row(info, TimeZoneInfo.Local, unreadable, noDescription));
                choices.Add(SelectChoice.FromText(info.Id, ChoiceText(info, TimeZoneInfo.Local, unreadable)));
                bytes += info.Bytes;
            }

            Table.SetRows(rows);
            string chosen = Choice.value;
            Choice.SetChoices(choices);
            Choice.SetValueWithoutNotify(SelectField.IndexOf(choices, chosen) >= 0 ? chosen : (choices.Count > 0 ? choices[0].Id : string.Empty));
            _total.text = _services?.Localizer?.Plural(RecordingStrings.Total, list.Count, ("size", Size(bytes))) ?? string.Empty;
            Show();
        }

        private void Show()
        {
            RecordingActivity activity = _recordings.Activity.Value;
            bool trackingRunning = _tracking?.State.Value == TrackingState.Running;
            TrackingRecordingInfo chosen = Find(Choice.value);

            State.Status = activity switch
            {
                RecordingActivity.Recording => StatusKind.Live,
                RecordingActivity.Replaying => StatusKind.Success,
                _ => StatusKind.Idle,
            };
            State.TextKey = activity switch
            {
                RecordingActivity.Recording => RecordingStrings.StateRecording,
                RecordingActivity.Replaying => RecordingStrings.StateReplaying,
                _ => RecordingStrings.StateIdle,
            };
            ShowElapsed();

            bool recording = activity == RecordingActivity.Recording;
            bool replaying = activity == RecordingActivity.Replaying;
            RecordButton.TextKey = recording ? RecordingStrings.Stop : RecordingStrings.Start;
            RecordButton.TooltipKey = recording ? null : replaying ? RecordingStrings.StartDuringReplay : trackingRunning ? null : RecordingStrings.StartNeedsTracking;
            RecordButton.SetEnabled(!_busy && (recording || (!replaying && trackingRunning)));
            DescriptionField.SetEnabled(!_busy && activity == RecordingActivity.Idle);

            ReplayButton.TextKey = replaying ? RecordingStrings.StopReplay : RecordingStrings.Replay;
            ReplayButton.SetEnabled(!_busy && (replaying || (!recording && chosen != null && chosen.Readable)));
            DeleteButton.SetEnabled(!_busy && chosen != null && chosen.Id != _recordings.ActiveId);
            DeleteAllButton.SetEnabled(!_busy && _recordings.Recordings.Value.Count > 0);
            Choice.SetEnabled(!_busy && _recordings.Recordings.Value.Count > 0);
        }

        private void ShowElapsed()
        {
            State.Number = _recordings.Activity.Value == RecordingActivity.Recording ? Length(_recordings.RecordedSeconds) : null;
        }

        private TrackingRecordingInfo Find(string id)
        {
            foreach (TrackingRecordingInfo info in _recordings.Recordings.Value)
            {
                if (info.Id == id)
                {
                    return info;
                }
            }

            return null;
        }

        private string Text(string key)
        {
            return _services?.Localizer?.Get(key) ?? key;
        }

        private Task ToggleRecordingAsync()
        {
            if (_recordings.Activity.Value == RecordingActivity.Recording)
            {
                _recordings.StopRecording();
            }
            else
            {
                _recordings.StartRecording(DescriptionField.value);
            }

            return Task.CompletedTask;
        }

        private Task ToggleReplayAsync()
        {
            if (_recordings.Activity.Value == RecordingActivity.Replaying)
            {
                _recordings.StopReplay();
                return Task.CompletedTask;
            }

            return _recordings.StartReplayAsync(Choice.value, CancellationToken.None);
        }

        private async Task DeleteAsync()
        {
            string id = Choice.value;
            if (Find(id) == null)
            {
                return;
            }

            if (_services?.Dialogs != null && !await _services.Dialogs.ConfirmAsync(RecordingStrings.DeleteTitle, RecordingStrings.DeleteBody, RecordingStrings.Delete, ConfirmStyle.Danger))
            {
                return;
            }

            await _recordings.DeleteAsync(id, CancellationToken.None);
        }

        private async Task DeleteAllAsync()
        {
            if (_services?.Dialogs != null && !await _services.Dialogs.ConfirmAsync(RecordingStrings.DeleteAllTitle, RecordingStrings.DeleteAllBody, RecordingStrings.DeleteAll, ConfirmStyle.Danger))
            {
                return;
            }

            await _recordings.DeleteAllAsync(CancellationToken.None);
        }

        /// <summary>Runs one operation at a time, with the buttons disabled, and reports what fails.</summary>
        private async Task Guarded(Func<Task> operation)
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            Show();
            try
            {
                await operation();
            }
            catch (Exception exception)
            {
                _services?.Log?.Error("A tracking recording operation failed.", exception);
                _services?.ShowFailure?.Invoke(RecordingStrings.Failed);
            }
            finally
            {
                _busy = false;
                Show();
            }
        }
    }
}
