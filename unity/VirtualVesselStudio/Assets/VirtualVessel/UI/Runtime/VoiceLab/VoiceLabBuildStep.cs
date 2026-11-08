using System;
using System.Threading;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// STEP 3, make it (system design 14.16): training speech in the shaped voice, then training, with
    /// progress and Stop. It goes on while other screens are open. During a stream or a recording the whole screen
    /// is locked (VoiceLabScreen), so this step does not check for them.
    /// </summary>
    public sealed class VoiceLabBuildStep : VisualElement
    {
        private const long RefreshMs = 500;

        private readonly IVoiceLab _lab;
        private readonly VoiceLabDraft _draft;
        private readonly IVoiceLabFlow _flow;
        private readonly VisualElement _ready;
        private readonly VisualElement _running;
        private readonly ProgressMeter _progress;
        private readonly StatusChip _result;
        private readonly KeyedText _note;
        private CancellationTokenSource _building;
        private IVisualElementScheduledItem _refresh;

        public VoiceLabBuildStep(IVoiceLab lab, VoiceLabDraft draft, IVoiceLabFlow flow)
        {
            _lab = lab ?? throw new ArgumentNullException(nameof(lab));
            _draft = draft ?? throw new ArgumentNullException(nameof(draft));
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            Add(VoiceLabScreen.Lead(VoiceLabStrings.BuildLead));

            Card card = Ui.Card(VoiceLabStrings.StepBuild);
            Label note = QuietText.Of(new Label());
            note.AddToClassList(VoiceLabScreen.NoteClass);
            note.AddToClassList("vv-text-body");
            note.AddToClassList(VoiceLabScreen.GapClass);
            _note = new KeyedText(note, value => note.text = value) { Key = NoteKey(_draft.TrainingLength) };
            card.Add(note);

            // Folded: most people keep the default; a longer training keeps more of the voice's texture (decision
            // record, 2026-11-08).
            Card details = Ui.Card(VoiceLabStrings.BuildDetails, collapsible: true);
            details.Expanded = false;
            details.AddToClassList(VoiceLabScreen.GapClass);
            LengthChooser = Ui.Segmented(LengthKeys(InEditor));
            LengthChooser.SetValueWithoutNotify((int)_draft.TrainingLength);
            LengthChooser.RegisterValueChangedCallback(evt =>
            {
                _draft.TrainingLength = (VoiceLabTrainingLength)evt.newValue;
                _note.Key = NoteKey(_draft.TrainingLength);
            });
            SettingRow length = Ui.SettingRow(VoiceLabStrings.BuildLength, VoiceLabStrings.BuildLengthDescription);
            length.Add(LengthChooser);
            details.Add(length);
            card.Add(details);

            _result = new StatusChip(StatusKind.Error, VoiceLabStrings.BuildFailed);
            _result.AddToClassList(VoiceLabScreen.GapClass);
            card.Add(_result);

            StartButton = Ui.Button(VoiceLabStrings.BuildStart, ButtonVariant.Primary);
            StartButton.clicked += Build;
            _ready = VoiceLabScreen.Actions(StartButton);
            card.Add(_ready);

            _running = new VisualElement();
            _progress = Ui.Progress(VoiceLabStrings.BuildPreparing);
            _progress.AddToClassList(VoiceLabScreen.GapClass);
            _running.Add(_progress);
            StopButton = Ui.Button(VoiceLabStrings.Stop);
            StopButton.clicked += () => _building?.Cancel();
            _running.Add(VoiceLabScreen.Actions(StopButton));
            card.Add(_running);
            Add(card);

            ShowRunning(false);
            _result.style.display = DisplayStyle.None;
        }

        public ActionButton StartButton { get; }

        public Segmented LengthChooser { get; }

#if UNITY_EDITOR
        private const bool InEditor = true;
#else
        // Built players leave the test length out altogether.
        private const bool InEditor = false;
#endif

        /// <summary>The training lengths to choose from, in the order of <see cref="VoiceLabTrainingLength"/>; the test one in the Editor only. Pure.</summary>
        public static string[] LengthKeys(bool inEditor)
        {
            return inEditor
                ? new[] { VoiceLabStrings.BuildLengthShort, VoiceLabStrings.BuildLengthNormal, VoiceLabStrings.BuildLengthLong, VoiceLabStrings.BuildLengthTest }
                : new[] { VoiceLabStrings.BuildLengthShort, VoiceLabStrings.BuildLengthNormal, VoiceLabStrings.BuildLengthLong };
        }

        /// <summary>The time it takes, for a training length. Pure.</summary>
        public static string NoteKey(VoiceLabTrainingLength length)
        {
            switch (length)
            {
                case VoiceLabTrainingLength.EditorTest:
                    return VoiceLabStrings.BuildNoteTest;
                case VoiceLabTrainingLength.Normal:
                    return VoiceLabStrings.BuildNoteNormal;
                case VoiceLabTrainingLength.Long:
                    return VoiceLabStrings.BuildNoteLong;
                default:
                    return VoiceLabStrings.BuildNote;
            }
        }

        public ActionButton StopButton { get; }

        public bool IsBuilding => _building != null;

        private async void Build()
        {
            VoiceLabCandidate candidate = _draft.Candidate;
            if (_building != null || candidate == null)
            {
                return;
            }

            _building = new CancellationTokenSource();
            _result.style.display = DisplayStyle.None;
            ShowRunning(true);
            _flow.Lock(true);
            _refresh = schedule.Execute(Refresh).Every(RefreshMs);
            VoiceLabBuild build = null;
            try
            {
                build = await _lab.BuildAsync(candidate, _draft.Timbre, _draft.TrainingLength, _building.Token);
            }
            catch (OperationCanceledException)
            {
                build = new VoiceLabBuild(null, VoiceLabBuildState.Cancelled, false);
            }
            catch (VoiceLabFailedException)
            {
                // The service has logged why; build stays null and reads as a failure.
            }
            finally
            {
                _refresh.Pause();
                _refresh = null;
                _building.Dispose();
                _building = null;
                ShowRunning(false);
                _flow.Lock(false);
            }

            string resultKey = VoiceLabView.BuildResultKey(build);
            if (resultKey == null)
            {
                _draft.RunId = build.RunId;
                _draft.TryFolder = null;
                _flow.GoTo(VoiceLabScreen.TryStep);
                return;
            }

            _result.Status = build?.State == VoiceLabBuildState.Cancelled ? StatusKind.Idle : StatusKind.Error;
            _result.TextKey = resultKey;
            _result.style.display = DisplayStyle.Flex;
        }

        private void Refresh()
        {
            VoiceLabBuildProgress progress = _lab.BuildProgress;
            string stage = VoiceLabView.BuildStageKey(progress.State);
            if (stage != null)
            {
                _progress.LabelKey = stage;
            }

            _progress.Progress = progress.Fraction;
            _progress.Detail = VoiceLabView.PercentText(progress.Fraction);
        }

        private void ShowRunning(bool running)
        {
            _running.style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
            _ready.style.display = running ? DisplayStyle.None : DisplayStyle.Flex;
            LengthChooser.SetEnabled(!running);
            if (running)
            {
                _progress.LabelKey = VoiceLabStrings.BuildPreparing;
                _progress.Progress = 0f;
                _progress.Detail = VoiceLabView.PercentText(0f);
            }

        }
    }
}
