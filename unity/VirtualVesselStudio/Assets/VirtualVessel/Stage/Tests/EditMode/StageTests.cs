using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Capture;
using VirtualVessel.Core.Geometry;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Lighting;
using VirtualVessel.Video;

namespace VirtualVessel.Stage.Tests
{
    public sealed class ScreenPlacementMathTests
    {
        [TestCase(CaptureSourceKind.GameCapture, true)]
        [TestCase(CaptureSourceKind.SubScreenCapture, true)]
        [TestCase(CaptureSourceKind.Camera, false)]
        public void TheTrackingCamera_NeverGoesOnTheStream(CaptureSourceKind kind, bool shown)
        {
            Assert.That(StageService.ShowsOnStream(kind), Is.EqualTo(shown));
        }

        [Test]
        public void TheQuad_CoversThePlacement_UprightOnTheLayer()
        {
            (Vector3 position, Vector3 scale) = ScreenPlacementMath.Quad(new NormalizedRect(0.1, 0.2, 0.4, 0.3), order: 2);

            Assert.That(position.x, Is.EqualTo(0.3f).Within(1e-6));
            Assert.That(position.y, Is.EqualTo(0.35f).Within(1e-6));
            Assert.That(position.z, Is.EqualTo(-2 * ScreenPlacementMath.OrderDepth).Within(1e-9), "A larger order is nearer, in front.");
            Assert.That(scale.x, Is.EqualTo(0.4f).Within(1e-6));
            Assert.That(scale.y, Is.EqualTo(-0.3f).Within(1e-6), "The layer's y grows downwards.");
        }

        [Test]
        public void TheTexture_ShowsTheCrop_CountedFromTheBottom()
        {
            var crop = new NormalizedRect(0.25, 0.1, 0.5, 0.6);

            Vector4 plain = ScreenPlacementMath.TextureScaleOffset(crop, flipHorizontal: false);
            Assert.That(plain.x, Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(plain.y, Is.EqualTo(0.6f).Within(1e-6));
            Assert.That(plain.z, Is.EqualTo(0.25f).Within(1e-6));
            Assert.That(plain.w, Is.EqualTo(0.3f).Within(1e-6), "1 - bottom (0.7).");

            Vector4 flipped = ScreenPlacementMath.TextureScaleOffset(crop, flipHorizontal: true);
            Assert.That(flipped.x, Is.EqualTo(-0.5f).Within(1e-6));
            Assert.That(flipped.z, Is.EqualTo(0.75f).Within(1e-6), "Read from the right edge.");
        }

        [Test]
        public void IsUsable_RejectsEmptyAndOutsideCrops()
        {
            Assert.That(ScreenPlacementMath.IsUsable(FrameLayout.Create(new NormalizedRect(0, 0, 1, 1))), Is.True);
            Assert.That(ScreenPlacementMath.IsUsable(FrameLayout.Create(new NormalizedRect(-0.5, 0, 2, 1))), Is.True, "A placement may go off screen.");
            Assert.That(ScreenPlacementMath.IsUsable(FrameLayout.Create(new NormalizedRect(0, 0, 0, 1))), Is.False);
            Assert.That(ScreenPlacementMath.IsUsable(FrameLayout.Create(new NormalizedRect(double.NaN, 0, 1, 1))), Is.False);
            Assert.That(ScreenPlacementMath.IsUsable(new FrameLayout(NormalizedRect.Full, new NormalizedRect(0.5, 0, 0.6, 1))), Is.False);
        }
    }

    public sealed class StageSettingsTests
    {
        [Test]
        public void ALayout_SurvivesTheFile()
        {
            var layout = new FrameLayout(new NormalizedRect(0.1, 0.2, 0.3, 0.4), new NormalizedRect(0, 0.5, 1, 0.5), flipHorizontal: true, locked: true, allowOutside: true, order: 3);

            Assert.That(ScreenSettings.From(layout).ToLayout(), Is.EqualTo(layout));
        }

        [Test]
        public void ABrokenEntry_FallsBackToTheDefault()
        {
            var settings = new StageSettings();
            settings.Screens["game"] = new ScreenSettings { Placement = new[] { 0.0, 0.0 }, Crop = new[] { 0.0, 0.0, 1.0, 1.0 } };
            settings.Screens["subscreen"] = ScreenSettings.From(FrameLayout.Create(new NormalizedRect(0.5, 0.5, 0.2, 0.2)));

            Assert.That(StageService.LayoutFor(settings, "game"), Is.EqualTo(StageDefaults.For("game")));
            Assert.That(StageService.LayoutFor(settings, "subscreen").Placement, Is.EqualTo(new NormalizedRect(0.5, 0.5, 0.2, 0.2)));
            Assert.That(StageService.LayoutFor(null, "subscreen"), Is.EqualTo(StageDefaults.For("subscreen")));
        }

        [Test]
        public void TheLight_IsTheDefaultUntilChanged_AndASavedValueIsKeptInRange()
        {
            Assert.That(StageService.IntensityFor(new StageSettings()), Is.EqualTo(StageLightIntensity.Default));
            Assert.That(StageService.IntensityFor(null), Is.EqualTo(StageLightIntensity.Default));
            Assert.That(StageService.IntensityFor(new StageSettings { LightIntensity = 0.6f }), Is.EqualTo(0.6f));
            Assert.That(StageService.IntensityFor(new StageSettings { LightIntensity = 9f }), Is.EqualTo(StageLightIntensity.Max), "A hand-edited value is read as the nearest end.");
            Assert.That(StageService.IntensityFor(new StageSettings { LightIntensity = 0f }), Is.EqualTo(StageLightIntensity.Min), "Never dark.");
            Assert.That(StageService.IntensityFor(new StageSettings { LightIntensity = float.NaN }), Is.EqualTo(StageLightIntensity.Default));
            Assert.That(new StageSettings { LightIntensity = 0.6f }.Clone().LightIntensity, Is.EqualTo(0.6f));
        }
    }

    public sealed class StageServiceTests
    {
        private string _directory;
        private GameObject _root;
        private FakeSource _game;
        private FakeSource _sub;
        private ManualClock _clock;
        private FrameLoop _loop;
        private StageService _stage;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _root = new GameObject("TestLayer");
            _game = new FakeSource("game");
            _sub = new FakeSource("subscreen");
            _clock = new ManualClock();
            _loop = new FrameLoop();
            _stage = Create();
        }

        [TearDown]
        public void TearDown()
        {
            _stage.Dispose();
            _game.Dispose();
            _sub.Dispose();
            UnityEngine.Object.DestroyImmediate(_root);
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Test]
        public void TheDefaultSpawnPoint_IsTheOrigin_FacingTheCamera_AndALightIsMadeAndRemoved()
        {
            Assert.That(_stage.SpawnPoints.Default.position, Is.EqualTo(Vector3.zero));
            Assert.That(_stage.SpawnPoints.Default.forward.z, Is.LessThan(-0.99f), "The Main Camera is on -Z.");
            Light light = _stage.DefaultLight;
            Assert.That(light, Is.Not.Null);
            Assert.That(light.type, Is.EqualTo(LightType.Directional));
            Assert.That(light.transform.forward.z, Is.GreaterThan(0.5f), "Lights the avatar's front.");

            _stage.Dispose();

            Assert.That(light == null, Is.True);
        }

        [Test]
        public void ChangingTheLight_AppliesAtOnce_AndIsSaved_AfterTheDelay()
        {
            var seen = new List<float>();
            using IDisposable subscription = _stage.Intensity.Subscribe(seen.Add);
            Assert.That(_stage.DefaultLight.intensity, Is.EqualTo(StageLightIntensity.Default));

            _stage.SetIntensity(0.6f);
            Assert.That(_stage.DefaultLight.intensity, Is.EqualTo(0.6f));
            Assert.That(_stage.Intensity.Value, Is.EqualTo(0.6f));
            _stage.SetIntensity(5f);
            Assert.That(_stage.DefaultLight.intensity, Is.EqualTo(StageLightIntensity.Max), "Kept within the range.");
            Assert.That(seen, Is.EqualTo(new[] { 0.6f, StageLightIntensity.Max }));

            string file = Path.Combine(_directory, StageSettings.FileName);
            _loop.Tick(null);
            Assert.That(File.Exists(file), Is.False, "Not while a slider may still be moving.");
            _clock.Advance(0.5);
            _loop.Tick(null);
            _stage.ShutdownAsync(CancellationToken.None).Wait();

            _stage.Dispose();
            _stage = Create();
            Assert.That(_stage.Intensity.Value, Is.EqualTo(StageLightIntensity.Max));
            Assert.That(_stage.DefaultLight.intensity, Is.EqualTo(StageLightIntensity.Max), "The new light starts as saved.");
        }

        [Test]
        public void EachCaptureSource_GetsAScreen_WithTheDefaultPlacement_AndATranslatedName()
        {
            Assert.That(_stage.Screens.Count, Is.EqualTo(2));
            Assert.That(_stage.Screens[0].Id, Is.EqualTo("game"));
            Assert.That(_stage.Screens[0].NameKey, Is.EqualTo("stage.screen.game"));
            Assert.That(_stage.Screens[1].Layout.Value, Is.EqualTo(StageDefaults.For("subscreen")));
            Assert.That(_root.transform.childCount, Is.EqualTo(2));
        }

        [Test]
        public void AScreen_ShowsItsVideo_OnlyWhileThereIsOne()
        {
            var screen = (CaptureScreen)_stage.Screens[0];
            Assert.That(screen.Visible.Value, Is.False);
            Assert.That(screen.QuadShown, Is.False);

            _game.SetVideo(true);
            Assert.That(screen.Visible.Value, Is.True);
            Assert.That(screen.QuadShown, Is.True);
            Assert.That(screen.Video, Is.SameAs(_game.Texture.Value));

            _game.SetVideo(false);
            Assert.That(screen.QuadShown, Is.False);
        }

        [Test]
        public void APreview_MovesTheQuad_ButIsNotConfirmed()
        {
            var screen = (CaptureScreen)_stage.Screens[1];
            FrameLayout before = screen.Layout.Value;

            screen.PreviewLayout(FrameLayout.Create(new NormalizedRect(0, 0, 0.5, 0.5)).WithOrder(1));

            Assert.That(screen.Quad.localPosition.x, Is.EqualTo(0.25f).Within(1e-6));
            Assert.That(screen.Layout.Value, Is.EqualTo(before));
        }

        [Test]
        public void AnUnusablePlacement_IsRejected_AndTheQuadGoesBack()
        {
            var screen = (CaptureScreen)_stage.Screens[1];
            FrameLayout before = screen.Layout.Value;

            Assert.Throws<UserFacingException>(() => screen.CommitLayoutAsync(FrameLayout.Create(new NormalizedRect(0, 0, 0, 0)), CancellationToken.None));

            Assert.That(screen.Layout.Value, Is.EqualTo(before));
            Assert.That(screen.Quad.localPosition.x, Is.EqualTo((float)(before.Placement.X + (before.Placement.Width / 2))).Within(1e-6));
        }

        [Test]
        public void AConfirmedPlacement_IsSaved_AfterTheDelay_AndReadBackNextTime()
        {
            var layout = FrameLayout.Create(new NormalizedRect(0.2, 0.2, 0.4, 0.4)).WithOrder(5);
            _stage.Screens[0].CommitLayoutAsync(layout, CancellationToken.None).Wait();
            string file = Path.Combine(_directory, StageSettings.FileName);

            _loop.Tick(null);
            Assert.That(File.Exists(file), Is.False, "Not before the delay.");

            _clock.Advance(0.5);
            _loop.Tick(null);
            _stage.ShutdownAsync(CancellationToken.None).Wait();
            Assert.That(File.Exists(file), Is.True);

            _stage.Dispose();
            _stage = Create();
            Assert.That(_stage.Screens[0].Layout.Value, Is.EqualTo(layout));
        }

        [Test]
        public void PlacementsOfUnknownScreens_AreKept()
        {
            string file = Path.Combine(_directory, StageSettings.FileName);
            Directory.CreateDirectory(_directory);
            File.WriteAllText(file, "{\"schemaVersion\": 1, \"screens\": {\"camera\": {\"placement\": [0, 0, 0.2, 0.2], \"crop\": [0, 0, 1, 1], \"order\": 7}}}");
            _stage.Dispose();
            _stage = Create();

            _stage.Screens[0].CommitLayoutAsync(StageDefaults.For("game").WithOrder(0), CancellationToken.None).Wait();
            _stage.ShutdownAsync(CancellationToken.None).Wait();

            Assert.That(File.ReadAllText(file), Does.Contain("\"camera\""));
        }

        private StageService Create()
        {
            var stage = new StageService(new FakeSources(_game, _sub), new FakeOutput(_root.transform), _loop, StageService.CreateStore(_directory, new UtcSystemClock()), _clock, null);
            stage.InitializeAsync(CancellationToken.None).Wait();
            return stage;
        }
    }

    internal sealed class FakeSource : IVideoCaptureSource, IDisposable
    {
        private readonly ObservableValue<Texture> _texture = new ObservableValue<Texture>(null);
        private readonly ObservableValue<CaptureSourceInfo> _info = new ObservableValue<CaptureSourceInfo>(default);
        private Texture2D _video;

        public FakeSource(string id)
        {
            Id = id;
        }

        public string Id { get; }

        public CaptureSourceKind Kind => CaptureSourceKind.GameCapture;

        public IReadOnlyObservable<CaptureState> State { get; } = new ObservableValue<CaptureState>(CaptureState.Stopped);

        public IReadOnlyObservable<CaptureProblem> Problem { get; } = new ObservableValue<CaptureProblem>(CaptureProblem.None);

        public IReadOnlyObservable<CaptureSourceInfo> Info => _info;

        public IReadOnlyObservable<Texture> Texture => _texture;

        public long FrameCount => 0;

        public long LastFrameTimestamp => 0;

        public IReadOnlyObservable<CaptureFlip> Flip { get; } = new ObservableValue<CaptureFlip>(CaptureFlip.None);

        public void SetFlip(CaptureFlip flip)
        {
        }

        public void Start()
        {
        }

        public void Stop()
        {
        }

        /// <summary>A solid video of <paramref name="color"/>, as a source that is capturing gives.</summary>
        public void SetVideo(bool on, Color? color = null)
        {
            if (on && _video == null)
            {
                _video = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var pixels = new Color32[16];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = color ?? Color.green;
                }

                _video.SetPixels32(pixels);
                _video.Apply();
                _info.Value = new CaptureSourceInfo(4, 4, 60, "fake");
            }

            _texture.Value = on ? _video : null;
        }

        public void Dispose()
        {
            if (_video != null)
            {
                UnityEngine.Object.DestroyImmediate(_video);
            }
        }
    }

    internal sealed class FakeSources : ICaptureSources
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

    internal sealed class FakeOutput : IStreamOutput, IStreamScreenLayer
    {
        public FakeOutput(Transform root)
        {
            Root = root;
        }

        public IReadOnlyObservable<Texture> Video { get; } = new ObservableValue<Texture>(null);

        public VideoSize Size => new VideoSize(1920, 1080);

        public IStreamScreenLayer Screens => this;

        public Transform Root { get; }

        public int Layer => 0;

        public int AvatarLayer => 0;
    }

    internal sealed class ManualClock : IMonotonicClock
    {
        private long _now = 1_000_000;

        public long Frequency => 1_000_000;

        public long GetTimestamp()
        {
            return _now;
        }

        public TimeSpan GetElapsed(long startTimestamp, long endTimestamp)
        {
            return TimeSpan.FromTicks((endTimestamp - startTimestamp) * 10);
        }

        public void Advance(double seconds)
        {
            _now += (long)(seconds * Frequency);
        }
    }
}
