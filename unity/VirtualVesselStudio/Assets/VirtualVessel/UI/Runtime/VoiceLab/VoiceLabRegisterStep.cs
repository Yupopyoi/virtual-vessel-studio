using System;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// STEP 5, register (system design 14.18): a name, a summary of how the voice was made, and registering
    /// it for voice conversion.
    /// </summary>
    public sealed class VoiceLabRegisterStep : VisualElement
    {
        public const string SummaryClass = VoiceLabScreen.UssClass + "__summary";

        private readonly IVoiceLab _lab;
        private readonly VoiceLabDraft _draft;
        private readonly IVoiceLabFlow _flow;
        private readonly TextInput _name;
        private readonly Label _base;
        private readonly Label _timbre;
        private readonly StatusChip _result;
        private readonly ActionButton _again;
        private string _registeredRun;

        public VoiceLabRegisterStep(IVoiceLab lab, VoiceLabDraft draft, IVoiceLabFlow flow)
        {
            _lab = lab ?? throw new ArgumentNullException(nameof(lab));
            _draft = draft ?? throw new ArgumentNullException(nameof(draft));
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            Add(VoiceLabScreen.Lead(VoiceLabStrings.RegisterLead));

            Card card = Ui.Card(VoiceLabStrings.Register);
            _name = Ui.TextField(VoiceLabStrings.VoiceName);
            _name.PlaceholderKey = VoiceLabStrings.VoiceNameHint;
            _name.MaxLength = 60;
            _name.RegisterValueChangedCallback(_ => _name.ErrorKey = null);
            card.Add(_name);
            card.Add(SummaryRow(VoiceLabStrings.SummaryBase, out _base));
            card.Add(SummaryRow(VoiceLabStrings.SummaryTimbre, out _timbre));
            card.Add(Ui.Text(VoiceLabStrings.RegisterNote, VoiceLabScreen.NoteClass, "vv-text-body"));
            _result = new StatusChip(StatusKind.Success, VoiceLabStrings.Registered);
            card.Add(_result);
            Add(card);

            RegisterButton = Ui.Button(VoiceLabStrings.RegisterButton, ButtonVariant.Primary);
            RegisterButton.clicked += Register;
            _again = Ui.Button(VoiceLabStrings.MakeAnother);
            _again.clicked += _flow.StartOver;
            Add(VoiceLabScreen.Actions(RegisterButton, _again));
            RegisterCallback<AttachToPanelEvent>(_ => Refresh());
        }

        public ActionButton RegisterButton { get; }

        public TextInput NameInput => _name;

        /// <summary>Shows what is about to be registered; called when the step opens.</summary>
        public void Refresh()
        {
            ILocalizer localizer = UiServices.Find(this)?.Localizer;
            VoiceLabCandidate candidate = _draft.Candidate;
            _base.text = candidate == null ? string.Empty : VoiceLabView.BaseName(localizer, candidate, _draft.CandidateNumber) + "  " + CandidateTile.HzText(candidate.MedianHz);
            _timbre.text = VoiceLabView.TimbreSummary(localizer, _draft.Timbre);
            bool registered = _draft.RunId != null && _draft.RunId == _registeredRun;
            _result.style.display = registered ? DisplayStyle.Flex : DisplayStyle.None;
            _again.style.display = registered ? DisplayStyle.Flex : DisplayStyle.None;
            RegisterButton.SetEnabled(_draft.RunId != null && !registered);
        }

        private static VisualElement SummaryRow(string nameKey, out Label value)
        {
            var row = new VisualElement();
            row.AddToClassList(SummaryClass);
            row.Add(Ui.Text(nameKey, VoiceLabScreen.NoteClass));
            value = QuietText.Of(new Label());
            value.AddToClassList("vv-text-body");
            row.Add(value);
            return row;
        }

        private async void Register()
        {
            string name = VoiceLabView.CleanName(_name.value);
            string runId = _draft.RunId;
            if (name == null)
            {
                _name.ErrorKey = VoiceLabStrings.NameRequired;
                return;
            }

            if (runId == null)
            {
                return;
            }

            RegisterButton.SetEnabled(false);
            try
            {
                // Copies the voice's files, which can take a moment: off the main thread.
                await Task.Run(() => _lab.Register(runId, name));
                _registeredRun = runId;
                _result.Status = StatusKind.Success;
                _result.TextKey = VoiceLabStrings.Registered;
            }
            catch (VoiceLabFailedException)
            {
                // The service has logged why.
                _result.Status = StatusKind.Error;
                _result.TextKey = VoiceLabStrings.RegisterFailed;
                _result.style.display = DisplayStyle.Flex;
                RegisterButton.SetEnabled(true);
                return;
            }

            Refresh();
        }
    }
}
