using System;
using UnityEngine.UIElements;
using VirtualVessel.Audio;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Screens;
using VirtualVessel.UI.Streaming;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>What the steps decide, carried from one to the next.</summary>
    public sealed class VoiceLabDraft
    {
        public VoiceLabCandidate Candidate { get; set; }

        /// <summary>The candidate's number as its tile showed it.</summary>
        public int CandidateNumber { get; set; }

        public VoiceLabTimbre Timbre { get; set; }

        /// <summary>The completed build to register; null until STEP 3 completes.</summary>
        public string RunId { get; set; }
    }

    /// <summary>How a step moves the flow on, or back.</summary>
    public interface IVoiceLabFlow
    {
        void GoTo(int step);

        /// <summary>A choice changed: steps after <paramref name="lastValid"/> no longer hold and close again.</summary>
        void Rewind(int lastValid);

        /// <summary>While a voice is being made, the other steps cannot be opened.</summary>
        void Lock(bool locked);

        /// <summary>Back to STEP 1 with the later steps closed, to make another voice.</summary>
        void StartOver();
    }

    /// <summary>
    /// The Voice Lab screen (id "lab"; system design 14.13 to 14.18): making a voice in numbered steps. Pick
    /// a base, shape its timbre, make it, try it, and register it. Finished steps can be revisited. A second
    /// tab lists what was made (screen spec 8).
    /// </summary>
    public sealed class VoiceLabScreen : VisualElement, IScreen, IVoiceLabFlow
    {
        public const string UssClass = "vv-lab";
        public const string PanelClass = UssClass + "__panel";
        public const string LeadClass = UssClass + "__lead";
        public const string ActionsClass = UssClass + "__actions";
        public const string NoteClass = UssClass + "__note";

        public const int BaseStep = 0;
        public const int TimbreStep = 1;
        public const int BuildStep = 2;
        public const int TryStep = 3;
        public const int RegisterStep = 4;

        public const int MakeTab = 0;
        public const int MadeTab = 1;

        private readonly VisualElement[] _panels = new VisualElement[5];
        private readonly IVoiceLab _lab;
        private readonly VisualElement _make;
        private readonly VoiceLabBaseStep _base;
        private readonly VoiceLabPlayer _player;
        private readonly VoiceLabDraft _draft;
        private readonly VoiceLabTimbreStep _timbre;
        private readonly VoiceLabRegisterStep _register;

        /// <param name="lab">Null, or not set up, shows that voices cannot be made yet.</param>
        /// <param name="audio">Plays candidates; null leaves out the play buttons.</param>
        /// <param name="streaming">Stops a build from starting during a stream; optional.</param>
        /// <param name="library">What was made, for the Made voices tab; null leaves the tab out.</param>
        public VoiceLabScreen(IVoiceLab lab, IAudioPreview audio = null, IStreamingService streaming = null, IVoiceLabLibrary library = null)
        {
            AddToClassList(UssClass);
            _lab = lab;
            _player = new VoiceLabPlayer(audio, this);
            bool ready = lab != null && lab.Availability == VoiceLabAvailability.Ready;
            if (library != null)
            {
                Tabs = Ui.Tabs(VoiceLabStrings.TabMake, VoiceLabStrings.TabMade);
                Tabs.RegisterValueChangedCallback(e => ShowTab(e.newValue));
                Add(Tabs);
            }

            _make = new VisualElement();
            Add(_make);
            if (library != null)
            {
                // What was made can be listed without a training environment; making it again cannot.
                Library = new VoiceLabLibraryView(library, _player, ready ? Remake : (Action<string>)null, () => ready && lab.BuildProgress.State != VoiceLabBuildState.None);
                Library.style.display = DisplayStyle.None;
                Add(Library);
            }

            if (!ready)
            {
                Card card = Ui.Card(VoiceLabStrings.NotSetUpTitle);
                card.Add(Ui.Text(VoiceLabStrings.NotSetUpBody, NoteClass, "vv-text-body"));
                _make.Add(card);
                return;
            }

            _draft = new VoiceLabDraft();
            VoiceLabDraft draft = _draft;

            Steps = new StepIndicator();
            Steps.SetSteps(VoiceLabStrings.StepBase, VoiceLabStrings.StepTimbre, VoiceLabStrings.StepBuild, VoiceLabStrings.StepTry, VoiceLabStrings.StepRegister);
            Steps.StepChosen += GoTo;
            _make.Add(Steps);

            _timbre = new VoiceLabTimbreStep(lab, _player, draft, this);
            _register = new VoiceLabRegisterStep(lab, draft, this);
            _base = new VoiceLabBaseStep(lab, _player, draft, this);
            _panels[BaseStep] = _base;
            _panels[TimbreStep] = _timbre;
            _panels[BuildStep] = new VoiceLabBuildStep(lab, draft, streaming, this);
            _panels[TryStep] = TryPanel();
            _panels[RegisterStep] = _register;
            foreach (VisualElement panel in _panels)
            {
                panel.AddToClassList(PanelClass);
                _make.Add(panel);
            }

            GoTo(BaseStep);
        }

        public StepIndicator Steps { get; }

        /// <summary>Make a voice and Made voices; null without a library.</summary>
        public TabBar Tabs { get; }

        public VoiceLabLibraryView Library { get; }

        public void ShowTab(int tab)
        {
            _player.Stop();
            Tabs?.SetValueWithoutNotify(tab);
            _make.style.display = tab == MakeTab ? DisplayStyle.Flex : DisplayStyle.None;
            if (Library != null)
            {
                Library.style.display = tab == MadeTab ? DisplayStyle.Flex : DisplayStyle.None;
                if (tab == MadeTab)
                {
                    Library.Refresh();
                }
            }
        }

        /// <summary>
        /// Puts a run's words, base, and timbre back and opens STEP 2; without the base's audio, STEP 1 with the
        /// words (Voice Lab detailed design 8.6).
        /// </summary>
        public void Remake(string runId)
        {
            VoiceLabRemake remake;
            try
            {
                remake = _lab.Remake(runId);
            }
            catch (VoiceLabFailedException)
            {
                // The service has logged why.
                return;
            }

            _base.RequestView.SetPicks(remake.Words, remake.FreeWords);
            _base.ShowBaseGone(remake.Candidate == null);
            _draft.RunId = null;
            ShowTab(MakeTab);
            if (remake.Candidate == null)
            {
                _draft.Candidate = null;
                GoTo(BaseStep, reset: true);
                return;
            }

            _draft.Candidate = remake.Candidate;
            _draft.CandidateNumber = 0;
            GoTo(TimbreStep, reset: true);
            _timbre.Restore(remake.Timbre);
        }

        public int Current => Steps?.Current ?? -1;

        /// <summary>The step's panel, for tests.</summary>
        public VisualElement Panel(int step)
        {
            return _panels[step];
        }

        public void GoTo(int step)
        {
            GoTo(step, reset: false);
        }

        public void Rewind(int lastValid)
        {
            if (lastValid < BuildStep)
            {
                _draft.RunId = null;
            }

            Steps.Show(Steps.Current, Math.Min(Steps.Reached, Math.Max(lastValid, Steps.Current)));
        }

        public void Lock(bool locked)
        {
            Steps.SetEnabled(!locked);
        }

        /// <summary>Keeps the mood and the candidates, so a similar voice is quick to make.</summary>
        public void StartOver()
        {
            _draft.RunId = null;
            GoTo(BaseStep, reset: true);
        }

        void IScreen.OnShown()
        {
            if (Library != null && Library.style.display == DisplayStyle.Flex)
            {
                Library.Refresh();
            }
        }

        void IScreen.OnHidden()
        {
            // Samples are for this screen; one must not go on playing over another screen.
            _player?.Stop();
        }

        /// <summary>A lead paragraph at the top of a step.</summary>
        internal static Label Lead(string key)
        {
            return Ui.Text(key, LeadClass, "vv-text-body");
        }

        internal static VisualElement Actions(params VisualElement[] buttons)
        {
            var row = new VisualElement();
            row.AddToClassList(ActionsClass);
            foreach (VisualElement button in buttons)
            {
                row.Add(button);
            }

            return row;
        }

        private void GoTo(int step, bool reset)
        {
            _player.Stop();
            int reached = reset ? step : Math.Max(Steps.Reached, step);
            if (step == TimbreStep)
            {
                _timbre.Refresh();
            }
            else if (step == RegisterStep)
            {
                _register.Refresh();
            }

            Steps.Show(step, reached);
            for (int i = 0; i < _panels.Length; i++)
            {
                _panels[i].style.display = i == step ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // Trying a voice through the real-time conversion is the next stage (Voice Lab detailed design 8.3.4).
        private VisualElement TryPanel()
        {
            var panel = new VisualElement();
            panel.Add(Lead(VoiceLabStrings.TryLead));
            Card card = Ui.Card(VoiceLabStrings.StepTry);
            card.Add(Ui.Text(VoiceLabStrings.TryLater, NoteClass, "vv-text-body"));
            panel.Add(card);
            ActionButton back = Ui.Button(VoiceLabStrings.BackToTimbre);
            back.clicked += () => GoTo(TimbreStep);
            ActionButton next = Ui.Button(VoiceLabStrings.ToRegister, ButtonVariant.Primary);
            next.clicked += () => GoTo(RegisterStep);
            panel.Add(Actions(back, next));
            return panel;
        }
    }
}
