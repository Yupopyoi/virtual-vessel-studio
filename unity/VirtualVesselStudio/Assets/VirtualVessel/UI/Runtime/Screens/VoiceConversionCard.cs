using System;
using System.Globalization;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Voice;

namespace VirtualVessel.UI.Screens
{
    [StringKeys]
    public static class VoiceConversionStrings
    {
        public const string Title = "diagnostics.voice.title";
        public const string Enable = "diagnostics.voice.enable";
        public const string Semitones = "diagnostics.voice.semitones";
        public const string StateOff = "diagnostics.voice.state.off";
        public const string StateLoading = "diagnostics.voice.state.loading";
        public const string StateRunning = "diagnostics.voice.state.running";
        public const string Window = "diagnostics.voice.window";
        public const string Window100 = "diagnostics.voice.window.100";
        public const string Window250 = "diagnostics.voice.window.250";
        public const string Window540 = "diagnostics.voice.window.540";
        public const string Tail = "diagnostics.voice.tail";
        public const string Tail30 = "diagnostics.voice.tail.30";
        public const string Tail10 = "diagnostics.voice.tail.10";
        public const string Denoise = "diagnostics.voice.denoise";
        public const string Loading = "diagnostics.voice.loading";
        public const string PlayFile = "diagnostics.voice.play-file";
        public const string StopFile = "diagnostics.voice.stop-file";
        public const string FileFilter = "diagnostics.voice.file-filter";
        public const string FileUnreadable = "diagnostics.voice.file-unreadable";
        public const string StatePlayingFile = "diagnostics.voice.state.playing-file";
        public const string StateNoMicrophone = "diagnostics.voice.state.no-microphone";
        public const string StateUnavailable = "diagnostics.voice.state.unavailable";
        public const string StateFailed = "diagnostics.voice.state.failed";
    }

    /// <summary>
    /// Turns the voice conversion prototype on and shows its timing. On the Diagnostics screen, so only
    /// in Developer Mode, until the Voice module has its detailed design and its real settings.
    /// </summary>
    public sealed class VoiceConversionCard : VisualElement
    {
        public const string UssClass = "vv-voice-card";
        public const string TextClass = UssClass + "__text";

        private const long RefreshMs = 500;
        private const float SemitoneRange = 24f;

        private readonly IVoiceConversion _voice;
        private readonly KeyedText _stateText;
        private readonly Label _stats;
        private readonly SwitchToggle _toggle;
        private readonly IFileDialog _files;
        private readonly ActionButton _stopFile;
        private readonly ProgressMeter _progress;
        private readonly Label _file;
        private IDisposable _stateSubscription;
        private IVisualElementScheduledItem _refresh;

        /// <param name="files">Picks a WAV file to convert; null leaves that out.</param>
        public VoiceConversionCard(IVoiceConversion voice, IFileDialog files = null)
        {
            _voice = voice ?? throw new ArgumentNullException(nameof(voice));
            _files = files;
            AddToClassList(UssClass);
            Card card = Ui.Card(VoiceConversionStrings.Title);
            Add(card);

            _toggle = Ui.Toggle(VoiceConversionStrings.Enable);
            _toggle.RegisterValueChangedCallback(evt => _voice.SetEnabled(evt.newValue));
            card.Add(_toggle);

            ValueSlider semitones = Ui.Slider(VoiceConversionStrings.Semitones, -SemitoneRange, SemitoneRange, null);
            semitones.Step = 1f;
            semitones.Signed = true;
            semitones.SetValueWithoutNotify(_voice.Semitones);
            semitones.RegisterValueChangedCallback(evt => _voice.Semitones = evt.newValue);
            card.Add(semitones);

            card.Add(Text(VoiceConversionStrings.Window));
            Segmented window = Ui.Segmented(VoiceConversionStrings.Window100, VoiceConversionStrings.Window250, VoiceConversionStrings.Window540);
            int[] windows = { 100, 250, 540 };
            window.SetValueWithoutNotify(Math.Max(0, Array.IndexOf(windows, _voice.WindowFrames)));
            window.RegisterValueChangedCallback(evt => _voice.WindowFrames = windows[evt.newValue]);
            card.Add(window);

            card.Add(Text(VoiceConversionStrings.Tail));
            Segmented tailChoice = Ui.Segmented(VoiceConversionStrings.Tail30, VoiceConversionStrings.Tail10);
            tailChoice.SetValueWithoutNotify(_voice.TailGuardFrames == 1 ? 1 : 0);
            tailChoice.RegisterValueChangedCallback(evt => _voice.TailGuardFrames = evt.newValue == 1 ? 1 : 3);
            card.Add(tailChoice);

            SwitchToggle denoise = Ui.Toggle(VoiceConversionStrings.Denoise);
            denoise.SetValueWithoutNotify(_voice.Denoise);
            denoise.RegisterValueChangedCallback(evt => _voice.Denoise = evt.newValue);
            card.Add(denoise);

            if (files != null)
            {
                ActionButton playFile = Ui.Button(VoiceConversionStrings.PlayFile);
                playFile.clicked += OnPlayFile;
                card.Add(playFile);
                _stopFile = Ui.Button(VoiceConversionStrings.StopFile);
                _stopFile.clicked += _voice.StopFile;
                card.Add(_stopFile);
            }

            _progress = Ui.Progress(VoiceConversionStrings.Loading);
            _progress.style.display = DisplayStyle.None;
            card.Add(_progress);

            _file = Text();
            _file.AddToClassList("vv-text-mono");
            card.Add(_file);

            Label state = Text();
            state.AddToClassList("vv-text-body");
            _stateText = new KeyedText(state, value => state.text = value);
            card.Add(state);

            _stats = Text();
            _stats.AddToClassList("vv-text-mono");
            card.Add(_stats);

            RegisterCallback<AttachToPanelEvent>(_ => OnAttached());
            RegisterCallback<DetachFromPanelEvent>(_ => OnDetached());
        }

        /// <summary>The state's text key. Pure.</summary>
        public static string StateKey(VoiceConversionState state)
        {
            switch (state)
            {
                case VoiceConversionState.Loading:
                    return VoiceConversionStrings.StateLoading;
                case VoiceConversionState.Running:
                    return VoiceConversionStrings.StateRunning;
                case VoiceConversionState.PlayingFile:
                    return VoiceConversionStrings.StatePlayingFile;
                case VoiceConversionState.NoMicrophone:
                    return VoiceConversionStrings.StateNoMicrophone;
                case VoiceConversionState.Unavailable:
                    return VoiceConversionStrings.StateUnavailable;
                case VoiceConversionState.Failed:
                    return VoiceConversionStrings.StateFailed;
                default:
                    return VoiceConversionStrings.StateOff;
            }
        }

        /// <summary>The playing file and its place; empty when none is. Pure.</summary>
        public static string FormatFile(string name, float position, float length)
        {
            return name == null ? string.Empty : string.Format(CultureInfo.InvariantCulture, "{0}  {1:F0} / {2:F0} s", name, position, length);
        }

        /// <summary>Numbers read the same in every language, so this needs no string table. Pure.</summary>
        public static string FormatStats(VoiceConversionStats stats)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:F0} / {1:F0} ms ({2:F0} ms main)  {3} blocks, {4} dropped", stats.WorkMs, stats.BlockMs, stats.IssueMs, stats.Blocks, stats.Dropped);
        }

        private void OnAttached()
        {
            _stateSubscription = _voice.State.Subscribe(OnState);
            _refresh = schedule.Execute(Refresh).Every(RefreshMs);
        }

        // A Label takes the panel's default dark text unless a class gives it a color.
        private static Label Text()
        {
            Label label = QuietText.Of(new Label());
            label.AddToClassList(TextClass);
            return label;
        }

        private static Label Text(string key)
        {
            Label label = Ui.Text(key, TextClass);
            return label;
        }

        private void Refresh()
        {
            _stats.text = FormatStats(_voice.Stats);
            _progress.Progress = _voice.LoadProgress;
            (float position, float length) = _voice.FileTime;
            _file.text = FormatFile(_voice.FileName, position, length);
        }

        private void OnState(VoiceConversionState state)
        {
            _stateText.Key = StateKey(state);
            _progress.style.display = state == VoiceConversionState.Loading ? DisplayStyle.Flex : DisplayStyle.None;
            _toggle.SetValueWithoutNotify(state != VoiceConversionState.Off && state != VoiceConversionState.Failed && state != VoiceConversionState.Unavailable);
            _stopFile?.SetEnabled(state == VoiceConversionState.PlayingFile);
        }

        private async void OnPlayFile()
        {
            var localizer = UiServices.Find(this)?.Localizer;
            string path = await _files.OpenAsync(new FileDialogOptions
            {
                Filters = new[] { new FileFilter(localizer?.Get(VoiceConversionStrings.FileFilter) ?? "WAV", new[] { ".wav" }) },
            });
            if (path != null && !await _voice.PlayFileAsync(path))
            {
                _stateText.Key = VoiceConversionStrings.FileUnreadable;
            }
        }

        private void OnDetached()
        {
            _stateSubscription?.Dispose();
            _stateSubscription = null;
            _refresh?.Pause();
            _refresh = null;
        }
    }
}
