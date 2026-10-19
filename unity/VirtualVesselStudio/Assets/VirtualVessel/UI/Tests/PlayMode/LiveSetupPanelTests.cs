using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Capture;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.Tracking;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Screens;
using Object = UnityEngine.Object;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The live and setup screens inside the real UI root (docs/&lt;lang&gt;/ui/screens/).</summary>
    public sealed class LiveSetupPanelTests
    {
        private string _directory;
        private GameObject _host;
        private UiRoot _root;
        private FakeDeviceSource _game;
        private FakePointerSource _sub;
        private FakeDeviceSource _camera;
        private FakeTracking _tracking;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _game = new FakeDeviceSource("game", CaptureSourceKind.GameCapture);
            _sub = new FakePointerSource("subscreen", CaptureSourceKind.SubScreenCapture);
            _camera = new FakeDeviceSource(CaptureService.TrackingCameraId, CaptureSourceKind.Camera);
            _tracking = new FakeTracking();
            _host = new GameObject(nameof(LiveSetupPanelTests));
            _host.AddComponent<UIDocument>();
            _root = _host.AddComponent<UiRoot>();
            _root.SetAssets(UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset"));
            _root.Build(new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false, captures: new FakeSources(_game, _sub, _camera), tracking: _tracking));
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
        public IEnumerator Setup_HasACardPerSource_AboveTheAppSettings()
        {
            _root.Shell.Open("setup");
            yield return null;

            var cards = _root.Content.Query<CaptureSourceCard>().ToList();
            Assert.That(cards.Count, Is.EqualTo(2), "The tracking camera has no capture Card of its own.");
            Assert.That(cards[0].Pointer, Is.Null, "A capture board has no cursor.");
            Assert.That(cards[1].Pointer, Is.Not.Null);
            Assert.That(_root.Content.Q<AppSettingsCard>(), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator TheCardsControls_GoToTheSource_AndFollowIt()
        {
            _root.Shell.Open("setup");
            yield return null;
            CaptureSourceCard card = _root.Content.Query<CaptureSourceCard>().First();

            card.Capture.value = true;
            Assert.That(_game.Starts, Is.EqualTo(1));
            card.Devices.value = "Board B";
            Assert.That(_game.SelectedDeviceId, Is.EqualTo("Board B"));
            card.FlipVertical.value = true;
            Assert.That(_game.Flip.Value, Is.EqualTo(CaptureFlip.Vertical));

            _game.SetStatus(CaptureState.Stopped, CaptureProblem.DeviceNotChosen);
            Assert.That(card.Status.Status, Is.EqualTo(StatusKind.Warning));
            Assert.That(card.ProblemKey, Is.EqualTo(CaptureStrings.ProblemChoose));
            Assert.That(card.Capture.value, Is.False, "The toggle follows the state.");

            _game.SetStatus(CaptureState.Capturing, CaptureProblem.None);
            _game.SetInfo(new CaptureSourceInfo(1920, 1080, 59.94, "Board B"));
            Assert.That(card.Status.Status, Is.EqualTo(StatusKind.Success));
            Assert.That(card.Capture.value, Is.True);
            Assert.That(card.Info.text, Is.EqualTo("1920×1080 · 59.9 fps"));

            var video = new Texture2D(4, 4);
            _game.SetTexture(video);
            Assert.That(card.Preview.image, Is.SameAs(video), "The small preview shows the source's video.");
            _game.SetTexture(null);
            Assert.That(card.Preview.image, Is.Null);
            Object.Destroy(video);
        }

        [UnityTest]
        public IEnumerator TheTrackingCard_StartsTracking_AndChoosesItsCamera()
        {
            _root.Shell.Open("setup");
            yield return null;
            TrackingCard card = _root.Content.Q<TrackingCard>();
            Assert.That(card, Is.Not.Null);

            card.Track.value = true;
            Assert.That(_tracking.State.Value, Is.EqualTo(TrackingState.Running));
            card.Devices.value = "Board A";
            Assert.That(_camera.SelectedDeviceId, Is.EqualTo("Board A"));

            _tracking.Set(TrackingState.Unavailable, TrackingProblem.CameraNotChosen);
            Assert.That(card.Status.Status, Is.EqualTo(StatusKind.Warning));
            Assert.That(card.ProblemKey, Is.EqualTo(TrackingStrings.ProblemChoose));
            Assert.That(card.Track.value, Is.True, "Still on while it waits for the camera.");

            Assert.That(card.MirrorToggle.value, Is.True, "Mirrored by default.");
            Assert.That(card.Preview.resolvedStyle.scale.value.x, Is.EqualTo(-1f), "The image and points flip together.");
            card.MirrorToggle.value = false;
            Assert.That(_tracking.Options.Value.Mirror, Is.False);

            var video = new Texture2D(4, 4);
            _camera.SetTexture(video);
            Assert.That(card.Preview.image, Is.SameAs(video), "The camera image is shown to check the points on.");
            Object.Destroy(video);
        }

        [Test]
        public void TrackingDescribe_PairsEveryStateWithAShapeAndText()
        {
            Assert.That(TrackingCard.Describe(TrackingState.Stopped, TrackingProblem.None), Is.EqualTo((StatusKind.Idle, TrackingStrings.Stopped, (string)null)));
            Assert.That(TrackingCard.Describe(TrackingState.Running, TrackingProblem.None).Kind, Is.EqualTo(StatusKind.Success));
            Assert.That(TrackingCard.Describe(TrackingState.Running, TrackingProblem.NoPerson), Is.EqualTo((StatusKind.Warning, TrackingStrings.NoPerson, TrackingStrings.ProblemNoPerson)));
            Assert.That(TrackingCard.Describe(TrackingState.Error, TrackingProblem.CameraInUse).ProblemKey, Is.EqualTo(TrackingStrings.ProblemInUse));
            Assert.That(TrackingCard.Describe(TrackingState.Error, TrackingProblem.StartFailed).Kind, Is.EqualTo(StatusKind.Error));
            Assert.That(CaptureSourceCard.Describe(CaptureState.Error, CaptureProblem.DeviceInUse).ProblemKey, Is.EqualTo(CaptureStrings.ProblemInUse));
        }

        [Test]
        public void ThePartsLine_MarksEachPartWithAShapeAndAWord()
        {
            var frame = new TrackingFrame();
            frame.Face.Status = TrackingStatus.Tracked;
            frame.Body.Status = TrackingStatus.LowConfidence;
            frame.LeftHand.Status = TrackingStatus.Lost;
            frame.RightHand.Status = TrackingStatus.Disabled;

            Assert.That(TrackingCard.PartsLine(frame, true, key => key.Substring(key.LastIndexOf('.') + 1)), Is.EqualTo("face ✓   body ✓   lefthand –   righthand ×"));
            Assert.That(TrackingCard.PartsLine(frame, false, key => key), Is.Empty, "Nothing while tracking is off.");
        }

        [Test]
        public void TheLoadLine_ShowsNumbers_AndADashForAnUnknownGpu()
        {
            Assert.That(TrackingCard.LoadValues(new AppLoad(59.6, 12.4, 31.2), 29.8), Is.EqualTo(("60", "30", "12%", "31%")));
            Assert.That(TrackingCard.LoadValues(new AppLoad(60, 5, double.NaN), 0).Gpu, Is.EqualTo("—"));
        }

        [Test]
        public void ThePoints_FollowTheImageAsThePreviewFitsIt()
        {
            Rect wide = TrackingOverlay.FitRect(256, 144, 1280, 720);
            Assert.That(wide, Is.EqualTo(new Rect(0, 0, 256, 144)));

            Rect narrow = TrackingOverlay.FitRect(256, 144, 640, 480);
            Assert.That(narrow.width, Is.EqualTo(192f).Within(1e-4f));
            Assert.That(narrow.x, Is.EqualTo(32f).Within(1e-4f), "Centered, with bars on the sides.");
        }

        [UnityTest]
        public IEnumerator Live_ShowsThePreview_AndTheLayers()
        {
            _root.Shell.Open("live");
            yield return null;
            yield return null;

            var live = _root.Content.Q<LiveScreen>();
            Assert.That(live, Is.Not.Null);
            Assert.That(live.Preview.worldBound.height, Is.EqualTo(live.Preview.worldBound.width * 9f / 16f).Within(1f), "The preview takes the stream's shape.");
            Assert.That(live.Layers, Is.Not.Null);
        }

        [Test]
        public void Describe_PairsEveryStateWithAShapeAndText()
        {
            Assert.That(CaptureSourceCard.Describe(CaptureState.Stopped, CaptureProblem.None), Is.EqualTo((StatusKind.Idle, CaptureStrings.Stopped, (string)null)));
            Assert.That(CaptureSourceCard.Describe(CaptureState.Ready, CaptureProblem.NoSignal).Kind, Is.EqualTo(StatusKind.Warning));
            Assert.That(CaptureSourceCard.Describe(CaptureState.Unavailable, CaptureProblem.MonitorNotFound), Is.EqualTo((StatusKind.Warning, CaptureStrings.NotFound, CaptureStrings.ProblemNotFound)));
            Assert.That(CaptureSourceCard.Describe(CaptureState.Error, CaptureProblem.PluginMissing), Is.EqualTo((StatusKind.Error, CaptureStrings.Failed, CaptureStrings.ProblemFailed)), "Users see a missing plugin as a failure.");
            Assert.That(CaptureSourceCard.InfoText(default), Is.EqualTo("—"));
        }

        private class FakeDeviceSource : ICaptureDeviceSource
        {
            private readonly ObservableValue<CaptureState> _state = new ObservableValue<CaptureState>(CaptureState.Stopped);
            private readonly ObservableValue<CaptureProblem> _problem = new ObservableValue<CaptureProblem>(CaptureProblem.None);
            private readonly ObservableValue<CaptureSourceInfo> _info = new ObservableValue<CaptureSourceInfo>(default);
            private readonly ObservableValue<CaptureFlip> _flip = new ObservableValue<CaptureFlip>(CaptureFlip.None);

            public FakeDeviceSource(string id, CaptureSourceKind kind)
            {
                Id = id;
                Kind = kind;
            }

            public string Id { get; }

            public CaptureSourceKind Kind { get; }

            public int Starts { get; private set; }

            public IReadOnlyObservable<CaptureState> State => _state;

            public IReadOnlyObservable<CaptureProblem> Problem => _problem;

            public IReadOnlyObservable<CaptureSourceInfo> Info => _info;

            private readonly ObservableValue<Texture> _texture = new ObservableValue<Texture>(null);

            public IReadOnlyObservable<Texture> Texture => _texture;

            public long FrameCount => 0;

            public long LastFrameTimestamp => 0;

            public void SetTexture(Texture texture)
            {
                _texture.Value = texture;
            }

            public IReadOnlyObservable<CaptureFlip> Flip => _flip;

            public IReadOnlyList<CaptureDevice> Devices { get; } = new[] { new CaptureDevice("Board A", "Board A"), new CaptureDevice("Board B", "Board B") };

            public string SelectedDeviceId { get; private set; }

            public void SetFlip(CaptureFlip flip)
            {
                _flip.Value = flip;
            }

            public void Start()
            {
                Starts++;
            }

            public void Stop()
            {
            }

            public void RefreshDevices()
            {
            }

            public void SelectDevice(string deviceId)
            {
                SelectedDeviceId = deviceId;
            }

            public void SetStatus(CaptureState state, CaptureProblem problem)
            {
                _problem.Value = problem;
                _state.Value = state;
            }

            public void SetInfo(CaptureSourceInfo info)
            {
                _info.Value = info;
            }
        }

        private sealed class FakePointerSource : FakeDeviceSource, IPointerOption
        {
            private readonly ObservableValue<bool> _pointer = new ObservableValue<bool>(true);

            public FakePointerSource(string id, CaptureSourceKind kind)
                : base(id, kind)
            {
            }

            public IReadOnlyObservable<bool> DrawPointer => _pointer;

            public void SetDrawPointer(bool draw)
            {
                _pointer.Value = draw;
            }
        }

        private sealed class FakeTracking : ITrackingSource
        {
            private readonly ObservableValue<TrackingState> _state = new ObservableValue<TrackingState>(TrackingState.Stopped);
            private readonly ObservableValue<TrackingProblem> _problem = new ObservableValue<TrackingProblem>(TrackingProblem.None);
            private readonly ObservableValue<TrackingOptions> _options = new ObservableValue<TrackingOptions>(TrackingOptions.Default);

            public IReadOnlyObservable<TrackingState> State => _state;

            public IReadOnlyObservable<TrackingProblem> Problem => _problem;

            public TrackingRegions Supported => TrackingRegions.All;

            public IReadOnlyObservable<TrackingOptions> Options => _options;

            public TrackingFrame Current { get; } = new TrackingFrame();

            public double ObservationsPerSecond => 30;

            public void Start()
            {
                Set(TrackingState.Running, TrackingProblem.None);
            }

            public void Stop()
            {
                Set(TrackingState.Stopped, TrackingProblem.None);
            }

            public void SetOptions(TrackingOptions options)
            {
                _options.Value = options;
            }

            public void Set(TrackingState state, TrackingProblem problem)
            {
                _problem.Value = problem;
                _state.Value = state;
            }
        }

        private sealed class FakeSources : ICaptureSources
        {
            public FakeSources(params IVideoCaptureSource[] sources)
            {
                Sources = sources;
            }

            public IReadOnlyList<IVideoCaptureSource> Sources { get; }

            public IVideoCaptureSource Find(string id)
            {
                foreach (IVideoCaptureSource source in Sources)
                {
                    if (source.Id == id)
                    {
                        return source;
                    }
                }

                return null;
            }
        }
    }
}
