using System;
using System.Globalization;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
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

        private const long RefreshMs = 500;
        private const float SemitoneRange = 24f;

        private readonly IVoiceConversion _voice;
        private readonly KeyedText _stateText;
        private readonly Label _stats;
        private readonly SwitchToggle _toggle;
        private IDisposable _stateSubscription;
        private IVisualElementScheduledItem _refresh;

        public VoiceConversionCard(IVoiceConversion voice)
        {
            _voice = voice ?? throw new ArgumentNullException(nameof(voice));
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

            var state = new Label();
            state.AddToClassList("vv-text-body");
            _stateText = new KeyedText(state, value => state.text = value);
            card.Add(state);

            _stats = new Label();
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
                case VoiceConversionState.Unavailable:
                    return VoiceConversionStrings.StateUnavailable;
                case VoiceConversionState.Failed:
                    return VoiceConversionStrings.StateFailed;
                default:
                    return VoiceConversionStrings.StateOff;
            }
        }

        /// <summary>Numbers read the same in every language, so this needs no string table. Pure.</summary>
        public static string FormatStats(VoiceConversionStats stats)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:F0} / {1:F0} ms ({2:F0} ms main)  {3} blocks, {4} dropped", stats.WorkMs, stats.BlockMs, stats.IssueMs, stats.Blocks, stats.Dropped);
        }

        private void OnAttached()
        {
            _stateSubscription = _voice.State.Subscribe(state => _stateText.Key = StateKey(state));
            _refresh = schedule.Execute(() => _stats.text = FormatStats(_voice.Stats)).Every(RefreshMs);
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
