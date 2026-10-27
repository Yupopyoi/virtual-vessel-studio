using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Recording;
using VirtualVessel.UI.Screens;
using Object = UnityEngine.Object;

namespace VirtualVessel.UI.Tests
{
    /// <summary>Recording on the live and setup screens inside the real UI root (recording detailed design 18).</summary>
    public sealed class RecordPanelTests
    {
        private string _directory;
        private GameObject _host;
        private UiRoot _root;
        private FakeRecording _recording;
        private FakeExplorer _explorer;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _recording = new FakeRecording();
            _explorer = new FakeExplorer();
            _host = new GameObject(nameof(RecordPanelTests));
            _host.AddComponent<UIDocument>();
            _root = _host.AddComponent<UiRoot>();
            _root.SetAssets(UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset"));
            _root.Build(new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false, screens: new ScreenServices { Recording = _recording, Explorer = _explorer }));
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_host);
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [UnityTest]
        public IEnumerator Record_StartsAtOnce_AndShowsTheElapsedTime()
        {
            RecordControl record = Open();
            yield return null;
            Assert.That(record.StartButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(record.StopButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(record.Chip.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(record.StartButton.DisplayedText, Is.EqualTo("● 録画"));

            Submit(record.StartButton);
            yield return null;

            Assert.That(_recording.Started, Is.EqualTo(1), "Starting needs no confirmation.");
            Assert.That(_root.Dialogs.Current, Is.Null);
            _recording.Elapsed = TimeSpan.FromSeconds(3725);
            yield return new WaitForSeconds(0.3f);
            Assert.That(record.StartButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(record.StopButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That((record.Chip.Status, record.Chip.TextKey, record.Chip.Number), Is.EqualTo((StatusKind.Live, RecordStrings.Recording, "01:02:05")), "A filled dot and text, never color alone.");
        }

        [UnityTest]
        public IEnumerator Stop_AsksFirst_AndCancelKeepsRecording()
        {
            RecordControl record = Open();
            _recording.Start();
            yield return null;

            Submit(record.StopButton);
            yield return null;
            ConfirmDialog dialog = _root.Dialogs.Current;
            Assert.That(dialog, Is.Not.Null, "Stopping is a Danger action behind a confirmation.");
            Assert.That(_recording.Stopped, Is.EqualTo(0));
            Submit(dialog.Cancel);
            yield return null;
            yield return null;
            Assert.That(_recording.Stopped, Is.EqualTo(0));
            Assert.That(_recording.State.Value, Is.EqualTo(RecordingState.Recording));

            Submit(record.StopButton);
            yield return null;
            Submit(_root.Dialogs.Current.Confirm);
            yield return null;
            yield return null;
            Assert.That(_recording.Stopped, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AfterStopping_TheFileIsShown_WithOpenFolder()
        {
            RecordControl record = Open();
            _recording.Start();
            yield return null;
            Assert.That(record.Saved.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));

            _recording.Finish("C:\\Videos\\VirtualVesselStudio_2026-10-27_22-30-05.mp4");
            yield return null;

            Assert.That(record.Saved.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(record.SavedText.text, Is.EqualTo("保存しました: VirtualVesselStudio_2026-10-27_22-30-05.mp4"));
            Submit(record.OpenFolderButton);
            yield return null;
            Assert.That(_explorer.Shown, Is.EqualTo("C:\\Videos\\VirtualVesselStudio_2026-10-27_22-30-05.mp4"));
        }

        [UnityTest]
        public IEnumerator WhileStarting_TheButtonSaysSo_AndDoesNotAnswer()
        {
            RecordControl record = Open();
            _recording.SetState(RecordingState.Starting);
            yield return null;

            Assert.That(record.StartButton.enabledSelf, Is.False);
            Assert.That(record.StartButton.TextKey, Is.EqualTo(RecordStrings.Starting));
            Submit(record.StartButton);
            yield return null;
            Assert.That(_recording.Started, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator AProblem_IsShownWithWhatToDo()
        {
            RecordControl record = Open();
            _recording.Fail(RecordingProblem.DiskFull);
            yield return null;

            Assert.That((record.Chip.Status, record.Chip.TextKey), Is.EqualTo((StatusKind.Error, RecordStrings.Failed)));
            Assert.That(record.Problem.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(record.Problem.text, Does.Contain("空き容量"));
            Assert.That(record.StartButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "Recording can be tried again.");
        }

        [UnityTest]
        public IEnumerator TheSetupCard_ChoosesTheQuality_AndShowsTheFolderAndSpace()
        {
            _root.Shell.Open("setup");
            yield return null;
            RecordingCard card = _root.Content.Q<RecordingCard>();
            Assert.That(card, Is.Not.Null);
            Assert.That(card.FolderText.text, Is.EqualTo("C:\\Videos\\Virtual Vessel Studio"));
            Assert.That(card.FreeText.text, Is.EqualTo("12.5 GB"));
            Assert.That(card.FormatText.text, Is.EqualTo("1920×1080・30 fps・MP4（H.264）"));
            Assert.That(card.Quality.value, Is.EqualTo(0));

            card.Quality.value = 1;
            Assert.That(_recording.Options.Value.Quality, Is.EqualTo(RecordingQuality.High));

            Submit(card.OpenButton);
            yield return null;
            Assert.That(_explorer.Shown, Is.EqualTo("C:\\Videos\\Virtual Vessel Studio"));
        }

        [Test]
        public void ViewOf_PairsEveryStateWithWhatItShows()
        {
            Assert.That(RecordControl.ViewOf(RecordingState.Idle, RecordingProblem.None).ShowStart, Is.True);
            Assert.That(RecordControl.ViewOf(RecordingState.Idle, RecordingProblem.None).Chip, Is.Null);
            Assert.That(RecordControl.ViewOf(RecordingState.Stopping, RecordingProblem.None).StartKey, Is.EqualTo(RecordStrings.Finishing));
            Assert.That(RecordControl.ViewOf(RecordingState.Recording, RecordingProblem.None).ShowStop, Is.True);
            Assert.That(RecordControl.ViewOf(RecordingState.Recording, RecordingProblem.AudioUnavailable).ProblemKey, Is.EqualTo(RecordStrings.AudioUnavailable));
            Assert.That(RecordControl.ViewOf(RecordingState.Failed, RecordingProblem.EncoderFailed).ProblemKey, Is.EqualTo(RecordStrings.EncoderFailed));
            Assert.That(RecordControl.FormatElapsed(TimeSpan.FromSeconds(59.9)), Is.EqualTo("00:00:59"));
            Assert.That(RecordControl.FormatElapsed(TimeSpan.FromHours(12)), Is.EqualTo("12:00:00"));
            Assert.That(RecordingCard.Gigabytes(1024L * 1024 * 1024 * 3 / 2), Is.EqualTo("1.5"));
        }

        private RecordControl Open()
        {
            _root.Shell.Open("live");
            return _root.Content.Q<LiveScreen>().Record;
        }

        private static void Submit(VisualElement target)
        {
            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        private sealed class FakeRecording : IRecordingService
        {
            private readonly ObservableValue<RecordingState> _state = new ObservableValue<RecordingState>(RecordingState.Idle);
            private readonly ObservableValue<RecordingProblem> _problem = new ObservableValue<RecordingProblem>(RecordingProblem.None);
            private readonly ObservableValue<RecordingOptions> _options = new ObservableValue<RecordingOptions>(new RecordingOptions("C:\\Videos\\Virtual Vessel Studio", RecordingQuality.Standard, 1920, 1080, 30));

            public int Started { get; private set; }

            public int Stopped { get; private set; }

            public IReadOnlyObservable<RecordingState> State => _state;

            public IReadOnlyObservable<RecordingProblem> Problem => _problem;

            public TimeSpan Elapsed { get; set; }

            public string LastFile { get; private set; }

            public IReadOnlyObservable<RecordingOptions> Options => _options;

            public void SetOptions(RecordingOptions options)
            {
                _options.Set(options);
            }

            public long? FreeBytes()
            {
                return 1024L * 1024 * 1024 * 25 / 2;
            }

            public void Start()
            {
                Started++;
                _state.Set(RecordingState.Recording);
            }

            public void Stop()
            {
                Stopped++;
            }

            public void SetState(RecordingState state)
            {
                _state.Set(state);
            }

            public void Finish(string file)
            {
                LastFile = file;
                _state.Set(RecordingState.Idle);
            }

            public void Fail(RecordingProblem problem)
            {
                _problem.Set(problem);
                _state.Set(RecordingState.Failed);
            }
        }

        private sealed class FakeExplorer : IFileExplorer
        {
            public string Shown { get; private set; }

            public void Show(string path)
            {
                Shown = path;
            }
        }
    }
}
