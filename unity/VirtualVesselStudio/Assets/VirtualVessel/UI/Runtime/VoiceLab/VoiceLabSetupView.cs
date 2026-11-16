using System;
using System.Threading;
using UnityEngine.UIElements;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>What the setup view shows (screen spec voice-lab 2.1).</summary>
    public enum VoiceLabSetupPage
    {
        /// <summary>Nothing: Voice Lab is ready and has no update.</summary>
        None,

        /// <summary>The check of the PC and where to build, before the terms.</summary>
        Check,

        Terms,

        /// <summary>A build running, stopped, or failed.</summary>
        Progress,

        /// <summary>A first build just completed.</summary>
        Done,

        /// <summary>Ready, with a newer environment to build.</summary>
        Update,
    }

    /// <summary>
    /// Preparing Voice Lab on the screen (screen spec voice-lab 2.1; Voice Lab detailed design 8.1, 8.2): which card
    /// shows for activation's state, and starting and stopping the build. The cards are their own parts: the check of
    /// the PC (<see cref="VoiceLabSetupCheck"/>), the terms (<see cref="VoiceLabSetupTerms"/>), and the build's
    /// progress (<see cref="VoiceLabSetupProgressView"/>); the done and update cards are small and stay here. The terms
    /// are read from the files the app ships, so they are kept in one place (the user's review, 2026-11-11).
    /// </summary>
    public sealed class VoiceLabSetupView : VisualElement
    {
        public const string UssClass = "vv-lab-setup";
        public const string TermsClass = UssClass + "__terms";
        public const string TermsHeadingClass = UssClass + "__terms-heading";
        public const string TermsItemClass = UssClass + "__terms-item";
        public const string StepClass = UssClass + "__step";
        public const string ProblemClass = UssClass + "__problem";
        public const string ControlsClass = UssClass + "__controls";
        public const string LogClass = UssClass + "__log";

        private const int RefreshMs = 250;

        private readonly IVoiceLabSetup _setup;
        private readonly Action _toMake;
        private readonly VoiceLabSetupCheck _check;
        private readonly VoiceLabSetupTerms _terms;
        private readonly VoiceLabSetupProgressView _progress;
        private readonly StatusChip _updateFailed;
        private readonly Label _updateSize;
        private CancellationTokenSource _run;
        private IVisualElementScheduledItem _refresh;
        private bool _wantsTerms;
        private bool _justFinished;

        /// <param name="setup">Activation as the screen sees it.</param>
        /// <param name="files">Picks the folder to build in; null keeps the default.</param>
        /// <param name="toMake">Opens making a voice once the first build completed, or the terms were agreed to again.</param>
        public VoiceLabSetupView(IVoiceLabSetup setup, IFileDialog files, Action toMake)
        {
            _setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _toMake = toMake;
            AddToClassList(UssClass);

            _check = new VoiceLabSetupCheck(setup, files, () => ShowTerms(true));
            Add(_check.Card);
            _terms = new VoiceLabSetupTerms(setup, () => ShowTerms(false), AgreeAndRun);
            Add(_terms.Card);
            _progress = new VoiceLabSetupProgressView(setup, () => _run?.Cancel(), RunOrAsk);
            Add(_progress.Card);

            DoneCard = Ui.Card(VoiceLabStrings.SetupDoneTitle);
            DoneCard.Add(Ui.Text(VoiceLabStrings.SetupDoneBody, VoiceLabScreen.NoteClass, "vv-text-body"));
            ToMakeButton = Ui.Button(VoiceLabStrings.SetupToMake, ButtonVariant.Primary);
            ToMakeButton.clicked += () =>
            {
                _justFinished = false;
                Refresh();
                _toMake?.Invoke();
            };
            DoneCard.Add(VoiceLabScreen.Actions(ToMakeButton));
            Add(DoneCard);

            UpdateCard = Ui.Card(VoiceLabStrings.SetupUpdateTitle);
            UpdateCard.Add(Ui.Text(VoiceLabStrings.SetupUpdateBody, VoiceLabScreen.NoteClass, "vv-text-body"));
            _updateFailed = Ui.Chip(StatusKind.Error, VoiceLabStrings.SetupUpdateFailed);
            _updateFailed.AddToClassList(VoiceLabScreen.GapClass);
            UpdateCard.Add(_updateFailed);
            UpdateButton = Ui.Button(VoiceLabStrings.SetupUpdateStart, ButtonVariant.Primary);
            UpdateButton.clicked += RunOrAsk;
            _updateSize = TextLabel(VoiceLabScreen.NoteClass);
            UpdateCard.Add(VoiceLabScreen.Actions(UpdateButton, _updateSize));
            Add(UpdateCard);

            RegisterCallback<AttachToPanelEvent>(_ => Refresh());

            // The check's values and the terms are built in the language shown; they follow it when it changes.
            PanelLifetime.OnLocaleChanged(this, () =>
            {
                _check.Relabel();
                _progress.ShowEntries(rewrite: true);
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ => _check.Cancel());
            Refresh();
        }

        public Card CheckCard => _check.Card;

        public Card TermsCard => _terms.Card;

        public Card ProgressCard => _progress.Card;

        public Card DoneCard { get; }

        public Card UpdateCard { get; }

        public ActionButton ChangeButton => _check.ChangeButton;

        public ActionButton ToTermsButton => _check.ToTermsButton;

        public SwitchToggle AgreeToggle => _terms.AgreeToggle;

        public ActionButton StartButton => _terms.StartButton;

        public ActionButton BackButton => _terms.BackButton;

        public ActionButton StopButton => _progress.StopButton;

        public ActionButton ResumeButton => _progress.ResumeButton;

        public ActionButton ToMakeButton { get; }

        public ActionButton UpdateButton { get; }

        /// <summary>The page shown now.</summary>
        public VoiceLabSetupPage Page { get; private set; }

        /// <summary>The last check of the PC; null until one completed.</summary>
        public VoiceLabPcCheck LastCheck => _check.LastCheck;

        /// <summary>
        /// Voice Lab is built and the terms show because they changed, not because the user chose to build: agreeing
        /// only agrees, and there is nowhere to go back to. An update waiting is offered after, on its own card. Pure.
        /// </summary>
        public static bool OnlyToAgree(VoiceLabSetupState state, bool wantsTerms)
        {
            return state == VoiceLabSetupState.Ready && !wantsTerms;
        }

        /// <summary>
        /// The page for activation's state: a build running shows its progress; the terms when asked for; one stopped or
        /// failed its progress, to continue; once ready, the first build's completion, then terms not agreed to (changed
        /// since, Voice Lab detailed design 8.2), then an update when there is one. Pure.
        /// </summary>
        public static VoiceLabSetupPage PageOf(VoiceLabSetupState state, bool updateAvailable, bool wantsTerms, bool justFinished, bool termsAgreed = true)
        {
            if (state == VoiceLabSetupState.Preparing)
            {
                return VoiceLabSetupPage.Progress;
            }

            if (wantsTerms)
            {
                return VoiceLabSetupPage.Terms;
            }

            switch (state)
            {
                case VoiceLabSetupState.Paused:
                case VoiceLabSetupState.Failed:
                    return VoiceLabSetupPage.Progress;
                case VoiceLabSetupState.Ready:
                    if (justFinished)
                    {
                        return VoiceLabSetupPage.Done;
                    }

                    return !termsAgreed ? VoiceLabSetupPage.Terms : updateAvailable ? VoiceLabSetupPage.Update : VoiceLabSetupPage.None;
                default:
                    return VoiceLabSetupPage.Check;
            }
        }

        /// <summary>Shows activation's state again, such as when the screen opens.</summary>
        public void Refresh()
        {
            VoiceLabSetupState state = _setup.State;
            VoiceLabSetupPage page = PageOf(state, _setup.UpdateAvailable, _wantsTerms, _justFinished, _setup.TermsAgreed);
            VoiceLabSetupPage before = Page;
            Page = page;

            // Looked at on coming to it: on opening the screen, and after the environment was deleted.
            if (page == VoiceLabSetupPage.Check && panel != null && !_check.Checking && (before != page || LastCheck == null))
            {
                _check.Start();
            }

            style.display = page == VoiceLabSetupPage.None ? DisplayStyle.None : DisplayStyle.Flex;
            CheckCard.style.display = Show(page == VoiceLabSetupPage.Check);
            TermsCard.style.display = Show(page == VoiceLabSetupPage.Terms);
            ProgressCard.style.display = Show(page == VoiceLabSetupPage.Progress);
            DoneCard.style.display = Show(page == VoiceLabSetupPage.Done);
            UpdateCard.style.display = Show(page == VoiceLabSetupPage.Update);
            ILocalizer localizer = UiServices.Find(this)?.Localizer;
            if (page == VoiceLabSetupPage.Terms)
            {
                _terms.Show(localizer, OnlyToAgree(state, _wantsTerms));
            }
            else if (page == VoiceLabSetupPage.Progress)
            {
                _progress.Show(state, _run != null);
            }
            else if (page == VoiceLabSetupPage.Update)
            {
                _updateFailed.style.display = Show(_setup.LastFailure != VoiceLabSetupFailure.None);
                _updateSize.text = localizer?.Get(VoiceLabStrings.SetupUpdateSize, ("size", VoiceLabLibraryView.SizeText(_setup.DownloadBytes))) ?? string.Empty;
            }
        }

        internal static StyleEnum<DisplayStyle> Show(bool shown)
        {
            return shown ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // Text set by the view, such as a GPU's name or a key it changes.
        internal static Label TextLabel(string ussClass)
        {
            Label label = QuietText.Of(new Label());
            label.AddToClassList(ussClass);
            label.AddToClassList("vv-text-body");
            return label;
        }

        private void ShowTerms(bool shown)
        {
            _wantsTerms = shown;
            _terms.Reset();
            Refresh();
        }

        private void AgreeAndRun()
        {
            if (!AgreeToggle.value)
            {
                return;
            }

            bool onlyToAgree = OnlyToAgree(_setup.State, _wantsTerms);
            _setup.AgreeToTerms();
            _wantsTerms = false;
            if (onlyToAgree)
            {
                Refresh();
                _toMake?.Invoke();
                return;
            }

            Run();
        }

        // The terms changed since they were agreed to: they come first again.
        private void RunOrAsk()
        {
            if (!_setup.TermsAgreed)
            {
                ShowTerms(true);
                return;
            }

            Run();
        }

        private async void Run()
        {
            if (_run != null)
            {
                return;
            }

            bool wasReady = _setup.State == VoiceLabSetupState.Ready;
            _run = new CancellationTokenSource();
            _refresh = schedule.Execute(Refresh).Every(RefreshMs);
            var task = _setup.RunAsync(_run.Token);
            Refresh();
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // Stopped: the downloads are kept, and continuing picks up from them.
            }
            finally
            {
                _refresh.Pause();
                _refresh = null;
                _run.Dispose();
                _run = null;
            }

            _justFinished = !wasReady && _setup.State == VoiceLabSetupState.Ready;
            Refresh();
        }
    }
}
