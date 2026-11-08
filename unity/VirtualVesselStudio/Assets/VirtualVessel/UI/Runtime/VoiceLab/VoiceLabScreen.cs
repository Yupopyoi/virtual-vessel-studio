using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VirtualVessel.Audio;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Screens;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Recording;
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

        /// <summary>How long STEP 3 trains; short unless chosen in its details.</summary>
        public VoiceLabTrainingLength TrainingLength { get; set; }

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
        private readonly IStreamingService _streaming;
        private readonly IRecordingService _recording;
        private readonly VisualElement _content;

        /// <param name="lab">Null, or not set up, shows that voices cannot be made yet.</param>
        /// <param name="audio">Plays candidates; null leaves out the play buttons.</param>
        /// <param name="streaming">While it streams, Voice Lab is not used; optional.</param>
        /// <param name="library">What was made, for the Made voices tab; null leaves the tab out.</param>
        /// <param name="recording">While it records, Voice Lab is not used; optional.</param>
        public VoiceLabScreen(IVoiceLab lab, IAudioPreview audio = null, IStreamingService streaming = null, IVoiceLabLibrary library = null, IRecordingService recording = null)
        {
            AddToClassList(UssClass);
            _lab = lab;
            _streaming = streaming;
            _recording = recording;
            _player = new VoiceLabPlayer(audio, this);
            bool ready = lab != null && lab.Availability == VoiceLabAvailability.Ready;

            // In place of everything while a stream or a recording runs; a build in progress was stopped or kept when
            // it started (BuildGuardedStart).
            LiveLock = Ui.Card(VoiceLabStrings.LiveLockTitle);
            LiveLock.Add(Ui.Text(VoiceLabStrings.LiveLockBody, NoteClass, "vv-text-body"));
            Add(LiveLock);
            _content = new VisualElement();
            Add(_content);
            ShowLock();
            PanelLifetime.WhileAttached(this, _ => WatchLive());

            if (library != null)
            {
                Tabs = Ui.Tabs(VoiceLabStrings.TabMake, VoiceLabStrings.TabMade);
                Tabs.RegisterValueChangedCallback(e => ShowTab(e.newValue));
                _content.Add(Tabs);
            }

            _make = new VisualElement();
            _content.Add(_make);
            if (library != null)
            {
                // What was made can be listed without a training environment; making it again cannot.
                Library = new VoiceLabLibraryView(library, _player, ready ? Remake : (Action<string>)null, () => ready && lab.BuildProgress.State != VoiceLabBuildState.None);
                Library.style.display = DisplayStyle.None;
                _content.Add(Library);
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
            _panels[BuildStep] = new VoiceLabBuildStep(lab, draft, this);
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

        /// <summary>Shown instead of Voice Lab while a stream or a recording runs.</summary>
        public Card LiveLock { get; }

        private bool IsLive => VoiceLabView.IsLive(_streaming?.State.Value ?? StreamingState.Idle, _recording?.State.Value ?? RecordingState.Idle);

        private IDisposable WatchLive()
        {
            var subscriptions = new List<IDisposable>();
            if (_streaming != null)
            {
                subscriptions.Add(_streaming.State.Subscribe(_ => ShowLock()));
            }

            if (_recording != null)
            {
                subscriptions.Add(_recording.State.Subscribe(_ => ShowLock()));
            }

            ShowLock();
            return new DisposeAction(() => subscriptions.ForEach(s => s.Dispose()));
        }

        private void ShowLock()
        {
            bool live = IsLive;
            if (live)
            {
                _player.Stop();
            }

            LiveLock.style.display = live ? DisplayStyle.Flex : DisplayStyle.None;
            _content.style.display = live ? DisplayStyle.None : DisplayStyle.Flex;
        }

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
            CrudResult<VoiceLabRemake> read = _lab.Remake(runId);
            if (!read.Ok)
            {
                Library?.ShowFailure("Making run " + runId + " again", read.Error, read.Detail);
                return;
            }

            VoiceLabRemake remake = read.Value;

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

        /// <summary>
        /// Handles a CRUD failure as its caller (system design 6.31): the chip tells the user by the kind of
        /// failure, and the log keeps the detail for developers.
        /// </summary>
        internal static void ShowFailure(VisualElement owner, StatusChip chip, string what, CrudError error, string detail)
        {
            chip.Status = StatusKind.Error;
            chip.TextKey = VoiceLabView.CrudFailureKey(error);
            chip.style.display = DisplayStyle.Flex;
            UiServices.Find(owner)?.Log?.Warning($"{what} failed ({error}): {detail}");
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
