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
        private readonly Label _base;
        private readonly Label _pitchChange;
        private readonly VisualElement _far;
        private readonly ActionButton _listenBase;
        private readonly ActionButton _listenShaped;
        private readonly Label _shaping;
        private readonly StatusChip _failed;
        private CancellationTokenSource _shapingSource;
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
            _breathiness = Knob(timbre, VoiceLabStrings.TimbreBreathiness, VoiceLabStrings.TimbreBreathinessTip, VoiceLabStrings.TimbreBreathinessLow, VoiceLabStrings.TimbreBreathinessHigh);

            _far = new VisualElement();
            _far.AddToClassList(FarClass);
            _far.Add(Ui.Chip(StatusKind.Warning, VoiceLabStrings.FarTitle));
            _far.Add(Ui.Text(VoiceLabStrings.FarBody, VoiceLabScreen.NoteClass, "vv-text-body"));
            timbre.Add(_far);

            _listenBase = Ui.Button(VoiceLabStrings.ListenBase);
            _listenBase.clicked += ListenBase;
            _listenShaped = Ui.Button(VoiceLabStrings.ListenShaped);
            _listenShaped.clicked += ListenShaped;
            _shaping = Ui.Text(VoiceLabStrings.Shaping, VoiceLabScreen.NoteClass, "vv-text-body");
            timbre.Add(VoiceLabScreen.Actions(Ui.Text(VoiceLabStrings.Compare, VoiceLabScreen.NoteClass, "vv-text-body"), _listenBase, _listenShaped, _shaping));
            _failed = Ui.Chip(StatusKind.Error, VoiceLabStrings.ShapeFailed);
            timbre.Add(_failed);
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
            ShowShaping(false);
            _failed.style.display = DisplayStyle.None;
            RegisterCallback<AttachToPanelEvent>(_ => Refresh());
        }

        public ActionButton NextButton { get; }

        public VoiceLabTimbre Timbre => new VoiceLabTimbre(_pitch.value, _age.value, _brightness.value, _breathiness.value);

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
            _base.text = _draft.Candidate == null ? string.Empty : CandidateTile.NameText(localizer, _draft.CandidateNumber);
            _pitchChange.text = (localizer?.Get(VoiceLabStrings.PitchChange) ?? string.Empty) + "  " + VoiceLabView.PitchChangeText(_draft.Candidate?.MedianHz ?? 0, _pitch.value);
            _far.style.display = VoiceLabView.IsFar(Timbre) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private ValueSlider Knob(VisualElement parent, string nameKey, string tipKey, string lowKey, string highKey)
        {
            ValueSlider slider = Ui.Slider(nameKey, -6f, 6f);
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
            foreach (ValueSlider slider in new[] { _pitch, _age, _brightness, _breathiness })
            {
                slider.SetValueWithoutNotify(0f);
            }

            OnMoved();
        }

        private void ListenBase()
        {
            _ = _player.PlayAsync(_draft.Candidate?.File, null);
        }

        private async void ListenShaped()
        {
            VoiceLabCandidate candidate = _draft.Candidate;
            VoiceLabTimbre timbre = Timbre;
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
                _shapingSource?.Cancel();
                var source = new CancellationTokenSource();
                _shapingSource = source;
                ShowShaping(true);
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
                        ShowShaping(false);
                    }

                    source.Dispose();
                }
            }

            _ = _player.PlayAsync(_preview.File, null);
        }

        private void ShowShaping(bool shaping)
        {
            _shaping.style.display = shaping ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
