using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Localization;
using VirtualVessel.ProjectData.Records;
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
        public const string Voice = "diagnostics.voice.voice";
        public const string AutoPitch = "diagnostics.voice.auto-pitch";
        public const string Remeasure = "diagnostics.voice.remeasure";
        public const string Prototype = "diagnostics.voice.prototype";
        public const string Unusable = "diagnostics.voice.unusable";
        public const string Switching = "diagnostics.voice.switching";
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
        private readonly SelectField _voices;
        private readonly ActionButton _stopFile;
        private readonly ProgressMeter _progress;
        private readonly Label _file;
        private readonly Label _pitch;
        private IDisposable _stateSubscription;
        private IVisualElementScheduledItem _refresh;
        private VoiceConversionState _state;
        private bool _switching;

        /// <param name="files">Picks an audio file to convert; null leaves that out.</param>
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

            _voices = Ui.Select(VoiceConversionStrings.Voice);
            _voices.RegisterValueChangedCallback(evt => _voice.SelectVoice(evt.newValue));
            card.Add(_voices);

            SwitchToggle auto = Ui.Toggle(VoiceConversionStrings.AutoPitch);
            auto.SetValueWithoutNotify(_voice.AutoPitch);
            auto.RegisterValueChangedCallback(evt => _voice.AutoPitch = evt.newValue);
            card.Add(auto);
            _pitch = Text();
            _pitch.AddToClassList("vv-text-mono");
            card.Add(_pitch);
            ActionButton remeasure = Ui.Button(VoiceConversionStrings.Remeasure);
            remeasure.clicked += _voice.RemeasurePitch;
            card.Add(remeasure);

            ValueSlider semitones = Ui.Slider(VoiceConversionStrings.Semitones, -SemitoneRange, SemitoneRange, null);
            semitones.Step = 1f;
            semitones.Signed = true;
            semitones.SetValueWithoutNotify(_voice.Semitones);
            semitones.RegisterValueChangedCallback(evt => _voice.Semitones = evt.newValue);
            card.Add(semitones);

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

        /// <summary>The voice list: the prototype's under its own name, a voice this version cannot run marked so. Pure.</summary>
        public static IReadOnlyList<SelectChoice> Choices(IReadOnlyList<VoiceConversionVoice> voices)
        {
            var choices = new List<SelectChoice>();
            foreach (VoiceConversionVoice voice in voices)
            {
                if (string.IsNullOrEmpty(voice.Id))
                {
                    choices.Add(SelectChoice.FromKey(voice.Id ?? string.Empty, VoiceConversionStrings.Prototype));
                }
                else if (!voice.Usable)
                {
                    choices.Add(SelectChoice.FromKey(voice.Id, VoiceConversionStrings.Unusable));
                }
                else
                {
                    choices.Add(SelectChoice.FromText(voice.Id, voice.Name));
                }
            }

            return choices;
        }

        /// <summary>The playing file and its place; empty when none is. Pure.</summary>
        public static string FormatFile(string name, float position, float length)
        {
            return name == null ? string.Empty : string.Format(CultureInfo.InvariantCulture, "{0}  {1:F0} / {2:F0} s", name, position, length);
        }

        /// <summary>The pitch match as numbers, which read the same in every language. Pure.</summary>
        public static string FormatPitch(float userHz, float voiceHz, float autoSemitones)
        {
            string user = userHz > 0f ? string.Format(CultureInfo.InvariantCulture, "{0:F0} Hz", userHz) : "? Hz";
            string voice = voiceHz > 0f ? string.Format(CultureInfo.InvariantCulture, "{0:F0} Hz", voiceHz) : "? Hz";
            return string.Format(CultureInfo.InvariantCulture, "{0} -> {1}  ({2:+0;-0;0})", user, voice, autoSemitones);
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
            ShowVoices();
        }

        // Read when the card is shown: voices made in Voice Lab since then appear on the next visit.
        private void ShowVoices()
        {
            CrudResult<IReadOnlyList<VoiceConversionVoice>> voices = _voice.ListVoices();
            _voices.SetChoices(Choices(voices.Ok ? voices.Value : Array.Empty<VoiceConversionVoice>()));
            _voices.SetValueWithoutNotify(_voice.VoiceId ?? string.Empty);
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
            _pitch.text = FormatPitch(_voice.UserPitchHz, _voice.VoicePitchHz, _voice.AutoSemitones);
            _progress.Progress = _voice.LoadProgress;
            (float position, float length) = _voice.FileTime;
            _file.text = FormatFile(_voice.FileName, position, length);
            bool switching = _voice.IsSwitching;
            if (switching != _switching)
            {
                // Only on a change, so a message such as an unreadable file stays until the state moves on.
                _switching = switching;
                _stateText.Key = switching ? VoiceConversionStrings.Switching : StateKey(_state);
            }

            if (_voice.VoiceId != null && _voices.value != _voice.VoiceId)
            {
                // A voice that could not load leaves the one in use chosen.
                _voices.SetValueWithoutNotify(_voice.VoiceId);
            }
        }

        private void OnState(VoiceConversionState state)
        {
            _state = state;
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
                Filters = new[] { new FileFilter(localizer?.Get(VoiceConversionStrings.FileFilter) ?? "Audio", _voice.FileExtensions) },
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
