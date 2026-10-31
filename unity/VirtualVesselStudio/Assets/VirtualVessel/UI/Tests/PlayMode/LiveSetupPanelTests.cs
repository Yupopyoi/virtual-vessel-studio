using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Capture;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.Tracking;
using VirtualVessel.Tracking.Recording;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Credits;
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
            _root.Build(new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false, screens: new ScreenServices { Captures = new FakeSources(_game, _sub, _camera), Tracking = _tracking, Credits = new FakeCredits() }));
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
        public IEnumerator Setup_ShowsOnePageAtATime_AndRemembersIt()
        {
            _root.Shell.Open("setup");
            yield return null;
            var screen = (SetupScreen)_root.Shell.Host.GetScreen("setup");
            CaptureSourceCard capture = screen.Q<CaptureSourceCard>();
            Assert.That(screen.Tabs.value, Is.EqualTo(SetupScreen.VideoPage));
            Assert.That(capture.resolvedStyle.width, Is.GreaterThan(0f));
            Assert.That(screen.Q<TrackingCard>().parent.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));

            // Measured on the real styles: everything on a page keeps M (16px) from the next, whichever module built it.
            List<VisualElement> items = capture.parent.Children().ToList();
            Assert.That(items.Count, Is.EqualTo(2));
            Assert.That(items[1].worldBound.yMin - items[0].worldBound.yMax, Is.EqualTo(16f).Within(0.5f));

            screen.Tabs.value = SetupScreen.AvatarPage;
            yield return null;
            Assert.That(capture.parent.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(screen.Q<TrackingCard>().parent.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(_root.Settings.LastSetupPage, Is.EqualTo("avatar"));
        }

        [Test]
        public void PageOf_FallsBackToTheFirstPage_ForAnIdNotKnown()
        {
            Assert.That(SetupScreen.PageOf("output"), Is.EqualTo(SetupScreen.OutputPage));
            Assert.That(SetupScreen.PageOf("gone"), Is.EqualTo(SetupScreen.VideoPage));
            Assert.That(SetupScreen.PageOf(null), Is.EqualTo(SetupScreen.VideoPage));
        }

        [UnityTest]
        public IEnumerator TheTrackingCard_ChoosesTheMovementSpeed_AndFollowsIt()
        {
            _root.Shell.Open("setup");
            yield return null;
            TrackingCard card = _root.Content.Q<TrackingCard>();
            Assert.That(card.Speed.value, Is.EqualTo((float)SmoothingLevel.Default));

            card.Speed.value = 8f;
            Assert.That(_tracking.Options.Value.Smoothing, Is.EqualTo(8));

            _tracking.SetOptions(_tracking.Options.Value.WithSmoothing(2));
            Assert.That(card.Speed.value, Is.EqualTo(2f), "Follows a change made elsewhere.");

            Assert.That(TrackingCard.LevelOf(7.5f), Is.EqualTo(8));
            Assert.That(TrackingCard.LevelOf(0f), Is.EqualTo(SmoothingLevel.Min));
            Assert.That(TrackingCard.LevelOf(12f), Is.EqualTo(SmoothingLevel.Max));
        }

        [UnityTest]
        public IEnumerator TheTrackingCard_ChoosesTwoOrThreeD_AndFollowsIt()
        {
            _root.Shell.Open("setup");
            yield return null;
            TrackingCard card = _root.Content.Q<TrackingCard>();
            Assert.That(card.Depth.value, Is.EqualTo(0), "3D by default.");

            card.Depth.value = 1;
            Assert.That(_tracking.Options.Value.BodyDepth, Is.EqualTo(BodyDepth.Flat));

            _tracking.SetOptions(_tracking.Options.Value.WithBodyDepth(BodyDepth.Tracked));
            Assert.That(card.Depth.value, Is.EqualTo(0), "Follows a change made elsewhere.");
        }

        [UnityTest]
        public IEnumerator TheTrackingCard_ChoosesTheRangePartsAndModel_AndFollowsThem()
        {
            _root.Shell.Open("setup");
            yield return null;
            TrackingCard card = _root.Content.Q<TrackingCard>();
            Assert.That(card.Range.value, Is.EqualTo(0), "The upper body.");
            Assert.That(card.Range.IsItemEnabled(1), Is.False, "Full body is coming soon and cannot be chosen.");
            Assert.That(new[] { card.FaceToggle.value, card.BodyToggle.value, card.HandsToggle.value }, Has.All.True);
            Assert.That(card.Model.value, Is.EqualTo((int)PoseModel.Full));

            card.HandsToggle.value = false;
            Assert.That(_tracking.Options.Value.Regions, Is.EqualTo(TrackingRegions.Face | TrackingRegions.Body));
            card.Model.value = (int)PoseModel.Lite;
            Assert.That(_tracking.Options.Value.PoseModel, Is.EqualTo(PoseModel.Lite));
            Assert.That(card.Model.ItemKeys, Is.EqualTo($"{TrackingStrings.ModelLite},{TrackingStrings.ModelFull}"), "Heavy is not offered.");

            _tracking.SetOptions(_tracking.Options.Value.WithRegions(TrackingRegions.Body).WithPoseModel(PoseModel.Full));
            Assert.That(card.FaceToggle.value, Is.False, "Follows a change made elsewhere.");
            Assert.That(card.HandsToggle.value, Is.False);
            Assert.That(card.Model.value, Is.EqualTo((int)PoseModel.Full));

            Assert.That(TrackingCard.RegionsOf(true, false, true), Is.EqualTo(TrackingRegions.Face | TrackingRegions.Hands));
            Assert.That(TrackingCard.RegionsOf(false, false, false), Is.EqualTo(TrackingRegions.None));
        }

        [UnityTest]
        public IEnumerator TheCredits_ListTheShippedComponents_AndShowAFullTextOnRequest()
        {
            _root.Shell.Open("setup");
            yield return null;
            CreditsCard credits = _root.Shell.Host.GetScreen("setup").Q<CreditsCard>();
            Assert.That(credits.parent.Children().Last(), Is.SameAs(credits), "The credits come last.");
            Assert.That(credits.Rows.Select(row => row.Entry.Name), Is.EqualTo(new[] { "App", "Plugin", "Inside", "Tool part" }));
            Assert.That(credits.Rows[2].Row.ClassListContains(CreditsCard.NestedClass), Is.True, "A component built into another is indented.");
            Assert.That(credits.Rows[2].Toggle, Is.Null, "No full text, no button.");

            CreditsCard.CreditRow plugin = credits.Rows[1];
            Assert.That(plugin.Shown, Is.False);
            Assert.That(plugin.Text.Query<Label>().ToList(), Is.Empty, "Texts are read when first shown.");
            Submit(plugin.Toggle);
            yield return null;
            Assert.That(plugin.Shown, Is.True);
            Assert.That(plugin.Toggle.TextKey, Is.EqualTo(CreditStrings.Hide));
            List<Label> labels = plugin.Text.Query<Label>().ToList();
            Assert.That(labels.Select(label => label.text), Is.EqualTo(new[] { "licenses/a.md", "<b>MIT</b> text", "licenses/gone.md", _root.Localizer.Get(CreditStrings.Missing) }), "Each file is named when there are several; a missing one says so.");
            Assert.That(labels[1].enableRichText, Is.False, "License texts are shown as written.");

            Submit(plugin.Toggle);
            Assert.That(plugin.Shown, Is.False);
            Assert.That(plugin.Toggle.TextKey, Is.EqualTo(CreditStrings.Show));
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

        [UnityTest]
        public IEnumerator Live_ShowsTheArmsAndHandsInView_WhileTrackingRuns()
        {
            _root.Shell.Open("live");
            yield return null;
            LimbStatusRow limbs = _root.Content.Q<LiveScreen>().Limbs;
            Assert.That(limbs, Is.Not.Null);
            Assert.That(limbs.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "Hidden while tracking is stopped.");

            // Both arms out sideways, the left wrist below the image; the right hand seen.
            SyntheticMotion.WriteArms(_tracking.Current.Body, Vector3.left, Vector3.left, Vector3.right, Vector3.right, 1.0);
            _tracking.Current.Body.ImagePositions[(int)BodyJoint.LeftWrist] = new Vector2(0.2f, 1.1f);
            _tracking.Current.RightHand.Status = TrackingStatus.Tracked;
            _tracking.Start();
            yield return new WaitForSeconds(0.3f);

            Assert.That(limbs.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That((limbs.RightArm.Status, limbs.RightArm.TextKey), Is.EqualTo((StatusKind.Success, LimbStrings.RightArmSeen)));
            Assert.That((limbs.LeftArm.Status, limbs.LeftArm.TextKey), Is.EqualTo((StatusKind.Warning, LimbStrings.LeftArmPartly)), "The wrist is out of the image.");
            Assert.That((limbs.RightHand.Status, limbs.RightHand.TextKey), Is.EqualTo((StatusKind.Success, LimbStrings.RightHandSeen)));
            Assert.That((limbs.LeftHand.Status, limbs.LeftHand.TextKey), Is.EqualTo((StatusKind.Idle, LimbStrings.LeftHandUnseen)));
        }

        [Test]
        public void ALimb_IsSeen_AsTheAvatarUsesIt()
        {
            var body = new BodyPose();
            Assert.That(LimbStatusRow.Arm(body, left: true), Is.EqualTo(LimbSeen.Unseen), "No body.");
            SyntheticMotion.WriteArms(body, Vector3.left, Vector3.left, Vector3.right, Vector3.right, 1.0);
            Assert.That(LimbStatusRow.Arm(body, left: true), Is.EqualTo(LimbSeen.Seen));
            body.JointConfidence[(int)BodyJoint.LeftWrist] = 0.1f;
            Assert.That(LimbStatusRow.Arm(body, left: true), Is.EqualTo(LimbSeen.Partly), "A hidden wrist.");
            body.ImagePositions[(int)BodyJoint.LeftElbow] = new Vector2(-0.1f, 0.5f);
            Assert.That(LimbStatusRow.Arm(body, left: true), Is.EqualTo(LimbSeen.Unseen), "An elbow out of the image.");
            Assert.That(LimbStatusRow.Arm(body, left: false), Is.EqualTo(LimbSeen.Seen), "Each side on its own.");
            Assert.That(LimbStatusRow.Hand(null), Is.EqualTo(LimbSeen.Unseen));
            Assert.That(new[] { LimbSeen.Seen, LimbSeen.Partly, LimbSeen.Unseen }.Select(LimbStatusRow.KindOf), Is.EqualTo(new[] { StatusKind.Success, StatusKind.Warning, StatusKind.Idle }), "A shape for each, never color alone.");
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

        private static void Click(VisualElement target)
        {
            Pointer(target, EventType.MouseDown);
            Pointer(target, EventType.MouseUp);
        }

        private static void Pointer(VisualElement target, EventType type)
        {
            var system = new Event { type = type, mousePosition = target.worldBound.center, button = 0, clickCount = 1 };
            using (EventBase evt = type == EventType.MouseDown ? PointerDownEvent.GetPooled(system) : PointerUpEvent.GetPooled(system))
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        private static void Submit(VisualElement target)
        {
            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        private sealed class FakeCredits : ICreditFiles
        {
            private readonly Dictionary<string, string> _files = new Dictionary<string, string>
            {
                [CreditFiles.SbomPath] = @"{ ""bomFormat"": ""CycloneDX"",
                    ""metadata"": { ""component"": { ""name"": ""App"", ""version"": ""0.1.0"", ""licenses"": [ { ""license"": { ""id"": ""Apache-2.0"" } } ] } },
                    ""components"": [
                        { ""name"": ""Plugin"", ""version"": ""1.0"", ""licenses"": [ { ""license"": { ""id"": ""MIT"" } } ],
                          ""properties"": [
                            { ""name"": ""virtual-vessel-studio:license-file"", ""value"": ""licenses/a.md"" },
                            { ""name"": ""virtual-vessel-studio:license-file"", ""value"": ""licenses/gone.md"" } ],
                          ""components"": [ { ""name"": ""Inside"" } ] },
                        { ""name"": ""Tool"", ""scope"": ""excluded"" },
                        { ""name"": ""Tool part"" } ] }",
                ["licenses/a.md"] = "<b>MIT</b> text",
            };

            public string ReadText(string packagePath)
            {
                return _files.TryGetValue(packagePath, out string text) ? text : null;
            }
        }

        internal class FakeDeviceSource : ICaptureDeviceSource
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

        internal sealed class FakeTracking : ITrackingSource
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
