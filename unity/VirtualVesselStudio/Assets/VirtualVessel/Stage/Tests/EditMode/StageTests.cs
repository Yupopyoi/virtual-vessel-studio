using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public void TheLighting_IsTheDefaultUntilChanged_AndTheOldBrightnessIsKept()
        {
            Assert.That(StageService.LightingFor(new StageSettings()), Is.EqualTo(StageLighting.Default));
            Assert.That(StageService.LightingFor(null), Is.EqualTo(StageLighting.Default));

            // A file of version 0.3 has only the one light's brightness.
            StageLighting old = StageService.LightingFor(new StageSettings { LightIntensity = 0.6f });
            Assert.That(old.Lights.Count, Is.EqualTo(1));
            Assert.That(old.Lights[0], Is.EqualTo(StageLight.Default.WithIntensity(0.6f)));
            Assert.That(StageService.LightingFor(new StageSettings { LightIntensity = float.NaN }).Lights[0].Intensity, Is.EqualTo(1f));
            Assert.That(new StageSettings { LightIntensity = 0.6f }.Clone().LightIntensity, Is.EqualTo(0.6f));
        }

        [Test]
        public void SavedLights_AreRead_WithinTheLimits_AndAMissingFieldTakesTheDefault()
        {
            var settings = new StageSettings
            {
                LightIntensity = 0.6f,
                AmbientLight = 9f,
                Lights = new List<LightSettings>
                {
                    new LightSettings { Kind = "spot", Yaw = 400f, Pitch = 20f, Distance = 3f, Intensity = 1.2f, Color = "pink", Temperature = 5000f, SpotAngle = 30f },
                    new LightSettings { Kind = "laser", Color = "rainbow" },
                    null,
                    new LightSettings(), new LightSettings(), new LightSettings(),
                },
            };

            StageLighting lighting = StageService.LightingFor(settings);
            Assert.That(lighting.Lights.Count, Is.EqualTo(StageLighting.MaxLights), "At most four.");
            Assert.That(lighting.Lights[0], Is.EqualTo(new StageLight(StageLightKind.Spot, StageLight.MaxYaw, 20f, 3f, 1.2f, StageLightColors.Pink, 5000f, 30f)));
            Assert.That(lighting.Lights[1], Is.EqualTo(StageLight.Default), "An unknown kind and color, and missing fields, are the default's.");
            Assert.That(lighting.Ambient, Is.EqualTo(StageLighting.MaxAmbient));
            Assert.That(StageService.LightingFor(new StageSettings { Lights = new List<LightSettings>() }), Is.EqualTo(StageLighting.Default), "Never without a light.");

            StageSettings copy = settings.Clone();
            copy.Lights[0].Yaw = 0f;
            Assert.That(settings.Lights[0].Yaw, Is.EqualTo(400f), "The copy is deep.");
        }

        [Test]
        public void AColorCode_IsReadInEitherCase_AndKeptInCapitals()
        {
            Assert.That(StageLightColors.TryParse("#ff88cc", out Color color), Is.True);
            Assert.That(StageLightColors.ToCode(color), Is.EqualTo("#FF88CC"));
            Assert.That(StageLightColors.TryParse(" 00ff00 ", out Color green), Is.True, "Without # and with spaces.");
            Assert.That(green, Is.EqualTo(new Color(0f, 1f, 0f)));
            Assert.That(StageLightColors.TryParse("#12345", out _), Is.False);
            Assert.That(StageLightColors.TryParse("#GGGGGG", out _), Is.False);
            Assert.That(StageLightColors.TryParse("#-12345", out _), Is.False);
            Assert.That(StageLightColors.TryParse(null, out _), Is.False);

            Assert.That(StageLightColors.Normalize("#ff88cc"), Is.EqualTo("#FF88CC"));
            Assert.That(StageLightColors.Normalize(StageLightColors.Pink), Is.EqualTo(StageLightColors.Pink));
            Assert.That(StageLightColors.Normalize("rainbow"), Is.Null);
            Assert.That(StageLightColors.ColorOf(StageLight.Default.WithColor("#FF0000")), Is.EqualTo(new Color(1f, 0f, 0f)));

            var settings = new StageSettings { Lights = new List<LightSettings> { new LightSettings { Color = "#ff88cc" }, new LightSettings { Color = "#nope" } } };
            StageLighting lighting = StageService.LightingFor(settings);
            Assert.That(lighting.Lights[0].ColorId, Is.EqualTo("#FF88CC"), "A saved code is kept.");
            Assert.That(lighting.Lights[1].ColorId, Is.EqualTo(StageLightColors.White), "A broken one is white.");
        }

        [Test]
        public void TheDefaultLight_ComesFromWhereItAlwaysDid()
        {
            Quaternion rotation = StageLightPlacement.Rotation(StageLight.Default);
            Assert.That(Quaternion.Angle(rotation, Quaternion.Euler(35f, 15f, 0f)), Is.LessThan(0.01f));
        }

        [Test]
        public void ALight_StandsAroundTheFace_AndAimsAtIt()
        {
            StageLight right = StageLight.Default.WithKind(StageLightKind.Spot).WithYaw(90f).WithPitch(0f).WithDistance(2f);
            Vector3 position = StageLightPlacement.Position(right);
            Assert.That(position.x, Is.EqualTo(2f).Within(1e-4f), "A positive yaw is on the screen's right.");
            Assert.That(position.y, Is.EqualTo(StageLightPlacement.Target.y).Within(1e-4f));
            Vector3 forward = StageLightPlacement.Rotation(right) * Vector3.forward;
            Assert.That(Vector3.Angle(forward, StageLightPlacement.Target - position), Is.LessThan(0.01f));

            StageLight front = right.WithYaw(0f);
            Assert.That(StageLightPlacement.Position(front).z, Is.LessThan(0f), "Yaw 0 is on the camera's side.");

            StageLight above = right.WithPitch(90f);
            Vector3 down = StageLightPlacement.Rotation(above) * Vector3.forward;
            Assert.That(Vector3.Angle(down, Vector3.down), Is.LessThan(0.01f), "Straight above still aims down.");
        }

        [Test]
        public void ASpot_IsRaisedByItsDistanceSquared_SoTheFaceGetsTheSameLight()
        {
            StageLight spot = StageLight.Default.WithKind(StageLightKind.Spot).WithDistance(3f).WithIntensity(0.5f);
            Assert.That(StageLightPlacement.UnityIntensity(spot), Is.EqualTo(4.5f).Within(1e-4f));
            Assert.That(StageLightPlacement.UnityIntensity(spot.WithKind(StageLightKind.Directional)), Is.EqualTo(0.5f));
            Assert.That(StageLightPlacement.Range(spot), Is.GreaterThan(spot.Distance));
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
            GameObject backdrop = _stage.Backdrop;
            Assert.That(backdrop, Is.Not.Null);
            Assert.That(backdrop.transform.position.z, Is.GreaterThan(0f), "Behind the avatar, away from the camera.");
            Material material = backdrop.GetComponent<MeshRenderer>().sharedMaterial;
            Texture texture = material.mainTexture;

            _stage.Dispose();

            Assert.That(light == null, Is.True);
            Assert.That(backdrop == null && material == null && texture == null, Is.True, "The backdrop, its material, and its picture are removed.");
        }

        [Test]
        public void TheBackdropPicture_IsDark_Opaque_AndTheSameEveryTime()
        {
            Color32[] picture = DefaultBackdrop.Render(160, 90);
            Assert.That(picture, Has.Length.EqualTo(160 * 90));
            Assert.That(picture.All(p => p.a == 255), Is.True, "Opaque.");
            Assert.That(picture.Average(p => (p.r + p.g + p.b) / 3.0), Is.LessThan(80), "Dark on the whole.");
            Assert.That(picture.Select(p => (p.r, p.g, p.b)).Distinct().Count(), Is.GreaterThan(50), "Not a flat color.");
            Assert.That(DefaultBackdrop.Render(160, 90), Is.EqualTo(picture), "The same picture every time.");

            Color top = DefaultBackdrop.Gradient(1f);
            Color bottom = DefaultBackdrop.Gradient(0f);
            Assert.That(top.r, Is.GreaterThan(bottom.r), "Plum at the top.");
            Assert.That(bottom.b, Is.GreaterThan(bottom.r), "Navy at the bottom.");

            // Drawn once at start, on the main thread.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            DefaultBackdrop.Render(DefaultBackdrop.Width, DefaultBackdrop.Height);
            Debug.Log($"[Stage/Backdrop] {DefaultBackdrop.Width}x{DefaultBackdrop.Height} drawn in {watch.Elapsed.TotalMilliseconds:F0} ms");
        }

        [Test]
        public void ChangingTheLights_AppliesAtOnce_AndIsSaved_AfterTheDelay()
        {
            var seen = new List<StageLighting>();
            using IDisposable subscription = _stage.Lighting.Subscribe(seen.Add);
            Assert.That(_stage.Lights.Count, Is.EqualTo(1));
            Assert.That(_stage.DefaultLight.type, Is.EqualTo(LightType.Directional));
            Assert.That(_stage.DefaultLight.intensity, Is.EqualTo(1f));

            StageLighting dimmer = _stage.Lighting.Value.WithLight(0, StageLight.Default.WithIntensity(0.6f));
            _stage.SetLighting(dimmer);
            Assert.That(_stage.DefaultLight.intensity, Is.EqualTo(0.6f));
            StageLighting withSpot = dimmer.WithAdded(StageLight.Added.WithColor(StageLightColors.Sky).WithIntensity(9f));
            _stage.SetLighting(withSpot);
            Assert.That(_stage.Lights.Count, Is.EqualTo(2));
            Assert.That(_stage.Lights[1].type, Is.EqualTo(LightType.Spot));
            Assert.That(_stage.Lights[1].color, Is.EqualTo(StageLightColors.ColorOf(StageLight.Added.WithColor(StageLightColors.Sky))));
            Assert.That(_stage.Lighting.Value.Lights[1].Intensity, Is.EqualTo(StageLight.MaxIntensity), "Kept within the range.");
            Assert.That(seen.Count, Is.EqualTo(2));

            // Removing a light takes its object away.
            Light spot = _stage.Lights[1];
            _stage.SetLighting(_stage.Lighting.Value.WithRemoved(1));
            Assert.That(_stage.Lights.Count, Is.EqualTo(1));
            Assert.That(spot == null, Is.True, "Destroyed.");
            _stage.SetLighting(withSpot);

            string file = Path.Combine(_directory, StageSettings.FileName);
            _loop.Tick(null);
            Assert.That(File.Exists(file), Is.False, "Not while a slider may still be moving.");
            _clock.Advance(0.5);
            _loop.Tick(null);
            _stage.ShutdownAsync(CancellationToken.None).Wait();

            _stage.Dispose();
            _stage = Create();
            Assert.That(_stage.Lighting.Value, Is.EqualTo(withSpot.Normalized()));
            Assert.That(_stage.Lights.Count, Is.EqualTo(2), "The new lights start as saved.");
            Assert.That(_stage.DefaultLight.intensity, Is.EqualTo(0.6f));
        }

        [Test]
        public void TheAmbientLight_ScalesTheScenes_AndIsPutBackOnDispose()
        {
            UnityEngine.Rendering.SphericalHarmonicsL2 scene = RenderSettings.ambientProbe;
            _stage.SetLighting(_stage.Lighting.Value.WithAmbient(0.5f));
            Assert.That(RenderSettings.ambientProbe, Is.EqualTo(scene * 0.5f));

            _stage.Dispose();
            Assert.That(RenderSettings.ambientProbe, Is.EqualTo(scene));
            _stage = Create();
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
