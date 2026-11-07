using System;
using System.Threading;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// STEP 2, shape the timbre (system design 14.15): four knobs that edit the chosen base, heard at once
    /// against the original. There are no intonation or pace knobs; those come from the speaker.
    /// </summary>
    public sealed class VoiceLabTimbreStep : VisualElement
    {
        public const string EndsClass = VoiceLabScreen.UssClass + "__ends";
        public const string SliderClass = VoiceLabScreen.UssClass + "__slider";
        public const string FactsClass = VoiceLabScreen.UssClass + "__facts";
        public const string FarClass = VoiceLabScreen.UssClass + "__far";

        private readonly IVoiceLab _lab;
        private readonly VoiceLabPlayer _player;
        private readonly VoiceLabDraft _draft;
        private readonly IVoiceLabFlow _flow;
        private readonly ValueSlider _pitch;
        private readonly ValueSlider _age;
        private readonly ValueSlider _brightness;
        private readonly ValueSlider _breathiness;
        private readonly ValueSlider _softness;
        private readonly VisualElement _mechanical;
        private readonly Label _base;
        private readonly Label _pitchChange;
        private readonly VisualElement _far;
        private readonly ActionButton _listenBase;
        private readonly ActionButton _listenShaped;
        private readonly Label _shaping;
        private readonly Label _reshaping;
        private readonly StatusChip _failed;
        private readonly ActionButton _hold;
        private readonly VoiceLabHoldList _held;
        private VoiceLabCandidate _heldFor;
        private CancellationTokenSource _shapingSource;
        private VoiceLabCandidate _shapingCandidate;
        private VoiceLabTimbre _shapingTimbre;
        private VoiceLabCandidate _previewCandidate;
        private VoiceLabTimbre _previewTimbre;
        private VoiceLabPreview _preview;

        public VoiceLabTimbreStep(IVoiceLab lab, VoiceLabPlayer player, VoiceLabDraft draft, IVoiceLabFlow flow)
        {
            _lab = lab ?? throw new ArgumentNullException(nameof(lab));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _draft = draft ?? throw new ArgumentNullException(nameof(draft));
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            Add(VoiceLabScreen.Lead(VoiceLabStrings.TimbreLead));

            Card timbre = Ui.Card(VoiceLabStrings.Timbre);
            var facts = new VisualElement();
            facts.AddToClassList(FactsClass);
            _base = QuietText.Of(new Label());
            _base.AddToClassList("vv-text-body");
            _pitchChange = QuietText.Of(new Label());
            _pitchChange.AddToClassList("vv-mono");
            facts.Add(_base);
            facts.Add(_pitchChange);
            timbre.Add(facts);

            _pitch = Knob(timbre, VoiceLabStrings.TimbrePitch, VoiceLabStrings.TimbrePitchTip, VoiceLabStrings.TimbrePitchLow, VoiceLabStrings.TimbrePitchHigh);
            _age = Knob(timbre, VoiceLabStrings.TimbreAge, VoiceLabStrings.TimbreAgeTip, VoiceLabStrings.TimbreAgeLow, VoiceLabStrings.TimbreAgeHigh);
            _brightness = Knob(timbre, VoiceLabStrings.TimbreBrightness, VoiceLabStrings.TimbreBrightnessTip, VoiceLabStrings.TimbreBrightnessLow, VoiceLabStrings.TimbreBrightnessHigh);
            _softness = Knob(timbre, VoiceLabStrings.TimbreSoftness, VoiceLabStrings.TimbreSoftnessTip, VoiceLabStrings.TimbreSoftnessLow, VoiceLabStrings.TimbreSoftnessHigh, low: 0f);
            _breathiness = Knob(timbre, VoiceLabStrings.TimbreBreathiness, VoiceLabStrings.TimbreBreathinessTip, VoiceLabStrings.TimbreBreathinessLow, VoiceLabStrings.TimbreBreathinessHigh);

            _far = new VisualElement();
            _far.AddToClassList(FarClass);
            _far.Add(Ui.Chip(StatusKind.Warning, VoiceLabStrings.FarTitle));
            _far.Add(Ui.Text(VoiceLabStrings.FarBody, VoiceLabScreen.NoteClass, "vv-text-body"));
            timbre.Add(_far);

            _mechanical = new VisualElement();
            _mechanical.AddToClassList(FarClass);
            _mechanical.Add(Ui.Chip(StatusKind.Warning, VoiceLabStrings.MechanicalTitle));
            _mechanical.Add(Ui.Text(VoiceLabStrings.MechanicalBody, VoiceLabScreen.NoteClass, "vv-text-body"));
            timbre.Add(_mechanical);

            _listenBase = Ui.Button(VoiceLabStrings.ListenBase);
            _listenBase.clicked += ListenBase;
            _listenShaped = Ui.Button(VoiceLabStrings.ListenShaped);
            _listenShaped.clicked += ListenShaped;
            _hold = Ui.Button(VoiceLabStrings.Hold);
            _hold.TooltipKey = VoiceLabStrings.HoldTip;
            _shaping = Ui.Text(VoiceLabStrings.Shaping, VoiceLabScreen.NoteClass, "vv-text-body");
            _reshaping = Ui.Text(VoiceLabStrings.Reshaping, VoiceLabScreen.NoteClass, "vv-text-body");
            timbre.Add(VoiceLabScreen.Actions(Ui.Text(VoiceLabStrings.Compare, VoiceLabScreen.NoteClass, "vv-text-body"), _listenBase, _listenShaped, _hold, _shaping, _reshaping));
            _failed = Ui.Chip(StatusKind.Error, VoiceLabStrings.ShapeFailed);
            timbre.Add(_failed);
            _held = new VoiceLabHoldList(ListenTo, Restore);
            _hold.clicked += () =>
            {
                _held.Hold(Timbre);
                Refresh();
            };
            timbre.Add(_held);
            Add(timbre);

            Add(Ui.Text(VoiceLabStrings.TimbreNote, VoiceLabScreen.NoteClass, "vv-text-body"));
            ActionButton reset = Ui.Button(VoiceLabStrings.Reset);
            reset.clicked += Reset;
            NextButton = Ui.Button(VoiceLabStrings.ToBuild, ButtonVariant.Primary);
            NextButton.clicked += () => _flow.GoTo(VoiceLabScreen.BuildStep);
            Add(VoiceLabScreen.Actions(reset, NextButton));

            bool canPlay = _player.Available;
            _listenBase.SetEnabled(canPlay);
            _listenShaped.SetEnabled(canPlay);
            ShowShaping(null);
            _failed.style.display = DisplayStyle.None;
            RegisterCallback<AttachToPanelEvent>(_ => Refresh());
        }

        public ActionButton NextButton { get; }

        public VoiceLabTimbre Timbre => new VoiceLabTimbre(_pitch.value, _age.value, _brightness.value, _breathiness.value, _softness.value);

        /// <summary>A slider with what its two ends mean written under them.</summary>
        public static VisualElement WithEnds(ValueSlider slider, string lowKey, string highKey)
        {
            var row = new VisualElement();
            row.AddToClassList(SliderClass);
            row.Add(slider);
            var ends = new VisualElement();
            ends.AddToClassList(EndsClass);
            ends.Add(Ui.Text(lowKey));
            ends.Add(Ui.Text(highKey));
            row.Add(ends);
            return row;
        }

        /// <summary>Shows the chosen base and the knobs' effect; called when the step opens.</summary>
        public void Refresh()
        {
            ILocalizer localizer = UiServices.Find(this)?.Localizer;
            _base.text = VoiceLabView.BaseName(localizer, _draft.Candidate, _draft.CandidateNumber);
            _pitchChange.text = (localizer?.Get(VoiceLabStrings.PitchChange) ?? string.Empty) + "  " + VoiceLabView.PitchChangeText(_draft.Candidate?.MedianHz ?? 0, _pitch.value);
            _far.style.display = VoiceLabView.IsFar(Timbre) ? DisplayStyle.Flex : DisplayStyle.None;
            _mechanical.style.display = VoiceLabView.IsMechanical(Timbre) ? DisplayStyle.Flex : DisplayStyle.None;
            if (!ReferenceEquals(_heldFor, _draft.Candidate))
            {
                // Held timbres belong to one base.
                _held.Clear();
                _heldFor = _draft.Candidate;
            }

            _hold.SetEnabled(VoiceLabHoldList.CanHold(_held.Held, Timbre));
        }

        /// <summary>The timbres held to compare.</summary>
        public VoiceLabHoldList HeldList => _held;

        public ActionButton HoldButton => _hold;

        /// <summary>Sets the knobs to a held timbre.</summary>
        public void Restore(VoiceLabTimbre timbre)
        {
            _pitch.SetValueWithoutNotify(timbre.Pitch);
            _age.SetValueWithoutNotify(timbre.Age);
            _brightness.SetValueWithoutNotify(timbre.Brightness);
            _softness.SetValueWithoutNotify(timbre.Softness);
            _breathiness.SetValueWithoutNotify(timbre.Breathiness);
            OnMoved();
        }

        private ValueSlider Knob(VisualElement parent, string nameKey, string tipKey, string lowKey, string highKey, float low = -6f)
        {
            ValueSlider slider = Ui.Slider(nameKey, low, 6f);
            slider.Step = 1f;
            slider.Signed = true;
            slider.TooltipKey = tipKey;
            slider.RegisterValueChangedCallback(_ => OnMoved());
            parent.Add(WithEnds(slider, lowKey, highKey));
            return slider;
        }

        private void OnMoved()
        {
            _draft.Timbre = Timbre;
            _flow.Rewind(VoiceLabScreen.TimbreStep);
            _failed.style.display = DisplayStyle.None;
            Refresh();
        }

        private void Reset()
        {
            foreach (ValueSlider slider in new[] { _pitch, _age, _brightness, _softness, _breathiness })
            {
                slider.SetValueWithoutNotify(0f);
            }

            OnMoved();
        }

        private void ListenBase()
        {
            _ = _player.PlayAsync(_draft.Candidate?.File, null);
        }

        private void ListenShaped()
        {
            ListenTo(Timbre);
        }

        private async void ListenTo(VoiceLabTimbre timbre)
        {
            VoiceLabCandidate candidate = _draft.Candidate;
            if (candidate == null)
            {
                return;
            }

            _failed.style.display = DisplayStyle.None;
            if (VoiceLabView.IsUnchanged(timbre))
            {
                ListenBase();
                return;
            }

            if (_preview == null || !ReferenceEquals(_previewCandidate, candidate) || !_previewTimbre.Equals(timbre))
            {
                if (_shapingSource != null && ReferenceEquals(_shapingCandidate, candidate) && _shapingTimbre.Equals(timbre))
                {
                    // Pressed again while the same voice is being made: let it finish; it plays when ready.
                    return;
                }

                // Only a changed timbre stops the one being made, and the line says it was stopped.
                bool restarting = _shapingSource != null;
                _shapingSource?.Cancel();
                var source = new CancellationTokenSource();
                _shapingSource = source;
                _shapingCandidate = candidate;
                _shapingTimbre = timbre;
                ShowShaping(restarting ? VoiceLabStrings.Reshaping : VoiceLabStrings.Shaping);
                try
                {
                    _preview = await _lab.PreviewAsync(candidate, timbre, source.Token);
                    _previewCandidate = candidate;
                    _previewTimbre = timbre;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (VoiceLabFailedException)
                {
                    // The service has logged why.
                    _failed.style.display = DisplayStyle.Flex;
                    return;
                }
                finally
                {
                    if (_shapingSource == source)
                    {
                        _shapingSource = null;
                        _shapingCandidate = null;
                        ShowShaping(null);
                    }

                    source.Dispose();
                }
            }

            _ = _player.PlayAsync(_preview.File, null);
        }

        /// <summary>The line shown while the shaped voice is made, or null when none is being made.</summary>
        public string ShapingKey { get; private set; }

        private void ShowShaping(string key)
        {
            ShapingKey = key;
            _shaping.style.display = key == VoiceLabStrings.Shaping ? DisplayStyle.Flex : DisplayStyle.None;
            _reshaping.style.display = key == VoiceLabStrings.Reshaping ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
