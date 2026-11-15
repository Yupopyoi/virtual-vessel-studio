using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using VirtualVessel.Audio;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Screens;
using VirtualVessel.UI.Voice;
using VirtualVessel.UI.Binding;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// STEP 4, try it with your own voice (system design 14.17; Voice Lab detailed design 8.3.4): a recording converted
    /// as the stream would and played to the monitor only, or the microphone through the stream's own conversion, with
    /// the pitch matched to the user's. Leaving the step stops what plays and puts the voice conversion back as it was.
    /// </summary>
    public sealed class VoiceLabTryStep : VisualElement
    {
        public const int FileSource = 0;
        public const int MicrophoneSource = 1;

        private const long RefreshMs = 300;
        private const float SemitoneRange = 24f;

        private readonly IVoiceLab _lab;
        private readonly VoiceLabDraft _draft;
        private readonly VoiceLabPlayer _player;
        private readonly IVoiceTrial _trial;
        private readonly IVoiceConversion _voice;
        private readonly IFileDialog _files;
        private readonly IAudioService _audio;
        private readonly VisualElement _filePanel;
        private readonly VisualElement _micPanel;
        private readonly VisualElement _convertPanel;
        private readonly VisualElement _stopConverting;
        private readonly Label _fileName;
        private readonly ProgressMeter _converting;
        private readonly StatusChip _problem;
        private readonly VisualElement _hearOff;
        private readonly Label _micState;
        private readonly Label _pitchLine;
        private string _folder;
        private string _recording;
        private string _converted;
        private string _convertedKey;
        private float _userHz;
        private float _voiceHz;
        private float _shift;
        private CancellationTokenSource _conversion;
        private IVisualElementScheduledItem _refresh;
        private bool _micOn;
        private bool _shown;

        public VoiceLabTryStep(ScreenServices services, IVoiceLab lab, VoiceLabDraft draft, VoiceLabPlayer player, IVoiceLabFlow flow)
        {
            _lab = lab ?? throw new ArgumentNullException(nameof(lab));
            _draft = draft ?? throw new ArgumentNullException(nameof(draft));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _trial = services?.VoiceTrial;
            _voice = services?.Voice;
            _files = services?.FileDialog;
            _audio = services?.Audio;
            Add(VoiceLabScreen.Lead(VoiceLabStrings.TryLead));

            Card source = Ui.Card(VoiceLabStrings.TrySource);
            Sources = Ui.Segmented(VoiceLabStrings.TryFile, VoiceLabStrings.TryMicrophone);
            Sources.RegisterValueChangedCallback(evt => ShowSource(evt.newValue));
            source.Add(Sources);

            _filePanel = new VisualElement();
            _fileName = PlainText();
            PanelLifetime.OnLocaleChanged(this, () =>
            {
                ShowFile();
                Refresh();
            });
            PickButton = Ui.Button(VoiceLabStrings.TryPick);
            PickButton.clicked += PickRecording;
            _filePanel.Add(VoiceLabScreen.Actions(_fileName, PickButton));
            OriginalButton = Ui.Button(VoiceLabStrings.TryOriginal);
            OriginalButton.clicked += () => _ = _player.PlayAsync(_recording, null);
            _filePanel.Add(VoiceLabScreen.Actions(OriginalButton));
            source.Add(_filePanel);

            _micPanel = new VisualElement();
            _hearOff = new VisualElement();
            _hearOff.Add(Ui.Chip(StatusKind.Warning, VoiceLabStrings.TryHearOff));
            _hearOff.Add(Ui.Text(VoiceLabStrings.TryHearOffBody, VoiceLabScreen.NoteClass, "vv-text-body"));
            HearButton = Ui.Button(VoiceLabStrings.TryHearOn);
            HearButton.clicked += TurnHearOn;
            _hearOff.Add(VoiceLabScreen.Actions(HearButton));
            _micPanel.Add(_hearOff);
            MicToggle = Ui.Toggle(VoiceLabStrings.TryMicToggle);
            MicToggle.RegisterValueChangedCallback(evt => SetMicrophone(evt.newValue));
            _micPanel.Add(MicToggle);
            _micPanel.Add(Ui.Text(VoiceLabStrings.TryHeadphones, VoiceLabScreen.NoteClass, "vv-text-body"));
            _micState = PlainText();
            _micPanel.Add(_micState);
            source.Add(_micPanel);

            // The pitch inside the voice to try, as it shapes both the file and the microphone (the user's review,
            // 2026-11-15); Converted comes after it, last, as it converts with it.
            Label pitchHeading = Ui.Text(VoiceLabStrings.TryPitch, "vv-text-body", "vv-bold", VoiceLabScreen.SubheadClass, VoiceLabScreen.GapClass);
            source.Add(pitchHeading);
            AutoPitch = Ui.Toggle(VoiceLabStrings.TryAutoPitch);
            AutoPitch.SetValueWithoutNotify(true);
            AutoPitch.RegisterValueChangedCallback(_ => PitchChanged());
            source.Add(AutoPitch);
            _pitchLine = PlainText();
            ActionButton remeasure = Ui.Button(VoiceLabStrings.TryRemeasure);
            remeasure.clicked += Remeasure;
            source.Add(VoiceLabScreen.Actions(_pitchLine, remeasure));
            Adjustment = Ui.Slider(VoiceLabStrings.TryAdjust, -SemitoneRange, SemitoneRange);
            Adjustment.Step = 1f;
            Adjustment.Signed = true;
            Adjustment.RegisterValueChangedCallback(_ => PitchChanged());
            source.Add(Adjustment);

            // The finish after the pitch, before Converted, which plays with it (the user's decision, 2026-11-15).
            ToneKnobs = new VoiceToneKnobs();
            ToneKnobs.Changed += OnToneChanged;
            source.Add(ToneKnobs);

            _convertPanel = new VisualElement();
            _convertPanel.AddToClassList(VoiceLabScreen.GapClass);
            ConvertedButton = Ui.Button(VoiceLabStrings.TryConverted, ButtonVariant.Primary);
            ConvertedButton.clicked += () => _ = PlayConvertedAsync();

            // Stopping what plays and stopping a conversion are two buttons that say which: one Stop did both, and
            // what it stopped was not clear (the user's review, 2026-11-15).
            StopPlayingButton = Ui.Button(VoiceLabStrings.TryStopPlaying);
            StopPlayingButton.clicked += () => _player.Stop();
            _convertPanel.Add(VoiceLabScreen.Actions(ConvertedButton, StopPlayingButton));
            _converting = Ui.Progress(VoiceLabStrings.TryConverting);
            _converting.AddToClassList(VoiceLabScreen.GapClass);
            _convertPanel.Add(_converting);
            StopConvertingButton = Ui.Button(VoiceLabStrings.TryStopConverting);
            StopConvertingButton.clicked += StopConverting;
            _stopConverting = VoiceLabScreen.Actions(StopConvertingButton);
            _convertPanel.Add(_stopConverting);
            source.Add(_convertPanel);

            _problem = new StatusChip(StatusKind.Error, VoiceLabStrings.TryFailed);
            _problem.AddToClassList(VoiceLabScreen.GapClass);
            source.Add(_problem);
            Add(source);

            ActionButton back = Ui.Button(VoiceLabStrings.BackToTimbre);
            back.clicked += () => flow.GoTo(VoiceLabScreen.TimbreStep);
            ActionButton next = Ui.Button(VoiceLabStrings.ToRegister, ButtonVariant.Primary);
            next.clicked += () => flow.GoTo(VoiceLabScreen.RegisterStep);
            Add(VoiceLabScreen.Actions(back, next));

            Sources.SetValueWithoutNotify(FileSource);
            ShowSource(FileSource);
            ShowConverting(false);
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                Leave();
                StopConverting();
            });
        }

        public Segmented Sources { get; }

        public ActionButton PickButton { get; }

        public ActionButton OriginalButton { get; }

        public ActionButton ConvertedButton { get; }

        /// <summary>Stops what plays: the original or the converted voice.</summary>
        public ActionButton StopPlayingButton { get; }

        /// <summary>Stops the conversion; shown only while converting.</summary>
        public ActionButton StopConvertingButton { get; }

        public ActionButton HearButton { get; }

        public SwitchToggle MicToggle { get; }

        public SwitchToggle AutoPitch { get; }

        public ValueSlider Adjustment { get; }

        /// <summary>The finish on the voice tried, kept with its run or voice.</summary>
        public VoiceToneKnobs ToneKnobs { get; }

        /// <summary>The recording to try; set by the file dialog, or by tests.</summary>
        public string Recording
        {
            get => _recording;
            set
            {
                _recording = value;
                _fileName.text = value == null ? string.Empty : Path.GetFileName(value);
                ShowFile();
            }
        }

        /// <summary>The voice to try: the completed build's. Called when the step opens.</summary>
        public void Enter()
        {
            CrudResult<string> folder = _draft.TryFolder != null
                ? CrudResult<string>.Success(_draft.TryFolder)
                : _draft.RunId == null ? CrudResult<string>.Fail(CrudError.NotFound, "No build.") : _lab.BuiltVoiceFolder(_draft.RunId);
            if (folder.Value != _folder)
            {
                _converted = null;
                _convertedKey = null;
            }

            if (folder.Value != _folder)
            {
                // A conversion for another voice is of no use now.
                StopConverting();
            }

            _folder = folder.Ok ? folder.Value : null;
            _shown = true;
            ToneKnobs.SetTone(_lab.ToneOf(_draft.RunId, _draft.TryFolder));
            _problem.style.display = DisplayStyle.None;
            _refresh ??= schedule.Execute(Refresh).Every(RefreshMs);
            _refresh.Resume();
            ShowFile();
            Refresh();
        }

        /// <summary>
        /// Stops what plays and puts the voice conversion back; called when the step closes or the screen is left. A
        /// file being converted goes on: leaving cancelled it, and it could not start again (the user's report,
        /// 2026-11-15). It is kept to play from here, not played over another step or screen.
        /// </summary>
        public void Leave()
        {
            _shown = false;
            _player.Stop();
            _refresh?.Pause();
            MicToggle.SetValueWithoutNotify(false);
            EndMicrophone();
        }

        /// <summary>Cancels a conversion, as when a stream or a recording starts (system design 14.26).</summary>
        public void StopConverting()
        {
            _conversion?.Cancel();
        }

        /// <summary>What identifies a conversion, so the same one is played again without converting again. Pure.</summary>
        public static string ConversionKey(string folder, string recording, bool auto, float adjustment, float userHz)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}", folder, recording, auto, adjustment, userHz);
        }

        /// <summary>A pitch in Hz as the line shows it, or null when unknown. Pure.</summary>
        public static string Hz(float hz)
        {
            return hz > 0f ? string.Format(CultureInfo.InvariantCulture, "{0:F0} Hz", hz) : null;
        }

        /// <summary>A shift in semitones with its sign. Pure.</summary>
        public static string Shift(float semitones)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:+0;-0;0}", semitones);
        }

        private bool OnMicrophone => Sources.value == MicrophoneSource;

        // Text this step sets itself, such as a file's name; a keyed label would put its key's text back on a language change.
        private static Label PlainText()
        {
            Label label = QuietText.Of(new Label());
            label.AddToClassList(VoiceLabScreen.NoteClass);
            label.AddToClassList("vv-text-body");
            return label;
        }

        private void ShowSource(int source)
        {
            _player.Stop();
            if (source != MicrophoneSource)
            {
                MicToggle.SetValueWithoutNotify(false);
                EndMicrophone();
            }

            _filePanel.style.display = source == FileSource ? DisplayStyle.Flex : DisplayStyle.None;
            _convertPanel.style.display = source == FileSource ? DisplayStyle.Flex : DisplayStyle.None;
            _micPanel.style.display = source == MicrophoneSource ? DisplayStyle.Flex : DisplayStyle.None;
            Refresh();
        }

        private void ShowFile()
        {
            bool idle = _conversion == null;
            PickButton.SetEnabled(idle && _files != null);
            OriginalButton.SetEnabled(idle && _recording != null && _player.Available);
            ConvertedButton.SetEnabled(idle && _recording != null && _folder != null && _trial != null && _player.Available);
            if (_recording == null)
            {
                _fileName.text = UiServices.Find(this)?.Localizer?.Get(VoiceLabStrings.TryNoFile) ?? string.Empty;
            }
        }

        private void ShowConverting(bool converting)
        {
            _converting.style.display = converting ? DisplayStyle.Flex : DisplayStyle.None;
            _stopConverting.style.display = converting ? DisplayStyle.Flex : DisplayStyle.None;
            if (converting)
            {
                _converting.Progress = 0f;
                _converting.Detail = VoiceLabView.PercentText(0f);
            }

            ShowFile();
        }

        private async void PickRecording()
        {
            var localizer = UiServices.Find(this)?.Localizer;
            string path = await _files.OpenAsync(new FileDialogOptions
            {
                Filters = new[] { new FileFilter(localizer?.Get(VoiceLabStrings.TryFileFilter) ?? "Audio", _voice?.FileExtensions ?? new[] { "wav", "m4a", "mp3" }) },
            });
            if (path != null)
            {
                Recording = path;
            }
        }

        private async Task PlayConvertedAsync()
        {
            if (_conversion != null || _recording == null || _folder == null || _trial == null)
            {
                return;
            }

            string key = ConversionKey(_folder, _recording, AutoPitch.value, Adjustment.value, _userHz);
            if (key != _convertedKey || _converted == null)
            {
                _player.Stop();
                _problem.style.display = DisplayStyle.None;
                _conversion = new CancellationTokenSource();
                ShowConverting(true);
                try
                {
                    var progress = new Progress<float>(p =>
                    {
                        _converting.Progress = p;
                        _converting.Detail = VoiceLabView.PercentText(p);
                    });
                    VoiceTrialResult result = await _trial.ConvertAsync(new VoiceTrialRequest(_folder, _recording, AutoPitch.value, Adjustment.value, _userHz), progress, _conversion.Token);
                    _converted = result.Converted;
                    _userHz = result.UserHz;
                    _voiceHz = result.VoiceHz;
                    _shift = result.Semitones;
                    _convertedKey = ConversionKey(_folder, _recording, AutoPitch.value, Adjustment.value, _userHz);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (VoiceTrialFailedException)
                {
                    // The service logged why.
                    _problem.style.display = DisplayStyle.Flex;
                    return;
                }
                catch (InvalidOperationException exception)
                {
                    // A trial already converting: the user is told it did not work, not left with nothing.
                    _problem.style.display = DisplayStyle.Flex;
                    UiServices.Find(this)?.Log?.Warning($"A trial conversion could not start: {exception.Message}");
                    return;
                }
                finally
                {
                    _conversion.Dispose();
                    _conversion = null;
                    ShowConverting(false);
                }
            }

            Refresh();
            if (!_shown)
            {
                return;
            }

            // The finish on the kept conversion: changing it needs no new conversion.
            string finished;
            try
            {
                finished = await _trial.FinishAsync(_converted, ToneKnobs.Tone, CancellationToken.None);
            }
            catch (VoiceTrialFailedException)
            {
                // The service logged why.
                _problem.style.display = DisplayStyle.Flex;
                return;
            }

            if (_shown)
            {
                await _player.PlayAsync(finished, null);
            }
        }

        // Kept at once, with the run (and the voice when one is tried): it is what the voice will sound like.
        private void OnToneChanged(VoiceTone tone)
        {
            CrudResult kept = _lab.KeepTone(_draft.RunId, _draft.TryFolder, tone);
            if (!kept.Ok)
            {
                UiServices.Find(this)?.Log?.Warning($"Keeping the finish failed ({kept.Error}): {kept.Detail}");
            }

            if (OnMicrophone && _micOn && _voice != null)
            {
                _voice.SetTone(tone);
            }
        }

        private void PitchChanged()
        {
            if (OnMicrophone && _micOn && _voice != null)
            {
                _voice.AutoPitch = AutoPitch.value;
                _voice.Semitones = Adjustment.value;
            }

            Refresh();
        }

        private void Remeasure()
        {
            _userHz = 0f;
            _convertedKey = null;
            if (OnMicrophone)
            {
                _voice?.RemeasurePitch();
            }

            Refresh();
        }

        private IAudioSource Microphone => _audio?.Sources.FirstOrDefault(s => s.Role == AudioSourceRole.Microphone);

        private void TurnHearOn()
        {
            IAudioSource microphone = Microphone;
            microphone?.SetOptions(microphone.Options.Value.WithMonitored(true));
            Refresh();
        }

        private void SetMicrophone(bool on)
        {
            if (!on)
            {
                EndMicrophone();
                Refresh();
                return;
            }

            if (_voice == null || _folder == null)
            {
                MicToggle.SetValueWithoutNotify(false);
                _problem.style.display = DisplayStyle.Flex;
                return;
            }

            _player.Stop();
            _micOn = true;
            _voice.BeginTrial(_folder);
            _voice.AutoPitch = AutoPitch.value;
            _voice.Semitones = Adjustment.value;
            _voice.SetTone(ToneKnobs.Tone);
            Refresh();
        }

        // The voice service puts its saved voice, pitch, and on or off back itself.
        private void EndMicrophone()
        {
            if (!_micOn || _voice == null)
            {
                return;
            }

            _micOn = false;
            _voice.EndTrial();
        }

        private void Refresh()
        {
            bool mic = OnMicrophone && _voice != null;
            IAudioSource microphone = Microphone;
            _hearOff.style.display = OnMicrophone && microphone != null && !microphone.Options.Value.Monitored ? DisplayStyle.Flex : DisplayStyle.None;
            MicToggle.SetEnabled(_voice != null && _folder != null);
            if (mic && _micOn)
            {
                _micState.text = UiServices.Find(this)?.Localizer?.Get(_voice.State.Value == VoiceConversionState.Loading || _voice.IsSwitching ? VoiceLabStrings.TryMicLoading : VoiceLabStrings.TryMicOn) ?? string.Empty;
            }
            else
            {
                _micState.text = UiServices.Find(this)?.Localizer?.Get(VoiceLabStrings.TryMicOff) ?? string.Empty;
            }

            float user = mic && _micOn ? _voice.UserPitchHz : _userHz;
            float voice = mic && _micOn ? _voice.VoicePitchHz : _voiceHz;
            float shift = mic && _micOn ? _voice.AutoSemitones : _shift;
            var localizer = UiServices.Find(this)?.Localizer;
            if (localizer != null)
            {
                string unknown = localizer.Get(VoiceLabStrings.TryPitchUnknown);
                _pitchLine.text = localizer.Get(VoiceLabStrings.TryPitchMatch, ("user", Hz(user) ?? unknown), ("voice", Hz(voice) ?? unknown), ("shift", Shift(AutoPitch.value ? shift : 0f)));
            }
        }
    }
}
