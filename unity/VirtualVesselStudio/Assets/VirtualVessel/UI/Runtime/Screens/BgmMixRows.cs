using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VirtualVessel.Audio;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of how BGM is mixed (docs/&lt;lang&gt;/ui/screens/setup.md 14.1).</summary>
    [StringKeys]
    public static class BgmMixStrings
    {
        public const string Ducking = "setup.bgm.ducking";
        public const string DuckingHelp = "setup.bgm.ducking.help";
        public const string DuckingNow = "setup.bgm.ducking.now";
        public const string DuckingNotNow = "setup.bgm.ducking.not-now";
        public const string DuckDepth = "setup.bgm.duck-depth";
        public const string DuckDepthHelp = "setup.bgm.duck-depth.help";
        public const string DuckThreshold = "setup.bgm.duck-threshold";
        public const string DuckThresholdHelp = "setup.bgm.duck-threshold.help";
        public const string DuckingOnly = "setup.bgm.ducking-only";
        public const string Crossfade = "setup.bgm.crossfade";
        public const string CrossfadeHelp = "setup.bgm.crossfade.help";
    }

    /// <summary>
    /// How BGM is mixed, under the BGM Card's list (setup.md 14.1; BGM and SE detailed design 8.4, 8.5): lowering it
    /// while the user talks, and how long a switch to another track takes. The values are the audio service's.
    /// </summary>
    public sealed class BgmMixRows : VisualElement
    {
        public const string UssClass = "vv-bgm-mix";

        private readonly IAudioService _audio;

        public BgmMixRows(IAudioService audio)
        {
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            AddToClassList(UssClass);

            DuckingToggle = Ui.Toggle(null);
            DuckingToggle.TooltipKey = BgmMixStrings.Ducking;
            DuckingToggle.RegisterValueChangedCallback(evt => Change(options => options.WithDucking(evt.newValue)));

            // Whether it is lowered now, by shape and text, so the user can set how loud a voice it reacts to.
            DuckingState = Ui.Chip(StatusKind.Idle, BgmMixStrings.DuckingNotNow);
            var ducking = new VisualElement();
            ducking.AddToClassList(UssClass + "__ducking");
            ducking.Add(DuckingState);
            ducking.Add(DuckingToggle);
            Add(Ui.ControlRow(BgmMixStrings.Ducking, ducking, helpKey: BgmMixStrings.DuckingHelp));

            DuckDepth = Slider(BgmMixStrings.DuckDepth, BgmMixStrings.DuckDepthHelp, BgmMixOptions.MinDuckDepthDb, BgmMixOptions.MaxDuckDepthDb, 1f, "dB", value => Change(options => options.WithDuckDepth(value)));
            DuckThreshold = Slider(BgmMixStrings.DuckThreshold, BgmMixStrings.DuckThresholdHelp, BgmMixOptions.MinDuckThresholdDb, BgmMixOptions.MaxDuckThresholdDb, 1f, "dB", value => Change(options => options.WithDuckThreshold(value)));
            Crossfade = Slider(BgmMixStrings.Crossfade, BgmMixStrings.CrossfadeHelp, 0f, BgmMixOptions.MaxCrossfadeSeconds, 0.5f, "s", value => Change(options => options.WithCrossfade(value)));
            Crossfade.Decimals = 1;
            Crossfade.Signed = false;

            PanelLifetime.WhileAttached(this, _ => Attach());
        }

        public SwitchToggle DuckingToggle { get; }

        public StatusChip DuckingState { get; }

        public ValueSlider DuckDepth { get; }

        public ValueSlider DuckThreshold { get; }

        public ValueSlider Crossfade { get; }

        private IDisposable Attach()
        {
            Show(_audio.BgmMix.Value);
            var subscriptions = new List<IDisposable>
            {
                _audio.BgmMix.Subscribe(Show),
                _audio.Ducking.Subscribe(_ => ShowDucking()),
            };
            return new DisposeAll(subscriptions);
        }

        private void Change(Func<BgmMixOptions, BgmMixOptions> change)
        {
            _audio.SetBgmMix(change(_audio.BgmMix.Value));
        }

        private void Show(BgmMixOptions options)
        {
            DuckingToggle.SetValueWithoutNotify(options.Ducking);
            DuckDepth.SetValueWithoutNotify(options.DuckDepthDb);
            DuckThreshold.SetValueWithoutNotify(options.DuckThresholdDb);
            Crossfade.SetValueWithoutNotify(options.CrossfadeSeconds);
            foreach (ValueSlider slider in new[] { DuckDepth, DuckThreshold })
            {
                slider.SetEnabled(options.Ducking);
                slider.TooltipKey = options.Ducking ? null : BgmMixStrings.DuckingOnly;
            }

            ShowDucking();
        }

        private void ShowDucking()
        {
            bool on = _audio.BgmMix.Value.Ducking;
            bool now = on && _audio.Ducking.Value;
            DuckingState.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            DuckingState.Status = now ? StatusKind.Success : StatusKind.Idle;
            DuckingState.TextKey = now ? BgmMixStrings.DuckingNow : BgmMixStrings.DuckingNotNow;
        }

        // A slider with its name above it, as in the lighting Card.
        private ValueSlider Slider(string nameKey, string helpKey, float low, float high, float step, string unit, Action<float> changed)
        {
            ValueSlider slider = Ui.Slider(nameKey, low, high, unit);
            slider.Step = step;
            slider.Signed = unit == "dB";
            slider.HelpKey = helpKey;
            slider.RegisterValueChangedCallback(evt => changed(evt.newValue));
            Add(slider);
            return slider;
        }
    }
}
