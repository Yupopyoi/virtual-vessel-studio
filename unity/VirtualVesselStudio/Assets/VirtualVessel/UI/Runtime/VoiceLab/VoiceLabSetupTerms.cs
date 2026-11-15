using System;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// The terms before preparing, or before making when they changed (screen spec voice-lab 2.1; Voice Lab detailed
    /// design 8.2): their version and full text in a scrolling frame, an agreement toggle, and the button that agrees.
    /// </summary>
    internal sealed class VoiceLabSetupTerms
    {
        private readonly IVoiceLabSetup _setup;
        private readonly Label _version;
        private readonly VoiceLabTermsBody _body = new VoiceLabTermsBody();

        /// <param name="back">Back to the check of the PC.</param>
        /// <param name="agree">Agrees, once the toggle is on.</param>
        public VoiceLabSetupTerms(IVoiceLabSetup setup, Action back, Action agree)
        {
            _setup = setup;
            Card = Ui.Card(VoiceLabStrings.SetupTermsTitle);
            _version = QuietText.Of(new Label());
            _version.AddToClassList(VoiceLabScreen.NoteClass);
            _version.AddToClassList("vv-text-caption");
            Card.Add(_version);
            Card.Add(_body.Scroll);
            AgreeToggle = Ui.Toggle(VoiceLabStrings.SetupAgree);
            AgreeToggle.AddToClassList(VoiceLabScreen.GapClass);
            AgreeToggle.RegisterValueChangedCallback(e => StartButton.SetEnabled(e.newValue));
            Card.Add(AgreeToggle);
            BackButton = Ui.Button(VoiceLabStrings.SetupBack);
            BackButton.clicked += back;
            StartButton = Ui.Button(VoiceLabStrings.SetupStart, ButtonVariant.Primary);
            StartButton.clicked += agree;
            StartButton.SetEnabled(false);
            Card.Add(VoiceLabScreen.Actions(BackButton, StartButton));
        }

        public Card Card { get; }

        public SwitchToggle AgreeToggle { get; }

        public ActionButton BackButton { get; }

        public ActionButton StartButton { get; }

        /// <summary>Unchecks the agreement, so each showing asks for it again.</summary>
        public void Reset()
        {
            AgreeToggle.SetValueWithoutNotify(false);
            StartButton.SetEnabled(false);
        }

        /// <summary>
        /// Shows the terms in the screen's language. With <paramref name="onlyToAgree"/> nothing is built after agreeing,
        /// so the button says Agree and there is no Back.
        /// </summary>
        public void Show(ILocalizer localizer, bool onlyToAgree)
        {
            StartButton.TextKey = onlyToAgree ? VoiceLabStrings.SetupAgreeOnly : VoiceLabStrings.SetupStart;
            BackButton.style.display = onlyToAgree ? DisplayStyle.None : DisplayStyle.Flex;
            _version.text = localizer?.Get(VoiceLabStrings.SetupTermsVersion, ("version", _setup.TermsVersion ?? string.Empty)) ?? string.Empty;

            // The terms cannot be agreed to unread.
            AgreeToggle.SetEnabled(_body.Show(_setup, localizer?.Locale?.Code ?? "en"));
        }
    }
}
