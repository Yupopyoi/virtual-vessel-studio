using System;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Voice;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Binding;

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
        private readonly Label _blocked;
        private readonly IVoiceConversion _voice;

        /// <param name="voice">The voice conversion, to use the voice right away; null leaves that out.</param>
        public VoiceLabRegisterStep(IVoiceLab lab, VoiceLabDraft draft, IVoiceLabFlow flow, IVoiceConversion voice = null)
        {
            _voice = voice;
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
            // On by default: a voice is made to be used (system design 14.18).
            UseNow = Ui.Toggle(VoiceLabStrings.UseNow);
            UseNow.SetValueWithoutNotify(true);
            UseNow.style.display = voice == null ? DisplayStyle.None : DisplayStyle.Flex;
            card.Add(UseNow);
            _result = new StatusChip(StatusKind.Success, VoiceLabStrings.Registered);

            // Room above, so the result does not touch the toggle (the user's review, 2026-11-15).
            _result.AddToClassList(VoiceLabScreen.SpacedClass);
            card.Add(_result);
            Add(card);

            RegisterButton = Ui.Button(VoiceLabStrings.RegisterButton, ButtonVariant.Primary);
            RegisterButton.clicked += Register;
            _again = Ui.Button(VoiceLabStrings.MakeAnother);
            _again.clicked += _flow.StartOver;
            ShowMadeButton = Ui.Button(VoiceLabStrings.ShowMadeVoices);
            ShowMadeButton.clicked += _flow.OpenMadeVoices;
            // Back to trying, also after registering: the run keeps the voice's files (the user's review, 2026-11-08).
            BackButton = Ui.Button(VoiceLabStrings.BackToTry);
            BackButton.clicked += () => _flow.GoTo(VoiceLabScreen.TryStep);
            Add(VoiceLabScreen.Actions(BackButton, RegisterButton, _again, ShowMadeButton));

            // Why Register cannot be pressed: it was greyed out without a word (the user's review, 2026-11-15).
            _blocked = QuietText.Of(new Label());
            _blocked.AddToClassList(VoiceLabScreen.NoteClass);
            _blocked.AddToClassList("vv-text-body");
            Add(_blocked);
            RegisterCallback<AttachToPanelEvent>(_ => Refresh());
            PanelLifetime.OnLocaleChanged(this, Refresh);
        }

        public ActionButton RegisterButton { get; }

        public ActionButton BackButton { get; }

        /// <summary>Opens Made voices once registered, where the voice is listed.</summary>
        public ActionButton ShowMadeButton { get; }

        /// <summary>Use the voice for the stream once registered.</summary>
        public SwitchToggle UseNow { get; }

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
            ShowMadeButton.style.display = registered ? DisplayStyle.Flex : DisplayStyle.None;
            string blocked = BlockedKey(_draft.RunId, _draft.TryFolder, registered);
            RegisterButton.SetEnabled(blocked == null && !registered);
            _blocked.style.display = blocked != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (blocked != null)
            {
                _blocked.text = localizer?.Get(blocked) ?? blocked;
            }
        }

        /// <summary>The note on why Register cannot be pressed, or null when it can (or the voice was just registered here). Pure.</summary>
        public static string BlockedKey(string runId, string tryFolder, bool registeredHere)
        {
            if (registeredHere)
            {
                return null;
            }

            if (tryFolder != null)
            {
                // A registered voice is being tried, opened from Made voices.
                return VoiceLabStrings.RegisterBlockedRegistered;
            }

            return runId == null ? VoiceLabStrings.RegisterBlockedNoRun : null;
        }

        /// <summary>The note shown when Register cannot be pressed, for tests.</summary>
        public Label BlockedNote => _blocked;

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
            // Copies the voice's files, which can take a moment: off the main thread.
            CrudResult<string> registered = await Task.Run(() => _lab.Register(runId, name));
            if (!registered.Ok)
            {
                VoiceLabScreen.ShowFailure(this, _result, "Registering run " + runId, registered.Error, registered.Detail);
                RegisterButton.SetEnabled(true);
                return;
            }

            _registeredRun = runId;
            bool use = _voice != null && UseNow.value;
            if (use)
            {
                _voice.SelectVoice(registered.Value);
            }

            _result.Status = StatusKind.Success;
            _result.TextKey = use ? VoiceLabStrings.RegisteredAndUsed : VoiceLabStrings.Registered;

            Refresh();
        }
    }
}
