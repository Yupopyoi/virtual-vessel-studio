using System;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Core.Time;
using VirtualVessel.UI.Lighting;
using VirtualVessel.UI.StageEditing;

namespace VirtualVessel.Stage.Tests
{
    public sealed class StageViewMathTests
    {
        private const float Aspect = 16f / 9f;

        [Test]
        public void TheFirstView_LooksAtTheFace_FromTheFrontLeftAndAbove()
        {
            StageViewPose pose = StageViewMath.Default;
            (Vector2 uv, bool inFront) = StageViewMath.ToView(pose, StageLightPlacement.Target, Aspect);
            Assert.That(inFront, Is.True);
            Assert.That(Vector2.Distance(uv, new Vector2(0.5f, 0.5f)), Is.LessThan(1e-4f), "The pivot is in the middle.");
            Vector3 eye = StageViewMath.Position(pose);
            Assert.That(eye.x, Is.LessThan(0f), "From the left.");
            Assert.That(eye.z, Is.LessThan(0f), "On the stream camera's side.");
            Assert.That(eye.y, Is.GreaterThan(StageLightPlacement.Target.y), "From above.");
        }

        [Test]
        public void Orbiting_WrapsAround_AndStopsAboveAndBelow()
        {
            StageViewPose pose = new StageViewPose(Vector3.zero, 170f, 20f, 4f);
            Assert.That(StageViewMath.Orbit(pose, new Vector2(20f, 0f)).Yaw, Is.EqualTo(-170f).Within(1e-4f));
            Assert.That(StageViewMath.Orbit(pose, new Vector2(0f, 200f)).Pitch, Is.EqualTo(StageViewMath.MaxPitch));
            Assert.That(StageViewMath.Orbit(pose, new Vector2(0f, -200f)).Pitch, Is.EqualTo(StageViewMath.MinPitch));
            Assert.That(StageViewMath.Zoom(pose, 1f).Distance, Is.EqualTo(3.6f).Within(1e-4f), "10% closer a step.");
            Assert.That(StageViewMath.Zoom(pose, 100f).Distance, Is.EqualTo(StageViewMath.MinDistance));
            Assert.That(StageViewMath.Zoom(pose, -100f).Distance, Is.EqualTo(StageViewMath.MaxDistance));
        }

        [Test]
        public void TheStage_TurnsWithThePointer()
        {
            // Left and right as in Unity's Scene View; up and down the other way, the user's choice (2026-11-22).
            Assert.That(StageViewPanel.OrbitFor(new Vector2(10f, 0f)).x, Is.LessThan(0f), "Dragging right moves the viewpoint left.");
            Assert.That(StageViewPanel.OrbitFor(new Vector2(0f, 10f)).y, Is.GreaterThan(0f), "Dragging down raises the viewpoint.");
            Assert.That(StageViewPanel.OrbitFor(new Vector2(10f, 10f), invertX: true, invertY: false), Is.EqualTo(new Vector2(4f, 3f)), "Each direction can be reversed.");
            Assert.That(StageViewPanel.OrbitFor(new Vector2(10f, 10f), invertX: false, invertY: true), Is.EqualTo(new Vector2(-4f, -3f)));
        }

        [Test]
        public void ARay_ThroughAPointOnTheView_PassesThroughThatPoint()
        {
            StageViewPose pose = StageViewMath.Default;
            var point = new Vector3(0.7f, 1.6f, 0.4f);
            (Vector2 uv, _) = StageViewMath.ToView(pose, point, Aspect);
            (Vector3 origin, Vector3 direction) = StageViewMath.Ray(pose, uv, Aspect);
            Vector3 toPoint = (point - origin).normalized;
            Assert.That(Vector3.Angle(direction, toPoint), Is.LessThan(0.01f));
        }

        [Test]
        public void Panning_KeepsAPointUnderThePointer()
        {
            StageViewPose pose = StageViewMath.Default;
            Vector3 point = StageLightPlacement.Target;
            (Vector2 before, _) = StageViewMath.ToView(pose, point, Aspect);
            var move = new Vector2(0.1f, 0.05f);
            (Vector2 after, _) = StageViewMath.ToView(StageViewMath.Pan(pose, move, Aspect), point, Aspect);
            Assert.That(Vector2.Distance(after, before + move), Is.LessThan(1e-3f), "The pivot follows the pointer.");
        }

        [Test]
        public void TheHandleOnASphere_GivesTheAnglesOfThePointUnderThePointer()
        {
            Vector3 center = StageLightPlacement.Target;
            Vector3 onSphere = center + StageLightPlacement.Offset(60f, 20f) * 2f;
            Vector3 origin = center + new Vector3(-1f, 1f, -6f);
            Vector2 angles = StageViewMath.OnSphere(center, 2f, origin, (onSphere - origin).normalized);
            Assert.That(angles.x, Is.EqualTo(60f).Within(0.01f));
            Assert.That(angles.y, Is.EqualTo(20f).Within(0.01f));

            // A ray passing beside the sphere still gives the nearest direction.
            Vector2 missed = StageViewMath.OnSphere(center, 0.5f, center + new Vector3(-3f, 0f, -5f), Vector3.forward);
            Assert.That(missed.x, Is.LessThan(0f), "Toward the side the ray passed on.");
        }
    }

    public sealed class StageViewTests
    {
        private string _directory;
        private GameObject _root;
        private FakeSource _game;
        private FakeSource _sub;
        private StageService _stage;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _root = new GameObject("StreamScreens");
            _game = new FakeSource("game");
            _sub = new FakeSource("subscreen");
            _stage = new StageService(new FakeSources(_game, _sub), new FakeOutput(_root.transform, layer: 5), new Core.Lifecycle.FrameLoop(), StageService.CreateStore(_directory, new UtcSystemClock()), new ManualClock(), null);
            _stage.InitializeAsync(CancellationToken.None).Wait();
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
        public void Opening_MakesACameraOfItsOwn_ApartFromTheStream_AndClosingRemovesIt()
        {
            StageView view = _stage.View;
            Assert.That(view.Image.Value, Is.Null, "Nothing is drawn until opened.");

            IDisposable first = view.Open(640, 360);
            IDisposable second = view.Open(640, 360);
            Camera camera = view.Camera;
            Assert.That(camera, Is.Not.Null);
            Assert.That(view.Image.Value, Is.SameAs(camera.targetTexture), "Its own render target.");
            Assert.That(camera.targetTexture.width, Is.EqualTo(640));
            Assert.That(camera.CompareTag("MainCamera"), Is.False);
            Assert.That(camera.cullingMask & (1 << 5), Is.Zero, "Not the flat videos' layer.");
            Assert.That(view.Markers.Value.Select(m => m.ItemId), Is.EqualTo(new[] { StageViewItems.Light(0) }));

            first.Dispose();
            first.Dispose();
            Assert.That(view.Camera, Is.Not.Null, "Still open for the other.");
            second.Dispose();
            Assert.That(camera == null, Is.True, "Destroyed with the last close.");
            Assert.That(view.Image.Value, Is.Null);
            Assert.That(view.Markers.Value, Is.Empty);
        }

        [Test]
        public void TheRenderTarget_KeepsTheViewsShape_WithinTheLimit()
        {
            Assert.That(StageView.TargetSize(1920, 1080), Is.EqualTo(new Vector2Int(StageView.MaxWidth, StageView.MaxHeight)));
            Assert.That(StageView.TargetSize(448, 252), Is.EqualTo(new Vector2Int(448, 252)));
        }

        [Test]
        public void DraggingALight_MovesItToThePointOnItsSphere_AndSavesItThroughTheLighting()
        {
            _stage.SetLighting(_stage.Lighting.Value.WithLight(0, StageLight.Default.WithKind(StageLightKind.Spot).WithDistance(2f)));
            StageView view = _stage.View;
            using IDisposable open = view.Open(1600, 900);
            // On the sphere's near side: the view is from the front left, and a drag takes the nearer of two points.
            Vector3 aim = StageLightPlacement.Target + StageLightPlacement.Offset(-60f, 30f) * 2f;
            (Vector2 uv, _) = StageViewMath.ToView(view.Pose, aim, 16f / 9f);

            Assert.That(view.BeginDrag(StageViewItems.Light(0), uv), Is.True);
            view.EndDrag();
            StageLight light = _stage.Lighting.Value.Lights[0];
            Assert.That(light.Yaw, Is.EqualTo(-60f).Within(1f));
            Assert.That(light.Pitch, Is.EqualTo(30f).Within(1f));
            Assert.That(_stage.Lights[0].transform.position, Is.EqualTo(StageLightPlacement.Position(light)), "The light moved at once.");
            Assert.That(view.BeginDrag("camera:0", uv), Is.False, "Nothing else can be dragged.");
        }

        [Test]
        public void TheChosenLight_IsDrawnOnTop_AndTheOthersFollowDepth()
        {
            var guides = new StageViewGuides();
            try
            {
                StageViewGuides.AddLight(guides, StageLight.Added, chosen: true);
                Assert.That(guides.SegmentCount, Is.EqualTo(1 + 32 + 4), "A spot: the line to the face, the ring, and four lines to it.");
                guides.Clear();
                StageViewGuides.AddLight(guides, StageLight.Default, chosen: false);
                Assert.That(guides.SegmentCount, Is.EqualTo(5 * 3), "A whole-stage light: five arrows with their heads.");
            }
            finally
            {
                guides.Dispose();
            }
        }
    }
}
