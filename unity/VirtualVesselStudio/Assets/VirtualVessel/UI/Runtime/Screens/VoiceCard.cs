using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine.UIElements;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Voice;

namespace VirtualVessel.UI.Screens
{
    [StringKeys]
    public static class VoiceCardStrings
    {
        public const string Title = "setup.voice.title";
        public const string StateRunning = "setup.voice.state.running";
        public const string StateLoading = "setup.voice.state.loading";
        public const string StateOff = "setup.voice.state.off";
        public const string StateUnavailable = "setup.voice.state.unavailable";
        public const string StateStopped = "setup.voice.state.stopped";
        public const string NoVoice = "setup.voice.no-voice";
        public const string NoVoiceBody = "setup.voice.no-voice.body";
        public const string OpenLab = "setup.voice.open-lab";
        public const string NoGpu = "setup.voice.no-gpu";
        public const string NoGpuBody = "setup.voice.no-gpu.body";
        public const string Failed = "setup.voice.failed";
        public const string FailedBody = "setup.voice.failed.body";
        public const string FailedBodyPassThrough = "setup.voice.failed.body-pass-through";
        public const string Enabled = "setup.voice.enabled";
        public const string EnabledBody = "setup.voice.enabled.body";
        public const string Voice = "setup.voice.voice";
        public const string VoiceBody = "setup.voice.voice.body";
        public const string AutoPitch = "setup.voice.auto-pitch";
        public const string AutoPitchBody = "setup.voice.auto-pitch.body";
        public const string PitchMatch = "setup.voice.pitch-match";
        public const string Unmeasured = "setup.voice.unmeasured";
        public const string Remeasure = "setup.voice.remeasure";
        public const string Adjust = "setup.voice.adjust";
        public const string AdjustBodyAuto = "setup.voice.adjust.body-auto";
        public const string AdjustBodyManual = "setup.voice.adjust.body-manual";
        public const string Final = "setup.voice.final";
        public const string Gpu = "setup.voice.gpu";
    }

    /// <summary>
    /// The voice conversion on the setup screen's audio page (setup screen 13): on or off, the voice for the stream, its
    /// pitch, and noise removal. The voice and pitch are the project's Voice Profile, noise removal and the user's
    /// measured pitch this PC's (Voice detailed design 6.2, 6.3).
    /// </summary>
    public sealed class VoiceCard : VisualElement
    {
        public const string UssClass = "vv-voice-settings";
        public const string NoteClass = UssClass + "__note";

        private const long RefreshMs = 300;
        private const float SemitoneRange = 24f;

        private readonly IVoiceConversion _voice;
        private readonly VisualElement _notice;
        private readonly StatusChip _noticeChip;
        private readonly KeyedText _noticeBody;
        private readonly ActionButton _openLab;
        private readonly Label _pitchLine;
        private readonly Label _finalLine;
        private readonly List<VisualElement> _rows = new List<VisualElement>();
        private readonly SettingRow _enabledRow;
        private IDisposable _stateSubscription;
        private IVisualElementScheduledItem _refresh;
        private int _voiceCount;

        public VoiceCard(IVoiceConversion voice)
        {
            _voice = voice ?? throw new ArgumentNullException(nameof(voice));
            AddToClassList(UssClass);
            Card card = Ui.Card(VoiceCardStrings.Title, alternate: true);
            Add(card);
            Status = Ui.Chip(StatusKind.Idle, VoiceCardStrings.StateOff);
            card.HeaderEnd.Add(Status);

            _notice = new VisualElement();
            _notice.AddToClassList(NoteClass);
            _noticeChip = Ui.Chip(StatusKind.Idle, VoiceCardStrings.NoVoice);
            _notice.Add(_noticeChip);
            Label body = Text();
            _noticeBody = new KeyedText(body, value => body.text = value);
            _notice.Add(body);
            _openLab = Ui.Button(VoiceCardStrings.OpenLab);
            _openLab.clicked += () => GetFirstAncestorOfType<AppShell>()?.Open("lab");
            _notice.Add(_openLab);
            card.Add(_notice);

            EnabledToggle = Ui.Toggle(null);
            EnabledToggle.RegisterValueChangedCallback(evt => _voice.SetEnabled(evt.newValue));
            _enabledRow = Row(VoiceCardStrings.Enabled, VoiceCardStrings.EnabledBody, EnabledToggle);
            _enabledRow.HelpKey = VoiceCardStrings.Gpu;
            card.Add(_enabledRow);

            VoiceChoice = Ui.Select(null);
            VoiceChoice.RegisterValueChangedCallback(evt =>
            {
                if (!string.IsNullOrEmpty(evt.newValue))
                {
                    _voice.SelectVoice(evt.newValue);
                    ToneKnobs.SetTone(_voice.Tone);
                }
            });
            card.Add(Row(VoiceCardStrings.Voice, VoiceCardStrings.VoiceBody, VoiceChoice));

            AutoPitchToggle = Ui.Toggle(null);
            AutoPitchToggle.RegisterValueChangedCallback(evt =>
            {
                _voice.AutoPitch = evt.newValue;
                Refresh();
            });
            ActionButton remeasure = Ui.Button(VoiceCardStrings.Remeasure);
            remeasure.clicked += () =>
            {
                _voice.RemeasurePitch();
                Refresh();
            };
            var autoControls = new VisualElement();
            autoControls.AddToClassList(SettingRow.ControlsClass);
            autoControls.Add(remeasure);
            autoControls.Add(AutoPitchToggle);
            SettingRow auto = Row(VoiceCardStrings.AutoPitch, VoiceCardStrings.AutoPitchBody, autoControls);
            _pitchLine = Text();
            auto.TextArea.Add(_pitchLine);
            card.Add(auto);

            Adjustment = Ui.Slider(null, -SemitoneRange, SemitoneRange);
            Adjustment.Step = 1f;
            Adjustment.Signed = true;
            Adjustment.RegisterValueChangedCallback(evt =>
            {
                _voice.Semitones = evt.newValue;
                Refresh();
            });
            SettingRow adjust = Row(VoiceCardStrings.Adjust, VoiceCardStrings.AdjustBodyAuto, Adjustment);
            _finalLine = Text();
            adjust.TextArea.Add(_finalLine);
            AdjustRow = adjust;
            card.Add(adjust);

            // The chosen voice's finish, to fix after it was made (the user's decision, 2026-11-15).
            ToneKnobs = new VoiceToneKnobs();
            ToneKnobs.Changed += tone =>
            {
                CrudResult kept = _voice.SetTone(tone);
                if (!kept.Ok)
                {
                    UiServices.Find(this)?.Log?.Warning($"Keeping the voice's finish failed ({kept.Error}): {kept.Detail}");
                }
            };
            card.Add(ToneKnobs);

            RegisterCallback<AttachToPanelEvent>(_ => OnAttached());
            RegisterCallback<DetachFromPanelEvent>(_ => OnDetached());
        }

        public StatusChip Status { get; }

        public SwitchToggle EnabledToggle { get; }

        public SelectField VoiceChoice { get; }

        public SwitchToggle AutoPitchToggle { get; }

        public ValueSlider Adjustment { get; }

        public SettingRow AdjustRow { get; }

        /// <summary>The chosen voice's finish.</summary>
        public VoiceToneKnobs ToneKnobs { get; }

        /// <summary>The header's state: what the conversion does, or why it cannot run. Pure.</summary>
        public static (StatusKind Kind, string Key) StatusOf(VoiceConversionState state, bool gpu, bool hasVoice)
        {
            if (!gpu || !hasVoice)
            {
                return (StatusKind.Error, VoiceCardStrings.StateUnavailable);
            }

            switch (state)
            {
                case VoiceConversionState.Running:
                case VoiceConversionState.NoMicrophone:
                case VoiceConversionState.PlayingFile:
                    return (StatusKind.Success, VoiceCardStrings.StateRunning);
                case VoiceConversionState.Loading:
                    return (StatusKind.Idle, VoiceCardStrings.StateLoading);
                case VoiceConversionState.Failed:
                case VoiceConversionState.Unavailable:
                    return (StatusKind.Error, VoiceCardStrings.StateStopped);
                default:
                    return (StatusKind.Idle, VoiceCardStrings.StateOff);
            }
        }

        /// <summary>
        /// Whether the toggle shows on: converting or getting ready, and also stopped while the microphone is kept out,
        /// so turning it off is what lets one's own voice through. Pure.
        /// </summary>
        public static bool ShowsOn(VoiceConversionState state, bool muting)
        {
            if (state == VoiceConversionState.Off)
            {
                return false;
            }

            return muting || (state != VoiceConversionState.Failed && state != VoiceConversionState.Unavailable);
        }

        /// <summary>The voices a user chooses from: the registered ones; the prototype's is for developers only. Pure.</summary>
        public static IReadOnlyList<SelectChoice> Choices(IReadOnlyList<VoiceConversionVoice> voices)
        {
            return voices.Where(v => !string.IsNullOrEmpty(v.Id) && v.Usable).Select(v => SelectChoice.FromText(v.Id, v.Name)).ToArray();
        }

        /// <summary>The pitch the user is converted to: their own, moved by the automatic shift when on and the adjustment. 0 when unknown. Pure.</summary>
        public static float FinalHz(float userHz, bool auto, float autoSemitones, float adjustment)
        {
            return userHz > 0f ? userHz * (float)Math.Pow(2.0, ((auto ? autoSemitones : 0f) + adjustment) / 12.0) : 0f;
        }

        /// <summary>A pitch as the lines show it, or null when unknown. Pure.</summary>
        public static string Hz(float hz)
        {
            return hz > 0f ? string.Format(CultureInfo.InvariantCulture, "{0:F0} Hz", hz) : null;
        }

        /// <summary>Semitones with their sign. Pure.</summary>
        public static string Shift(float semitones)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:+0;-0;0}", semitones);
        }

        private static Label Text()
        {
            Label label = QuietText.Of(new Label());
            label.AddToClassList(NoteClass);
            label.AddToClassList("vv-text-body");
            return label;
        }

        private SettingRow Row(string nameKey, string descriptionKey, VisualElement control)
        {
            SettingRow row = Ui.SettingRow(nameKey, descriptionKey);
            row.Add(control);
            _rows.Add(row);
            return row;
        }

        private void OnAttached()
        {
            CrudResult<IReadOnlyList<VoiceConversionVoice>> voices = _voice.ListVoices();
            IReadOnlyList<SelectChoice> choices = Choices(voices.Ok ? voices.Value : Array.Empty<VoiceConversionVoice>());
            _voiceCount = choices.Count;
            VoiceChoice.SetChoices(choices);
            VoiceChoice.SetValueWithoutNotify(_voice.InUseVoiceId ?? _voice.VoiceId ?? string.Empty);
            AutoPitchToggle.SetValueWithoutNotify(_voice.AutoPitch);
            Adjustment.SetValueWithoutNotify(_voice.Semitones);
            ToneKnobs.SetTone(_voice.Tone);
            ToneKnobs.SetEnabled(_voiceCount > 0);
            _stateSubscription = _voice.State.Subscribe(_ => Refresh());
            _refresh = schedule.Execute(Refresh).Every(RefreshMs);
            Refresh();
        }

        private void OnDetached()
        {
            _stateSubscription?.Dispose();
            _stateSubscription = null;
            _refresh?.Pause();
            _refresh = null;
        }

        private void Refresh()
        {
            bool gpu = _voice.GpuAvailable;
            bool hasVoice = _voiceCount > 0;
            VoiceConversionState state = _voice.State.Value;
            (StatusKind kind, string key) = StatusOf(state, gpu, hasVoice);
            Status.Status = kind;
            Status.TextKey = key;

            bool muting = _voice.MutesMicrophone;
            bool failed = state == VoiceConversionState.Failed;

            // Kept out of the stream, the microphone's silence is said first: it is what the stream gets.
            bool mutedStop = muting && (failed || state == VoiceConversionState.Unavailable);
            _notice.style.display = !gpu || !hasVoice || failed || mutedStop ? DisplayStyle.Flex : DisplayStyle.None;
            _openLab.style.display = gpu && !hasVoice ? DisplayStyle.Flex : DisplayStyle.None;
            if (mutedStop)
            {
                _noticeChip.Status = StatusKind.Error;
                _noticeChip.TextKey = VoiceCardStrings.Failed;
                _noticeBody.Key = VoiceCardStrings.FailedBody;
            }
            else if (!gpu)
            {
                _noticeChip.Status = StatusKind.Error;
                _noticeChip.TextKey = VoiceCardStrings.NoGpu;
                _noticeBody.Key = VoiceCardStrings.NoGpuBody;
            }
            else if (!hasVoice)
            {
                _noticeChip.Status = StatusKind.Idle;
                _noticeChip.TextKey = VoiceCardStrings.NoVoice;
                _noticeBody.Key = VoiceCardStrings.NoVoiceBody;
            }
            else if (failed)
            {
                _noticeChip.Status = StatusKind.Error;
                _noticeChip.TextKey = VoiceCardStrings.Failed;
                _noticeBody.Key = VoiceCardStrings.FailedBodyPassThrough;
            }

            // The toggle stays usable while muted, also without a voice or a GPU, so it can always be turned off.
            bool usable = gpu && hasVoice;
            foreach (VisualElement row in _rows)
            {
                row.SetEnabled(usable || (row == _enabledRow && muting));
            }

            EnabledToggle.SetValueWithoutNotify(ShowsOn(state, muting));
            AdjustRow.DescriptionKey = _voice.AutoPitch ? VoiceCardStrings.AdjustBodyAuto : VoiceCardStrings.AdjustBodyManual;

            ILocalizer localizer = UiServices.Find(this)?.Localizer;
            if (localizer == null)
            {
                return;
            }

            string unknown = localizer.Get(VoiceCardStrings.Unmeasured);
            float shift = _voice.AutoPitch ? _voice.AutoSemitones : 0f;
            _pitchLine.text = localizer.Get(VoiceCardStrings.PitchMatch, ("user", Hz(_voice.UserPitchHz) ?? unknown), ("voice", Hz(_voice.VoicePitchHz) ?? unknown), ("shift", Shift(shift)));
            _finalLine.text = localizer.Get(VoiceCardStrings.Final, ("hz", Hz(FinalHz(_voice.UserPitchHz, _voice.AutoPitch, _voice.AutoSemitones, _voice.Semitones)) ?? unknown), ("total", Shift(shift + _voice.Semitones)));
        }
    }
}
