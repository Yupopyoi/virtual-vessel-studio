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

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _game = new FakeDeviceSource("game", CaptureSourceKind.GameCapture);
            _sub = new FakePointerSource("subscreen", CaptureSourceKind.SubScreenCapture);
            _host = new GameObject(nameof(LiveSetupPanelTests));
            _host.AddComponent<UIDocument>();
            _root = _host.AddComponent<UiRoot>();
            _root.SetAssets(UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset"));
            _root.Build(new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false, captures: new FakeSources(_game, _sub)));
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
            Assert.That(cards.Count, Is.EqualTo(2));
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

            public IReadOnlyObservable<Texture> Texture { get; } = new ObservableValue<Texture>(null);

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
