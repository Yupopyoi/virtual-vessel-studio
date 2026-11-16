using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// At the top of Make a voice (the user's request, 2026-11-15; screen spec 2.2): the unfinished STEP 1 and STEP 2
    /// kept from before, to continue or discard, and a voice made but not registered, to try and register.
    /// </summary>
    public sealed class VoiceLabResumeNotice : VisualElement
    {
        public const string UssClass = VoiceLabScreen.UssClass + "__resume";

        private readonly Label _workBody;
        private readonly Label _voiceBody;
        private readonly Label _voiceMore;
        private VoiceLabWork _work;
        private VoiceLabRunRecord _voice;
        private int _voices;

        public VoiceLabResumeNotice(Action resume, Action discard, Action<string> tryVoice)
        {
            AddToClassList(UssClass);
            WorkCard = Ui.Card(VoiceLabStrings.ResumeWorkTitle);
            _workBody = Body(WorkCard);
            ContinueButton = Ui.Button(VoiceLabStrings.ResumeContinue, ButtonVariant.Primary);
            ContinueButton.clicked += () => resume?.Invoke();
            DiscardButton = Ui.Button(VoiceLabStrings.ResumeDiscard);
            DiscardButton.clicked += () => discard?.Invoke();
            WorkCard.Add(VoiceLabScreen.Actions(ContinueButton, DiscardButton));
            Add(WorkCard);

            VoiceCard = Ui.Card(VoiceLabStrings.ResumeVoiceTitle);
            _voiceBody = Body(VoiceCard);
            _voiceMore = Body(VoiceCard);
            TryButton = Ui.Button(VoiceLabStrings.ResumeTry, ButtonVariant.Primary);
            TryButton.clicked += () =>
            {
                if (_voice != null)
                {
                    tryVoice?.Invoke(_voice.RunId);
                }
            };
            VoiceCard.Add(VoiceLabScreen.Actions(TryButton));
            Add(VoiceCard);

            Show(null, null, 0);
            PanelLifetime.OnLocaleChanged(this, Render);
            RegisterCallback<AttachToPanelEvent>(_ => Render());
        }

        public Card WorkCard { get; }

        public Card VoiceCard { get; }

        public ActionButton ContinueButton { get; }

        public ActionButton DiscardButton { get; }

        public ActionButton TryButton { get; }

        /// <summary>
        /// Which cards show: the work only while this session has made nothing, as the screen then still holds it, and
        /// a voice not registered only in STEP 1 or STEP 2 and while nothing is being built, so it does not talk over the
        /// voice being made. Pure.
        /// </summary>
        public static (bool Work, bool Voice) WhatToShow(bool madeThisSession, bool workKept, int unregistered, int step, bool building)
        {
            bool early = step <= VoiceLabScreen.TimbreStep;
            return (!madeThisSession && workKept, unregistered > 0 && early && !building);
        }

        /// <summary>
        /// The step the work is kept as while <paramref name="current"/> is open, or -1 when nothing is kept: STEP 3
        /// before building keeps STEP 2's work, and once a voice is built or being built the run holds it. Pure.
        /// </summary>
        public static int StepToKeep(int current, bool built, bool building)
        {
            if (current == VoiceLabScreen.BaseStep || current == VoiceLabScreen.TimbreStep)
            {
                return current;
            }

            return current == VoiceLabScreen.BuildStep && !built && !building ? VoiceLabScreen.TimbreStep : -1;
        }

        /// <summary>Completed runs no voice was registered from, newest first. Pure.</summary>
        public static IReadOnlyList<VoiceLabRunRecord> UnregisteredOf(IEnumerable<VoiceLabRunRecord> runs)
        {
            return runs.Where(r => r.State == VoiceLabBuildState.Completed && r.VoiceNames.Count == 0).OrderByDescending(r => r.CreatedAt).ToArray();
        }

        /// <summary>"STEP 2 Shape the timbre", "11/15 14:20": where the work stopped and when. Pure.</summary>
        public static string WorkText(ILocalizer localizer, VoiceLabWork work)
        {
            string stepKey = work.Step == VoiceLabScreen.TimbreStep ? VoiceLabStrings.StepTimbre : VoiceLabStrings.StepBase;
            string step = "STEP " + (work.Step + 1).ToString(CultureInfo.InvariantCulture) + " " + (localizer?.Get(stepKey) ?? stepKey);
            return localizer?.Get(VoiceLabStrings.ResumeWorkBody, ("step", step), ("time", TimeText(work.SavedAt))) ?? step;
        }

        /// <summary>Month, day, and time in the PC's time zone. Pure but for the time zone.</summary>
        public static string TimeText(DateTimeOffset at)
        {
            return at.ToLocalTime().ToString("M/d HH:mm", CultureInfo.InvariantCulture);
        }

        /// <param name="work">The unfinished work to offer, or null.</param>
        /// <param name="voice">The newest voice made but not registered, or null.</param>
        /// <param name="voices">How many voices are made but not registered.</param>
        public void Show(VoiceLabWork work, VoiceLabRunRecord voice, int voices)
        {
            _work = work;
            _voice = voice;
            _voices = voices;
            WorkCard.style.display = work != null ? DisplayStyle.Flex : DisplayStyle.None;
            VoiceCard.style.display = voice != null ? DisplayStyle.Flex : DisplayStyle.None;
            style.display = work != null || voice != null ? DisplayStyle.Flex : DisplayStyle.None;
            Render();
        }

        private void Render()
        {
            ILocalizer localizer = UiServices.Find(this)?.Localizer;
            _workBody.text = _work != null ? WorkText(localizer, _work) : string.Empty;
            _voiceBody.text = _voice != null ? localizer?.Get(VoiceLabStrings.ResumeVoiceBody, ("time", TimeText(_voice.CreatedAt))) ?? string.Empty : string.Empty;
            string more = (_voices - 1).ToString(CultureInfo.InvariantCulture);
            _voiceMore.text = _voices > 1 ? localizer?.Get(VoiceLabStrings.ResumeVoiceMore, ("n", more)) ?? more : string.Empty;
            _voiceMore.style.display = _voices > 1 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static Label Body(Card card)
        {
            Label body = VoiceLabScreen.PlainNote();
            card.Add(body);
            return body;
        }
    }
}
