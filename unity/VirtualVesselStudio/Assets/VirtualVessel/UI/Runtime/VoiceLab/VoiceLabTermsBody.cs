using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// The terms' full text in a scrolling frame, split into headings, paragraphs, and items (screen spec 2.1, 2.3).
    /// The agreement and the Terms tab both show it, so it is made in one place.
    /// </summary>
    internal sealed class VoiceLabTermsBody
    {
        private string _language;
        private bool _hasText;

        public VoiceLabTermsBody()
        {
            Scroll = new ScrollView(ScrollViewMode.Vertical);
            Scroll.AddToClassList(VoiceLabSetupView.TermsClass);
        }

        public ScrollView Scroll { get; }

        /// <summary>
        /// Shows the terms in <paramref name="language"/>, unless they show in it already, and says whether there are terms
        /// to read. Without them it says they are missing.
        /// </summary>
        public bool Show(IVoiceLabSetup setup, string language)
        {
            if (language == _language)
            {
                return _hasText;
            }

            _language = language;
            Scroll.Clear();
            string text = setup?.TermsText(language);
            _hasText = text != null;
            if (!_hasText)
            {
                Scroll.Add(Ui.Text(VoiceLabStrings.SetupTermsMissing, VoiceLabSetupView.ProblemClass, "vv-text-body"));
                return false;
            }

            foreach (Label label in MarkdownText.Labels(text, VoiceLabSetupView.TermsHeadingClass, VoiceLabSetupView.TermsItemClass))
            {
                Scroll.Add(label);
            }

            return true;
        }

        /// <summary>Reads the terms again at the next showing, as after a language change.</summary>
        public void Forget()
        {
            _language = null;
        }
    }
}
