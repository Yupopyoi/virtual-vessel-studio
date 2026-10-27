using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Recording;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of the recording Card (docs/&lt;lang&gt;/ui/screens/setup.md).</summary>
    [StringKeys]
    public static class RecordingCardStrings
    {
        public const string Title = "setup.recording.title";
        public const string Unavailable = "setup.recording.unavailable";
        public const string Folder = "setup.recording.folder";
        public const string FolderDescription = "setup.recording.folder.description";
        public const string FolderPick = "setup.recording.folder.pick";
        public const string FolderPickTitle = "setup.recording.folder.pick-title";
        public const string FolderOpen = "setup.recording.folder.open";
        public const string Quality = "setup.recording.quality";
        public const string QualityDescription = "setup.recording.quality.description";
        public const string QualityStandard = "setup.recording.quality.standard";
        public const string QualityHigh = "setup.recording.quality.high";
        public const string Free = "setup.recording.free";
        public const string FreeValue = "setup.recording.free.value";
        public const string FreeUnknown = "setup.recording.free.unknown";
        public const string Format = "setup.recording.format";
        public const string FormatValue = "setup.recording.format.value";
    }

    /// <summary>
    /// Recording on the setup screen (recording detailed design 18.2): the folder, the quality, the free
    /// space there, and the fixed size and rate. Changes go to the recording service, which saves them in
    /// the project's streaming profile; a recording in progress keeps its own.
    /// </summary>
    public sealed class RecordingCard : VisualElement
    {
        public const string UssClass = "vv-recording-card";
        public const string FolderClass = UssClass + "__folder";
        public const string FolderButtonsClass = UssClass + "__folder-buttons";

        /// <summary>How often the free space is read again while the Card is shown.</summary>
        public const long FreeIntervalMs = 10_000;

        private readonly IRecordingService _recording;
        private readonly IFileDialog _files;
        private UiServices _services;

        public RecordingCard(IRecordingService recording, IFileDialog files, IFileExplorer explorer)
        {
            _recording = recording ?? throw new ArgumentNullException(nameof(recording));
            _files = files;
            AddToClassList(UssClass);
            Card card = Ui.Card(RecordingCardStrings.Title);
            Add(card);

            FolderText = new Label();
            FolderText.AddToClassList(FolderClass);
            FolderText.AddToClassList("vv-text-mono");
            PickButton = Ui.Button(RecordingCardStrings.FolderPick).Bind(new UiCommand(PickFolderAsync));
            PickButton.style.display = files != null ? DisplayStyle.Flex : DisplayStyle.None;
            OpenButton = Ui.Button(RecordingCardStrings.FolderOpen).Bind(new UiCommand(() => explorer?.Show(_recording.Options.Value.Folder)));
            OpenButton.style.display = explorer != null ? DisplayStyle.Flex : DisplayStyle.None;
            var folderButtons = new VisualElement();
            folderButtons.AddToClassList(FolderButtonsClass);
            folderButtons.Add(PickButton);
            folderButtons.Add(OpenButton);
            SettingRow folder = Ui.SettingRow(RecordingCardStrings.Folder, RecordingCardStrings.FolderDescription);
            folder.Add(FolderText);
            folder.Add(folderButtons);
            card.Add(folder);

            Quality = Ui.Segmented(RecordingCardStrings.QualityStandard, RecordingCardStrings.QualityHigh);
            Quality.RegisterValueChangedCallback(evt => _recording.SetOptions(_recording.Options.Value.WithQuality((RecordingQuality)evt.newValue)));
            SettingRow quality = Ui.SettingRow(RecordingCardStrings.Quality, RecordingCardStrings.QualityDescription);
            quality.Add(Quality);
            card.Add(quality);

            FreeText = new Label();
            FreeText.AddToClassList("vv-text-mono");
            SettingRow free = Ui.SettingRow(RecordingCardStrings.Free);
            free.Add(FreeText);
            card.Add(free);

            FormatText = new Label();
            FormatText.AddToClassList("vv-text-body");
            SettingRow format = Ui.SettingRow(RecordingCardStrings.Format);
            format.Add(FormatText);
            card.Add(format);

            PanelLifetime.WhileAttached(this, Attach);
        }

        public Label FolderText { get; }

        public ActionButton PickButton { get; }

        public ActionButton OpenButton { get; }

        public Segmented Quality { get; }

        public Label FreeText { get; }

        public Label FormatText { get; }

        /// <summary>"123.4" gigabytes, with one decimal so a nearly full disk still shows a change. Pure.</summary>
        public static string Gigabytes(long bytes)
        {
            return (bytes / (1024.0 * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture);
        }

        private IDisposable Attach(UiServices services)
        {
            _services = services;
            IVisualElementScheduledItem free = schedule.Execute(ShowFree).Every(FreeIntervalMs);
            var subscriptions = new List<IDisposable>
            {
                _recording.Options.Subscribe(_ => Show()),
                new DisposeAction(free.Pause),
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

        private async Task PickFolderAsync(CancellationToken cancellationToken)
        {
            if (_files == null)
            {
                return;
            }

            string picked = await _files.PickFolderAsync(_services?.Localizer?.Get(RecordingCardStrings.FolderPickTitle));
            if (!string.IsNullOrEmpty(picked))
            {
                _recording.SetOptions(_recording.Options.Value.WithFolder(picked));
            }
        }

        private void Show()
        {
            RecordingOptions options = _recording.Options.Value;
            FolderText.text = options.Folder ?? string.Empty;
            Quality.SetValueWithoutNotify((int)options.Quality);
            FormatText.text = _services?.Localizer?.Get(RecordingCardStrings.FormatValue, ("width", options.Width), ("height", options.Height), ("rate", options.FrameRate))
                ?? $"{options.Width}×{options.Height}, {options.FrameRate} fps";
            ShowFree();
        }

        private void ShowFree()
        {
            long? free = _recording.FreeBytes();
            ILocalizer localizer = _services?.Localizer;
            FreeText.text = free is long bytes
                ? localizer?.Get(RecordingCardStrings.FreeValue, ("size", Gigabytes(bytes))) ?? Gigabytes(bytes) + " GB"
                : localizer?.Get(RecordingCardStrings.FreeUnknown) ?? string.Empty;
        }
    }
}
