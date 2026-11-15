using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// The Terms tab (the user's decision, 2026-11-15; screen spec 2.3): Voice Lab's terms to read again at any time, with
    /// their version and whether they were agreed to. The same file the agreement shows, so the terms are kept once.
    /// </summary>
    public sealed class VoiceLabTermsView : VisualElement
    {
        private readonly IVoiceLabSetup _setup;
        private readonly Label _version;
        private readonly StatusChip _agreed;
        private readonly ScrollView _body;
        private string _language;

        public VoiceLabTermsView(IVoiceLabSetup setup)
        {
            _setup = setup;
            Card = Ui.Card(VoiceLabStrings.TermsTabTitle);
            _version = QuietText.Of(new Label());
            _version.AddToClassList("vv-text-body");
            _version.AddToClassList(VoiceLabScreen.NoteClass);
            _agreed = Ui.Chip(StatusKind.Idle, VoiceLabStrings.TermsTabNotAgreed);
            Card.HeaderEnd.Add(_agreed);
            Card.Add(_version);
            _body = new ScrollView(ScrollViewMode.Vertical);
            _body.AddToClassList(VoiceLabSetupView.TermsClass);
            Card.Add(_body);
            Add(Card);
            PanelLifetime.OnLocaleChanged(this, () =>
            {
                _language = null;
                Refresh();
            });
        }

        public Card Card { get; }

        /// <summary>The body's labels, for tests.</summary>
        public VisualElement Body => _body;

        public StatusChip AgreedChip => _agreed;

        /// <summary>Shows the terms in the screen's language, and whether they were agreed to; called when the tab opens.</summary>
        public void Refresh()
        {
            ILocalizer localizer = UiServices.Find(this)?.Localizer;
            _version.text = localizer?.Get(VoiceLabStrings.SetupTermsVersion, ("version", _setup?.TermsVersion ?? string.Empty)) ?? string.Empty;
            bool agreed = _setup != null && _setup.TermsAgreed;
            _agreed.Status = agreed ? StatusKind.Success : StatusKind.Idle;
            _agreed.TextKey = agreed ? VoiceLabStrings.TermsTabAgreed : VoiceLabStrings.TermsTabNotAgreed;

            string language = localizer?.Locale?.Code ?? "en";
            if (language == _language)
            {
                return;
            }

            _language = language;
            _body.Clear();
            string text = _setup?.TermsText(language);
            if (text == null)
            {
                _body.Add(Ui.Text(VoiceLabStrings.SetupTermsMissing, VoiceLabSetupView.ProblemClass, "vv-text-body"));
                return;
            }

            foreach (Label label in MarkdownText.Labels(text, VoiceLabSetupView.TermsHeadingClass, VoiceLabSetupView.TermsItemClass))
            {
                _body.Add(label);
            }
        }
    }
}
