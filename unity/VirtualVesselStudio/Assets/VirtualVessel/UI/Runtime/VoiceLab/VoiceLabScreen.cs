using System;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using VirtualVessel.Audio;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Icons;
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
    /// Plays candidates and previews to the user (Audio's monitor bus, never the stream) and says when one
    /// ends, so a button can show Play again. One sample at a time.
    /// </summary>
    public sealed class VoiceLabPlayer
    {
        private const long WatchMs = 200;

        private readonly IAudioPreview _audio;
        private readonly VisualElement _owner;
        private Action _finished;
        private IVisualElementScheduledItem _watch;
        private IconButton _button;

        public VoiceLabPlayer(IAudioPreview audio, VisualElement owner)
        {
            _audio = audio;
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public bool Available => _audio != null;

        /// <summary>Plays <paramref name="file"/>, stopping the one before; <paramref name="finished"/> runs when it ends or is replaced.</summary>
        public async Task<bool> PlayAsync(string file, Action finished)
        {
            Stop();
            if (_audio == null || file == null)
            {
                finished?.Invoke();
                return false;
            }

            _finished = finished;
            bool playing = await _audio.PlayAsync(file);
            if (_finished != finished)
            {
                // Another sample started or Stop ran while this one was being read.
                return playing;
            }

            if (!playing)
            {
                Finish();
                return false;
            }

            _watch = _owner.schedule.Execute(Watch).Every(WatchMs);
            return true;
        }

        /// <summary>
        /// Plays <paramref name="file"/> from a play button, which shows Stop while it plays; pressing the
        /// playing button again stops it. A null file does nothing.
        /// </summary>
        public async void Toggle(IconButton button, string file)
        {
            if (_button == button)
            {
                Stop();
                return;
            }

            if (file == null)
            {
                return;
            }

            _button = button;
            button.IconName = IconName.Stop;
            await PlayAsync(file, () =>
            {
                button.IconName = IconName.Play;
                if (_button == button)
                {
                    _button = null;
                }
            });
        }

        public void Stop()
        {
            if (_finished != null)
            {
                _audio?.Stop();
            }

            Finish();
        }

        /// <summary>Whether a preview's progress means it has ended. Pure.</summary>
        public static bool HasEnded(float? progress)
        {
            return progress == null || progress.Value >= 1f;
        }

        private void Watch()
        {
            if (HasEnded(_audio.Progress))
            {
                Finish();
            }
        }

        private void Finish()
        {
            _watch?.Pause();
            _watch = null;
            Action finished = _finished;
            _finished = null;
            finished?.Invoke();
        }
    }

    /// <summary>
    /// The Voice Lab screen (id "lab"; system design 14.13 to 14.18): making a voice in numbered steps. Pick
    /// a base, shape its timbre, make it, try it, and register it. Finished steps can be revisited.
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

        private readonly VisualElement[] _panels = new VisualElement[5];
        private readonly VoiceLabPlayer _player;
        private readonly VoiceLabDraft _draft;
        private readonly VoiceLabTimbreStep _timbre;
        private readonly VoiceLabRegisterStep _register;

        /// <param name="lab">Null, or not set up, shows that voices cannot be made yet.</param>
        /// <param name="audio">Plays candidates; null leaves out the play buttons.</param>
        /// <param name="streaming">Stops a build from starting during a stream; optional.</param>
        public VoiceLabScreen(IVoiceLab lab, IAudioPreview audio = null, IStreamingService streaming = null)
        {
            AddToClassList(UssClass);
            if (lab == null || lab.Availability != VoiceLabAvailability.Ready)
            {
                Card card = Ui.Card(VoiceLabStrings.NotSetUpTitle);
                card.Add(Ui.Text(VoiceLabStrings.NotSetUpBody, NoteClass, "vv-text-body"));
                Add(card);
                return;
            }

            _player = new VoiceLabPlayer(audio, this);
            _draft = new VoiceLabDraft();
            VoiceLabDraft draft = _draft;

            Steps = new StepIndicator();
            Steps.SetSteps(VoiceLabStrings.StepBase, VoiceLabStrings.StepTimbre, VoiceLabStrings.StepBuild, VoiceLabStrings.StepTry, VoiceLabStrings.StepRegister);
            Steps.StepChosen += GoTo;
            Add(Steps);

            _timbre = new VoiceLabTimbreStep(lab, _player, draft, this);
            _register = new VoiceLabRegisterStep(lab, draft, this);
            _panels[BaseStep] = new VoiceLabBaseStep(lab, _player, draft, this);
            _panels[TimbreStep] = _timbre;
            _panels[BuildStep] = new VoiceLabBuildStep(lab, draft, streaming, this);
            _panels[TryStep] = TryPanel();
            _panels[RegisterStep] = _register;
            foreach (VisualElement panel in _panels)
            {
                panel.AddToClassList(PanelClass);
                Add(panel);
            }

            GoTo(BaseStep);
        }

        public StepIndicator Steps { get; }

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
