using System;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.VoiceLab;

namespace VirtualVessel.UI.Voice
{
    /// <summary>String table keys of a voice's finish (Voice detailed design 8.6).</summary>
    [StringKeys]
    public static class VoiceToneStrings
    {
        public const string Title = "voice.tone.title";
        public const string Note = "voice.tone.note";
        public const string Body = "voice.tone.body";
        public const string BodyHelp = "voice.tone.body.help";
        public const string BodyLow = "voice.tone.body.low";
        public const string BodyHigh = "voice.tone.body.high";
        public const string Clarity = "voice.tone.clarity";
        public const string ClarityHelp = "voice.tone.clarity.help";
        public const string ClarityLow = "voice.tone.clarity.low";
        public const string ClarityHigh = "voice.tone.clarity.high";
        public const string Brightness = "voice.tone.brightness";
        public const string BrightnessHelp = "voice.tone.brightness.help";
        public const string BrightnessLow = "voice.tone.brightness.low";
        public const string BrightnessHigh = "voice.tone.brightness.high";
        public const string Air = "voice.tone.air";
        public const string AirHelp = "voice.tone.air.help";
        public const string AirLow = "voice.tone.air.low";
        public const string AirHigh = "voice.tone.air.high";
        public const string Reset = "voice.tone.reset";
    }

    /// <summary>
    /// The four knobs of a voice's finish, with what each end means and Reset (the user's decision, 2026-11-15): the same
    /// in Voice Lab's STEP 4 and in Setup's voice card. It only shows and reports a tone; who keeps it is the caller's.
    /// </summary>
    public sealed class VoiceToneKnobs : VisualElement
    {
        public VoiceToneKnobs()
        {
            Add(Ui.Text(VoiceToneStrings.Title, "vv-text-body", "vv-bold", VoiceLabScreen.SubheadClass, VoiceLabScreen.GapClass));
            Add(Ui.Text(VoiceToneStrings.Note, VoiceLabScreen.NoteClass, "vv-text-body"));
            BodyKnob = Knob(VoiceToneStrings.Body, VoiceToneStrings.BodyHelp, VoiceToneStrings.BodyLow, VoiceToneStrings.BodyHigh);
            ClarityKnob = Knob(VoiceToneStrings.Clarity, VoiceToneStrings.ClarityHelp, VoiceToneStrings.ClarityLow, VoiceToneStrings.ClarityHigh);
            BrightnessKnob = Knob(VoiceToneStrings.Brightness, VoiceToneStrings.BrightnessHelp, VoiceToneStrings.BrightnessLow, VoiceToneStrings.BrightnessHigh);
            AirKnob = Knob(VoiceToneStrings.Air, VoiceToneStrings.AirHelp, VoiceToneStrings.AirLow, VoiceToneStrings.AirHigh);
            ResetButton = Ui.Button(VoiceToneStrings.Reset);
            ResetButton.clicked += () =>
            {
                SetTone(default);
                Changed?.Invoke(Tone);
            };
            Add(VoiceLabScreen.Actions(ResetButton));
        }

        /// <summary>A knob moved, or Reset was pressed: the tone shown now.</summary>
        public event Action<VoiceTone> Changed;

        public ValueSlider BodyKnob { get; }

        public ValueSlider ClarityKnob { get; }

        public ValueSlider BrightnessKnob { get; }

        public ValueSlider AirKnob { get; }

        public ActionButton ResetButton { get; }

        public VoiceTone Tone => new VoiceTone(BodyKnob.value, ClarityKnob.value, BrightnessKnob.value, AirKnob.value);

        /// <summary>Shows <paramref name="tone"/> without reporting it, as when a voice is opened.</summary>
        public void SetTone(VoiceTone tone)
        {
            BodyKnob.SetValueWithoutNotify(tone.Body);
            ClarityKnob.SetValueWithoutNotify(tone.Clarity);
            BrightnessKnob.SetValueWithoutNotify(tone.Brightness);
            AirKnob.SetValueWithoutNotify(tone.Air);
        }

        private ValueSlider Knob(string nameKey, string helpKey, string lowKey, string highKey)
        {
            ValueSlider knob = Ui.Slider(nameKey, -VoiceTone.Limit, VoiceTone.Limit, "dB");
            knob.HelpKey = helpKey;
            knob.Step = 1f;
            knob.Signed = true;
            knob.RegisterValueChangedCallback(_ => Changed?.Invoke(Tone));
            Add(VoiceLabTimbreStep.WithEnds(knob, lowKey, highKey));
            return knob;
        }
    }
}
