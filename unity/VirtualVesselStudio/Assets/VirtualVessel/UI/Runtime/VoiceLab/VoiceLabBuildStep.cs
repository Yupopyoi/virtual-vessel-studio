using System;
using System.Threading;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Streaming;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// STEP 3, make it (system design 14.16): training speech in the shaped voice, then training, with
    /// progress and Stop. It goes on while other screens are open, and does not start during a stream.
    /// </summary>
    public sealed class VoiceLabBuildStep : VisualElement
    {
        private const long RefreshMs = 500;

        private readonly IVoiceLab _lab;
        private readonly VoiceLabDraft _draft;
        private readonly IStreamingService _streaming;
        private readonly IVoiceLabFlow _flow;
        private readonly VisualElement _ready;
        private readonly VisualElement _running;
        private readonly ProgressMeter _progress;
        private readonly VisualElement _streamingNote;
        private readonly StatusChip _result;
        private readonly KeyedText _note;
        private CancellationTokenSource _building;
        private IVisualElementScheduledItem _refresh;
        private IDisposable _streamingSubscription;

        /// <param name="streaming">Optional; without it a build is never held back for a stream.</param>
        public VoiceLabBuildStep(IVoiceLab lab, VoiceLabDraft draft, IStreamingService streaming, IVoiceLabFlow flow)
        {
            _lab = lab ?? throw new ArgumentNullException(nameof(lab));
            _draft = draft ?? throw new ArgumentNullException(nameof(draft));
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            _streaming = streaming;
            Add(VoiceLabScreen.Lead(VoiceLabStrings.BuildLead));

            Card card = Ui.Card(VoiceLabStrings.StepBuild);
            Label note = QuietText.Of(new Label());
            note.AddToClassList(VoiceLabScreen.NoteClass);
            note.AddToClassList("vv-text-body");
            _note = new KeyedText(note, value => note.text = value) { Key = NoteKey(_draft.TrainingLength) };
            card.Add(note);

            // Folded: most people keep the default; a longer training keeps more of the voice's texture (decision
            // record, 2026-11-08).
            Card details = Ui.Card(VoiceLabStrings.BuildDetails, collapsible: true);
            details.Expanded = false;
            LengthChooser = Ui.Segmented(VoiceLabStrings.BuildLengthShort, VoiceLabStrings.BuildLengthNormal, VoiceLabStrings.BuildLengthLong);
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

            _streamingNote = new VisualElement();
            _streamingNote.Add(Ui.Chip(StatusKind.Warning, VoiceLabStrings.BuildStreaming));
            _streamingNote.Add(Ui.Text(VoiceLabStrings.BuildStreamingBody, VoiceLabScreen.NoteClass, "vv-text-body"));
            card.Add(_streamingNote);

            _result = new StatusChip(StatusKind.Error, VoiceLabStrings.BuildFailed);
            card.Add(_result);

            StartButton = Ui.Button(VoiceLabStrings.BuildStart, ButtonVariant.Primary);
            StartButton.clicked += Build;
            _ready = VoiceLabScreen.Actions(StartButton);
            card.Add(_ready);

            _running = new VisualElement();
            _progress = Ui.Progress(VoiceLabStrings.BuildPreparing);
            _running.Add(_progress);
            StopButton = Ui.Button(VoiceLabStrings.Stop);
            StopButton.clicked += () => _building?.Cancel();
            _running.Add(VoiceLabScreen.Actions(StopButton));
            card.Add(_running);
            Add(card);

            ShowRunning(false);
            _result.style.display = DisplayStyle.None;
            RegisterCallback<AttachToPanelEvent>(_ => OnAttached());
            RegisterCallback<DetachFromPanelEvent>(_ => OnDetached());
        }

        public ActionButton StartButton { get; }

        public Segmented LengthChooser { get; }

        /// <summary>The time it takes, for a training length. Pure.</summary>
        public static string NoteKey(VoiceLabTrainingLength length)
        {
            switch (length)
            {
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

        private bool Streaming => _streaming != null && VoiceLabView.IsStreaming(_streaming.State.Value);

        private void OnAttached()
        {
            _streamingSubscription = _streaming?.State.Subscribe(_ => ShowStreaming());
            ShowStreaming();
        }

        private void OnDetached()
        {
            _streamingSubscription?.Dispose();
            _streamingSubscription = null;
        }

        private async void Build()
        {
            VoiceLabCandidate candidate = _draft.Candidate;
            if (_building != null || candidate == null || Streaming)
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

            ShowStreaming();
        }

        private void ShowStreaming()
        {
            bool streaming = Streaming;
            _streamingNote.style.display = streaming && _building == null ? DisplayStyle.Flex : DisplayStyle.None;
            StartButton.SetEnabled(!streaming);
        }
    }
}
