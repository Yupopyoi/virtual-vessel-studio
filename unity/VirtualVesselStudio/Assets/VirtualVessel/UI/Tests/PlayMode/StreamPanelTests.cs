using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Screens;
using VirtualVessel.UI.Streaming;
using Object = UnityEngine.Object;

namespace VirtualVessel.UI.Tests
{
    /// <summary>Streaming on the live and setup screens inside the real UI root (streaming detailed design 18).</summary>
    public sealed class StreamPanelTests
    {
        private string _directory;
        private GameObject _host;
        private UiRoot _root;
        private FakeStreaming _streaming;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _streaming = new FakeStreaming();
            _host = new GameObject(nameof(StreamPanelTests));
            _host.AddComponent<UIDocument>();
            _root = _host.AddComponent<UiRoot>();
            _root.SetAssets(UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset"));
            _root.Build(new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false, screens: new ScreenServices { Streaming = _streaming }));
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
        public IEnumerator GoingLive_AsksFirst_AndShowsTheShapeTextAndTime()
        {
            _streaming.SetStreamKey("key");
            StreamControl control = Open<StreamControl>();
            yield return null;
            Assert.That(control.StartButton.DisplayedText, Is.EqualTo("● 配信を開始"));

            Submit(control.StartButton);
            yield return null;
            Assert.That(_root.Dialogs.Current, Is.Not.Null, "A stray start would be public, so it is confirmed.");
            Assert.That(_streaming.Started, Is.EqualTo(0));
            Submit(_root.Dialogs.Current.Confirm);
            yield return null;
            yield return null;
            Assert.That(_streaming.Started, Is.EqualTo(1));

            _streaming.Elapsed = TimeSpan.FromSeconds(754);
            _streaming.Stats = new StreamingStats(5_900_000, 0, 0, 0);
            _streaming.SetState(StreamingState.Live);
            yield return new WaitForSeconds(0.3f);

            Assert.That((control.Chip.Status, control.Chip.TextKey, control.Chip.Number), Is.EqualTo((StatusKind.Live, StreamStrings.Live, "00:12:34")));
            Assert.That(control.StartButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(control.StopButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(control.Health.text, Does.Contain("5.9"));
        }

        [UnityTest]
        public IEnumerator Ending_AsksFirst_AndCancelKeepsStreaming()
        {
            _streaming.SetStreamKey("key");
            StreamControl control = Open<StreamControl>();
            _streaming.SetState(StreamingState.Live);
            yield return null;

            Submit(control.StopButton);
            yield return null;
            Assert.That(_root.Dialogs.Current, Is.Not.Null);
            Submit(_root.Dialogs.Current.Cancel);
            yield return null;
            yield return null;
            Assert.That(_streaming.Stopped, Is.EqualTo(0));

            Submit(control.StopButton);
            yield return null;
            Submit(_root.Dialogs.Current.Confirm);
            yield return null;
            yield return null;
            Assert.That(_streaming.Stopped, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator WithoutAKey_GoingLiveCannotBePressed()
        {
            StreamControl control = Open<StreamControl>();
            yield return null;

            Submit(control.StartButton);
            yield return null;

            Assert.That(_root.Dialogs.Current, Is.Null);
            Assert.That(_streaming.Started, Is.EqualTo(0));
        }

        [TestCase(StreamingState.Reconnecting, StreamingProblem.ConnectionLost, StatusKind.Warning, StreamStrings.ConnectionLost)]
        [TestCase(StreamingState.Failed, StreamingProblem.Rejected, StatusKind.Error, StreamStrings.Rejected)]
        [TestCase(StreamingState.Failed, StreamingProblem.ServerUnreachable, StatusKind.Error, StreamStrings.ServerUnreachable)]
        public void Trouble_HasAShapeAndSaysWhatToDo(StreamingState state, StreamingProblem problem, StatusKind chip, string message)
        {
            StreamView view = StreamControl.ViewOf(state, problem);

            Assert.That((view.Chip, view.ProblemKey), Is.EqualTo(((StatusKind?)chip, message)));
        }

        [UnityTest]
        public IEnumerator TheKey_IsEnteredMasked_AndOnlyWhetherItIsRegisteredComesBack()
        {
            StreamingCard card = Open<StreamingCard>();
            yield return null;
            Assert.That(card.KeyText.text, Is.EqualTo("未登録"));
            Assert.That(card.RemoveButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));

            Submit(card.RegisterButton);
            yield return null;
            yield return null;
            TextInput field = _root.Dialogs.Current.Q<TextInput>();
            Assert.That(field.Masked, Is.True);
            field.value = "  abcd-efgh\n";
            Submit(_root.Dialogs.Current.Confirm);
            yield return null;
            yield return null;

            Assert.That(_streaming.Key, Is.EqualTo("  abcd-efgh\n"));
            Assert.That(field.value, Is.Empty, "What was typed does not stay in the field.");
            Assert.That(card.KeyText.text, Is.EqualTo("登録済み"));
            Assert.That(card.RemoveButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(card.Query<Label>().ToList().Any(label => label.text != null && label.text.Contains("abcd")), Is.False, "The key is never shown.");
        }

        [UnityTest]
        public IEnumerator Cancel_RegistersNothing()
        {
            StreamingCard card = Open<StreamingCard>();
            yield return null;
            Submit(card.RegisterButton);
            yield return null;
            yield return null;
            _root.Dialogs.Current.Q<TextInput>().value = "typed";
            Submit(_root.Dialogs.Current.Cancel);
            yield return null;
            yield return null;

            Assert.That(_streaming.Key, Is.Null);
        }

        [UnityTest]
        public IEnumerator WhileStreaming_NothingCanChange_AndItSaysWhy()
        {
            StreamingCard card = Open<StreamingCard>();
            yield return null;
            Assert.That(card.Locked.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));

            _streaming.SetState(StreamingState.Live);
            yield return null;

            Assert.That(card.Locked.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(card.RegisterButton.enabledInHierarchy, Is.False);
            Assert.That(card.Destination.enabledInHierarchy, Is.False);
        }

        [UnityTest]
        public IEnumerator AnUnencryptedUrl_IsWarnedAbout_AndAnotherDestinationHasNoAv1()
        {
            StreamingCard card = Open<StreamingCard>();
            _streaming.SetOptions(_streaming.Options.Value.WithDestination(StreamDestinationKind.CustomRtmp).WithServerUrl("rtmp://server/live"));
            yield return null;

            Assert.That(card.UrlNote.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(card.CodecRow.DescriptionKey, Is.EqualTo(StreamingCardStrings.CodecNoAv1));

            _streaming.SetOptions(_streaming.Options.Value.WithServerUrl("nonsense"));
            yield return null;
            Assert.That(card.Url.ErrorKey, Is.EqualTo(StreamingCardStrings.UrlInvalid));
        }

        [UnityTest]
        public IEnumerator ConnectionTest_ShowsTheResult()
        {
            StreamingCard card = Open<StreamingCard>();
            yield return null;

            Submit(card.TestButton);
            yield return null;
            yield return null;
            Assert.That(card.TestResult.text, Is.EqualTo("接続できました。"));

            _streaming.TestResult = StreamingProblem.Rejected;
            Submit(card.TestButton);
            yield return null;
            yield return null;
            Assert.That(card.TestResult.text, Does.Contain("YouTube Studio"));
        }

        [Test]
        public void Bitrate_ReadsAsMegabits()
        {
            Assert.That(StreamControl.Megabits(5_930_000), Is.EqualTo("5.9"));
            Assert.That(StreamingCard.Bits(6.5f), Is.EqualTo(6_500_000));
        }

        private T Open<T>()
            where T : VisualElement
        {
            _root.Shell.Open(typeof(T) == typeof(StreamControl) ? "live" : "setup");
            T element = _root.Content.Q<T>();
            Assert.That(element, Is.Not.Null);
            return element;
        }

        private static void Submit(VisualElement target)
        {
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = target;
                target.SendEvent(submit);
            }
        }

        internal sealed class FakeStreaming : IStreamingService
        {
            private readonly ObservableValue<StreamingState> _state = new ObservableValue<StreamingState>(StreamingState.Idle);
            private readonly ObservableValue<StreamingProblem> _problem = new ObservableValue<StreamingProblem>(StreamingProblem.None);
            private readonly ObservableValue<StreamingOptions> _options = new ObservableValue<StreamingOptions>(
                new StreamingOptions(StreamDestinationKind.YouTube, "rtmps://a.rtmps.youtube.com/live2", false, StreamingCodec.Av1, true, 6_000_000));

            public IReadOnlyObservable<StreamingState> State => _state;

            public IReadOnlyObservable<StreamingProblem> Problem => _problem;

            public TimeSpan Elapsed { get; set; }

            public IReadOnlyObservable<StreamingOptions> Options => _options;

            public StreamingStats Stats { get; set; }

            public string Key { get; private set; }

            public int Started { get; private set; }

            public int Stopped { get; private set; }

            public StreamingProblem TestResult { get; set; }

            public void SetState(StreamingState state)
            {
                _state.Set(state);
            }

            public void SetOptions(StreamingOptions options)
            {
                bool av1 = options.Destination == StreamDestinationKind.YouTube;
                _options.Set(new StreamingOptions(options.Destination, options.ServerUrl, options.KeyRegistered, av1 ? options.Codec : StreamingCodec.H264, av1, options.VideoBitrate));
            }

            public bool SetStreamKey(string key)
            {
                Key = key;
                StreamingOptions options = _options.Value;
                _options.Set(new StreamingOptions(options.Destination, options.ServerUrl, !string.IsNullOrEmpty(key), options.Codec, options.Av1Available, options.VideoBitrate));
                return true;
            }

            public Task<StreamingProblem> TestConnectionAsync()
            {
                return Task.FromResult(TestResult);
            }

            public void Start()
            {
                Started++;
                _state.Set(StreamingState.Connecting);
            }

            public void Stop()
            {
                Stopped++;
            }
        }
    }
}
