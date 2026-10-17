// Only in the Unity Editor: the gallery is a tool for building the UI, not part of the application
// (UI foundation detailed design 7.7).
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.Core.State;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Frames;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Notifications;

namespace VirtualVessel.UI.Screens
{
    /// <summary>
    /// The component gallery (Unity Editor and Developer Mode only): the common components of the
    /// styleboard laid out in Unity, to check how they look and work (UI foundation detailed design 7.7).
    /// Minimal; may be revised.
    /// </summary>
    /// <remarks>
    /// Everything here is a sample: notices, the dialog, and the menu really open, but nothing changes
    /// any setting or state. Parts that need video (media frame, layer list, preview) are left out.
    /// </remarks>
    public sealed class ComponentGalleryScreen : VisualElement
    {
        public const string UssClass = "vv-gallery";
        public const string ColumnsClass = UssClass + "__columns";
        public const string ColumnClass = UssClass + "__column";
        public const string CardClass = UssClass + "__card";
        public const string ItemClass = UssClass + "__item";
        public const string RowClass = UssClass + "__row";
        public const string NoteClass = UssClass + "__note";

        private readonly INotificationService _notifications;
        private readonly Dictionary<string, Card> _sections = new Dictionary<string, Card>(StringComparer.Ordinal);
        private readonly VisualElement[] _columns;

        /// <param name="notifications">Shows the sample notices; optional.</param>
        public ComponentGalleryScreen(INotificationService notifications)
        {
            _notifications = notifications;
            AddToClassList(UssClass);
            Add(Ui.Text(GalleryStrings.Note, NoteClass, "vv-text-body"));

            // Two columns whose outer edges meet the screen's edges; a wrapping grid would leave a gutter
            // after the last card of each row. The table needs the full width and goes below.
            var columns = new VisualElement();
            columns.AddToClassList(ColumnsClass);
            Add(columns);
            _columns = new VisualElement[2];
            for (int i = 0; i < _columns.Length; i++)
            {
                _columns[i] = new VisualElement();
                _columns[i].AddToClassList(ColumnClass);
                _columns[i].EnableInClassList(ColumnClass + "--first", i == 0);
                columns.Add(_columns[i]);
            }

            Section(GalleryStrings.Buttons, Buttons(), _columns[0]);
            Section(GalleryStrings.Tooltips, Tooltips(), _columns[1]);
            Section(GalleryStrings.Toggles, Toggles(), _columns[0]);
            Section(GalleryStrings.Status, Status(), _columns[1]);
            Section(GalleryStrings.Fields, Fields(), _columns[0]);
            Section(GalleryStrings.Tabs, Tabs(), _columns[1]);
            Section(GalleryStrings.Notices, Notices(), _columns[1]);
            Section(GalleryStrings.Dialog, Dialogs(), _columns[0]);
            Section(GalleryStrings.Menu, Menus(), _columns[1]);
            Section(GalleryStrings.Table, Table(), this);
        }

        /// <summary>The section cards by their title key.</summary>
        public IReadOnlyDictionary<string, Card> Sections => _sections;

        private void Section(string titleKey, IEnumerable<VisualElement> content, VisualElement parent)
        {
            Card card = Ui.Card(titleKey);
            card.AddToClassList(CardClass);
            foreach (VisualElement element in content)
            {
                // A class of our own for the spacing, rather than a selector into the card's internals.
                element.AddToClassList(ItemClass);
                card.Add(element);
            }

            _sections.Add(titleKey, card);
            parent.Add(card);
        }

        private static IEnumerable<VisualElement> Buttons()
        {
            yield return Row(
                Ui.Button(GalleryStrings.Primary, ButtonVariant.Primary),
                Ui.Button(GalleryStrings.Secondary));

            // A command that cannot run: the button is disabled and its tooltip says why.
            var disabled = new UiCommand(() => { }, new ObservableValue<string>(GalleryStrings.DisabledReason));
            yield return Row(
                Ui.Button(GalleryStrings.Danger, ButtonVariant.Danger),
                Ui.Button(GalleryStrings.Disabled, ButtonVariant.Primary).Bind(disabled));
        }

        private static IEnumerable<VisualElement> Tooltips()
        {
            yield return Row(Ui.Button(GalleryStrings.PointHere).WithTooltip(GalleryStrings.SampleTip, "Ctrl+T"));
            yield return Note(GalleryStrings.TooltipNote);
        }

        private static IEnumerable<VisualElement> Toggles()
        {
            SwitchToggle on = Ui.Toggle(GalleryStrings.ConvertVoice);
            on.SetValueWithoutNotify(true);
            yield return on;
            yield return Ui.Toggle(GalleryStrings.ConvertVoice);

            IconButton mic = Ui.IconButton(IconName.Mic, toggle: true).WithTooltip(GalleryStrings.MicTip, "Ctrl+M");
            mic.RegisterCallback<ChangeEvent<bool>>(evt => mic.IconName = evt.newValue ? IconName.MicOff : IconName.Mic);
            yield return Row(mic, Ui.IconButton(IconName.Fullscreen).WithTooltip(GalleryStrings.FullscreenTip, "F"));
        }

        private static IEnumerable<VisualElement> Status()
        {
            StatusChip live = Ui.Chip(StatusKind.Live, GalleryStrings.Live);
            live.Number = "01:24:36";
            yield return Row(Ui.Chip(StatusKind.Idle, GalleryStrings.Idle), live);
            StatusChip good = Ui.Chip(StatusKind.Success, GalleryStrings.Good);
            good.Number = "38 ms";
            yield return Row(good, Ui.Chip(StatusKind.Warning, GalleryStrings.Unstable), Ui.Chip(StatusKind.Error, GalleryStrings.Disconnected));

            var meter = new LevelMeter { Segments = 24 };
            meter.Level = 0.8f;
            yield return meter;
            yield return Row(new KeyHint("Ctrl+M"), new KeyHint("F"), new KeyHint("Esc"));
        }

        private static IEnumerable<VisualElement> Fields()
        {
            TextInput title = Ui.TextField(GalleryStrings.Title);
            title.PlaceholderKey = GalleryStrings.TitlePlaceholder;
            yield return title;

            TextInput missing = Ui.TextField(GalleryStrings.Title);
            missing.ErrorKey = GalleryStrings.TitleError;
            yield return missing;

            NumberInput rate = Ui.NumberField(GalleryStrings.SampleRate, "Hz");
            rate.Max = 192000;
            rate.value = 48000;
            yield return rate;

            SelectField device = Ui.Select(GalleryStrings.Microphone);
            device.SetChoices(new[] { SelectChoice.FromText("mic-1", "USB Mic (Yeti)"), SelectChoice.FromText("mic-2", "Line In (Realtek)") });
            device.SetValueWithoutNotify("mic-1");
            yield return device;

            ValueSlider pitch = Ui.Slider(GalleryStrings.Pitch, -12, 12);
            pitch.Step = 1;
            pitch.Signed = true;
            pitch.value = 3;
            yield return pitch;
        }

        private static IEnumerable<VisualElement> Tabs()
        {
            yield return Ui.Tabs(GalleryStrings.TabAvatar, GalleryStrings.TabVoice, GalleryStrings.TabStage);
            SettingRow row = Ui.SettingRow(GalleryStrings.Noise, GalleryStrings.NoiseDescription);
            row.Add(Ui.Segmented(GalleryStrings.Weak, GalleryStrings.Strong));
            yield return row;
        }

        private IEnumerable<VisualElement> Notices()
        {
            yield return Row(
                NoticeButton(GalleryStrings.ShowInfo, NoticeKind.Info),
                NoticeButton(GalleryStrings.ShowSuccess, NoticeKind.Success),
                NoticeButton(GalleryStrings.ShowWarning, NoticeKind.Warning),
                NoticeButton(GalleryStrings.ShowError, NoticeKind.Error));

            ProgressMeter training = Ui.Progress(GalleryStrings.Training);
            training.Progress = 0.6f;
            training.Detail = "60%";
            yield return training;
        }

        private IEnumerable<VisualElement> Dialogs()
        {
            yield return Row(DialogButton(GalleryStrings.OpenDialog, ConfirmStyle.Normal), DialogButton(GalleryStrings.OpenDanger, ConfirmStyle.Danger));
        }

        private static IEnumerable<VisualElement> Menus()
        {
            ActionButton open = Ui.Button(GalleryStrings.OpenMenu);
            open.clicked += () =>
            {
                Rect bounds = open.worldBound;
                PopupMenu.Show(open, new Vector2(bounds.xMin, bounds.yMax + 4f), MenuEntries(), () => open.Focus());
            };
            yield return Row(open);
        }

        private static IReadOnlyList<MenuEntry> MenuEntries()
        {
            // The same rows as the placement editor's menu: a checked item, shortcuts, a separator, and a disabled item.
            return new[]
            {
                new MenuEntry(FrameStrings.Flip, () => { }, "H", FrameStrings.FlipTip, isChecked: true),
                new MenuEntry(FrameStrings.Lock, () => { }, "L", FrameStrings.LockTip, isChecked: false),
                MenuEntry.Separator,
                new MenuEntry(FrameStrings.Fit, () => { }, "Shift+1", FrameStrings.FitTip),
                new MenuEntry(FrameStrings.ResetCrop, () => { }, "R", FrameStrings.ResetCropTip, enabled: false),
            };
        }

        private static IEnumerable<VisualElement> Table()
        {
            DataTable table = Ui.Table(
                TableColumn.Filling(ScreenStrings.ColumnName, 200, monospace: true),
                TableColumn.Key(ScreenStrings.ColumnKind, 72),
                TableColumn.Number(ScreenStrings.ColumnCurrent, 108),
                TableColumn.Number(ScreenStrings.ColumnMax, 108),
                TableColumn.Number(ScreenStrings.ColumnCount, 80));
            table.SetRows(new[]
            {
                new[] { "MainThreadFrameTime", ScreenStrings.KindTimer, "5.79 ms", "11.5 ms", "296" },
                new[] { "SystemUsedMemory", ScreenStrings.KindGauge, "1,553.86 MB", "1,556.22 MB", "296" },
                new[] { "Underruns", ScreenStrings.KindCounter, "3", string.Empty, string.Empty },
            });
            yield return table;
        }

        private ActionButton NoticeButton(string textKey, NoticeKind kind)
        {
            ActionButton button = Ui.Button(textKey);
            button.clicked += () => _notifications?.Show(kind, GalleryStrings.NoticeTitle, GalleryStrings.NoticeBody);
            return button;
        }

        private ActionButton DialogButton(string textKey, ConfirmStyle style)
        {
            ActionButton button = Ui.Button(textKey, style == ConfirmStyle.Danger ? ButtonVariant.Danger : ButtonVariant.Secondary);

            // Fire and forget: the dialog only shows how it looks, and its answer is not used.
            button.clicked += () => _ = UiServices.Find(this)?.Dialogs?.ConfirmAsync(GalleryStrings.DialogTitle, GalleryStrings.DialogBody, GalleryStrings.DialogConfirm, style);
            return button;
        }

        private static VisualElement Row(params VisualElement[] elements)
        {
            var row = new VisualElement();
            row.AddToClassList(RowClass);
            foreach (VisualElement element in elements)
            {
                row.Add(element);
            }

            return row;
        }

        private static Label Note(string key)
        {
            return Ui.Text(key, NoteClass, "vv-text-caption");
        }
    }
}
#endif
