using System;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Core.Geometry;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Frames;
using VirtualVessel.Video;

namespace VirtualVessel.Stage.Tests
{
    /// <summary>How the avatar frame shoots the avatar (Stage detailed design 7.2).</summary>
    public sealed class AvatarFramingMathTests
    {
        private const double StreamAspect = 16.0 / 9.0;
        private static readonly Pose s_spawn = StageDefaults.AvatarSpawn;

        // Where a world point lands on the stream through the framing: x from the left, y from the top, 0 to 1.
        private static Vector2 OnStream(AvatarFraming framing, Vector3 world)
        {
            Vector3 local = Quaternion.Inverse(framing.Rotation) * (world - framing.Position);
            float x = local.x / local.z;
            float y = local.y / local.z;
            return new Vector2((x - framing.Left) / (framing.Right - framing.Left), (framing.Top - y) / (framing.Top - framing.Bottom));
        }

        // A point of the frame's whole square, (0, 0) at its top left, for an avatar of the height at the default spawn,
        // seen from the camera in front: its x runs to the camera's right.
        private static Vector3 SquarePoint(float height, double u, double v)
        {
            float side = AvatarFramingMath.SquarePerHeight * height;
            Vector3 facing = s_spawn.rotation * Vector3.forward;
            Vector3 right = Quaternion.LookRotation(-facing, Vector3.up) * Vector3.right;
            return s_spawn.position + right * (float)((u - 0.5) * side) + Vector3.up * (float)((1 - v) * side);
        }

        private static FrameLayout Layout(NormalizedRect placement, NormalizedRect crop)
        {
            return new FrameLayout(placement, crop);
        }

        [TestCase(1.6f)]
        [TestCase(1.2f)]
        [TestCase(2.0f)]
        public void ThePartShown_FillsTheFrame_AtAnyHeight(float height)
        {
            // The upper body on the right of the stream, its waist on the bottom edge: seated.
            var crop = new NormalizedRect(0.2, 0.0, 0.6, 0.6);
            double width = 0.6 * (crop.Width / crop.Height) / StreamAspect;
            var placement = new NormalizedRect(0.95 - width, 0.4, width, 0.6);

            AvatarFraming framing = AvatarFramingMath.Framing(s_spawn, height, Layout(placement, crop), StreamAspect).Value;

            Vector2 topLeft = OnStream(framing, SquarePoint(height, crop.Left, crop.Top));
            Vector2 bottomRight = OnStream(framing, SquarePoint(height, crop.Right, crop.Bottom));
            Assert.That(topLeft.x, Is.EqualTo(placement.Left).Within(1e-4));
            Assert.That(topLeft.y, Is.EqualTo(placement.Top).Within(1e-4));
            Assert.That(bottomRight.x, Is.EqualTo(placement.Right).Within(1e-4));
            Assert.That(bottomRight.y, Is.EqualTo(placement.Bottom).Within(1e-4));
        }

        [Test]
        public void TheCamera_StandsInFrontOfTheAvatar_LevelWithThePartShown_FacingIt()
        {
            var crop = new NormalizedRect(0.25, 0.0, 0.5, 0.5);
            AvatarFraming framing = AvatarFramingMath.Framing(s_spawn, 1.6f, Layout(new NormalizedRect(0.6, 0.2, 0.3, 0.6), crop), StreamAspect).Value;

            float side = AvatarFramingMath.SquarePerHeight * 1.6f;
            Assert.That(framing.Position.x, Is.EqualTo(0f).Within(1e-5), "Straight in front, wherever the frame is.");
            Assert.That(framing.Position.y, Is.EqualTo(side * 0.75f).Within(1e-4), "Level with the middle of the part shown.");
            Assert.That(framing.Position.z, Is.EqualTo(-AvatarFramingMath.Distance).Within(1e-5), "The avatar faces -Z, toward the camera.");
            Assert.That(Vector3.Angle(framing.Rotation * Vector3.forward, Vector3.forward), Is.LessThan(0.01f));
        }

        [Test]
        public void TheDefault_IsTheUpperBody_InTheMiddle_TheStreamsFullHeight()
        {
            FrameLayout layout = AvatarFramingMath.DefaultLayout(StreamAspect);

            Assert.That(layout.Crop, Is.EqualTo(AvatarFramingMath.DefaultCrop));
            Assert.That(layout.Placement.Height, Is.EqualTo(1).Within(1e-9));
            Assert.That(layout.Placement.Width, Is.EqualTo(0.5625).Within(1e-9), "The crop is square: 9/16 of a 16:9 stream's width.");
            Assert.That(layout.Placement.CenterX, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(layout.Order, Is.EqualTo(AvatarFrame.FrontOrder));
            Assert.That(ScreenPlacementMath.IsUsable(layout), Is.True);
        }

        [Test]
        public void NoFraming_ForValuesThatCannotMakeOne()
        {
            FrameLayout layout = AvatarFramingMath.DefaultLayout(StreamAspect);

            Assert.That(AvatarFramingMath.Framing(s_spawn, 0f, layout, StreamAspect), Is.Null);
            Assert.That(AvatarFramingMath.Framing(s_spawn, float.NaN, layout, StreamAspect), Is.Null);
            Assert.That(AvatarFramingMath.Framing(s_spawn, 1.6f, layout, 0), Is.Null);
            Assert.That(AvatarFramingMath.Framing(s_spawn, 1.6f, layout.WithCrop(new NormalizedRect(0, 0, 1, 0)), StreamAspect), Is.Null);
        }
    }

    public sealed class AvatarFrameTests
    {
        private FakeAvatarCamera _camera;
        private ObservableValue<float?> _height;
        private int _committed;

        [SetUp]
        public void SetUp()
        {
            _camera = new FakeAvatarCamera();
            _height = new ObservableValue<float?>(null);
            _committed = 0;
        }

        private AvatarFrame Create(FrameLayout? layout = null)
        {
            return new AvatarFrame(_camera, _height, StageDefaults.AvatarSpawn, 16.0 / 9.0, layout ?? AvatarFramingMath.DefaultLayout(16.0 / 9.0), _ => _committed++);
        }

        [Test]
        public void WithoutAnAvatar_TheFrameHides_AndTheCameraFollowsTheMainCamera()
        {
            using (AvatarFrame frame = Create())
            {
                Assert.That(frame.Visible.Value, Is.False);
                Assert.That(_camera.Framing, Is.Null);

                _height.Value = 1.6f;
                Assert.That(frame.Visible.Value, Is.True, "Shown once there is an avatar.");
                Assert.That(_camera.Framing, Is.Not.Null);

                _height.Value = null;
                Assert.That(frame.Visible.Value, Is.False);
                Assert.That(_camera.Framing, Is.Null);
            }
        }

        [Test]
        public void APreview_MovesTheAvatar_AndACommit_KeepsIt_InFront_Unflipped()
        {
            _height.Value = 1.6f;
            using (AvatarFrame frame = Create())
            {
                AvatarFraming before = _camera.Framing.Value;
                FrameLayout moved = frame.Layout.Value.WithPlacement(new NormalizedRect(0.5, 0.3, 0.4, 0.7));

                frame.PreviewLayout(moved);
                Assert.That(_camera.Framing.Value.Left, Is.Not.EqualTo(before.Left), "The stream follows the drag.");
                Assert.That(frame.Layout.Value, Is.Not.EqualTo(moved), "A preview is not confirmed.");
                Assert.That(_committed, Is.EqualTo(0));

                frame.CommitLayoutAsync(moved.WithOrder(3).WithFlipHorizontal(true), CancellationToken.None).Wait();
                Assert.That(frame.Layout.Value.Placement, Is.EqualTo(moved.Placement));
                Assert.That(frame.Layout.Value.Order, Is.EqualTo(AvatarFrame.FrontOrder));
                Assert.That(frame.Layout.Value.FlipHorizontal, Is.False);
                Assert.That(_committed, Is.EqualTo(1));
            }
        }

        [Test]
        public void AnUnusablePlacement_IsRejected()
        {
            _height.Value = 1.6f;
            using (AvatarFrame frame = Create())
            {
                FrameLayout before = frame.Layout.Value;
                var empty = before.WithCrop(new NormalizedRect(0, 0, 0, 0));

                Assert.Throws<UserFacingException>(() => frame.CommitLayoutAsync(empty, CancellationToken.None));
                Assert.That(frame.Layout.Value, Is.EqualTo(before));
            }
        }

        [Test]
        public void ItStaysInFront_CannotFlip_AndHasNoPicture()
        {
            using (AvatarFrame frame = Create())
            {
                IFrameSource source = frame;
                Assert.That(source.StaysInFront, Is.True);
                Assert.That(source.CanFlip, Is.False);
                Assert.That(source.CanStopCapture, Is.False);
                Assert.That(source.Video, Is.Null);
                Assert.That(source.SourceAspect, Is.EqualTo(1.0));
            }
        }

        [Test]
        public void Disposing_GivesTheCameraBack()
        {
            _height.Value = 1.6f;
            AvatarFrame frame = Create();
            Assert.That(_camera.Framing, Is.Not.Null);

            frame.Dispose();
            Assert.That(_camera.Framing, Is.Null);
            _height.Value = 1.7f;
            Assert.That(_camera.Framing, Is.Null, "No longer follows the avatar.");
        }
    }

    public sealed class StageAvatarFrameTests
    {
        private string _directory;
        private GameObject _root;
        private FakeSource _game;
        private ManualClock _clock;
        private FrameLoop _loop;
        private FakeAvatarCamera _camera;
        private ObservableValue<float?> _height;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _root = new GameObject("TestLayer");
            _game = new FakeSource("game");
            _clock = new ManualClock();
            _loop = new FrameLoop();
            _camera = new FakeAvatarCamera();
            _height = new ObservableValue<float?>(1.6f);
        }

        [TearDown]
        public void TearDown()
        {
            _game.Dispose();
            UnityEngine.Object.DestroyImmediate(_root);
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        private StageService Create(bool withAvatar = true)
        {
            var stage = new StageService(new FakeSources(_game), new FakeOutput(_root.transform), _loop, StageService.CreateStore(_directory, new UtcSystemClock()), _clock, null,
                withAvatar ? _camera : null, withAvatar ? _height : null);
            stage.InitializeAsync(CancellationToken.None).Wait();
            return stage;
        }

        [Test]
        public void TheAvatarFrame_ComesAfterTheVideos_WithTheDefaultPlacement()
        {
            StageService stage = Create();
            try
            {
                Assert.That(stage.Screens.Select(s => s.Id), Is.EqualTo(new[] { "game", AvatarFrame.FrameId }));
                Assert.That(stage.AvatarFrameSource, Is.SameAs(stage.Screens[1]));
                Assert.That(stage.AvatarFrameSource.NameKey, Is.EqualTo("stage.avatar.name"));
                Assert.That(stage.AvatarFrameSource.Layout.Value, Is.EqualTo(AvatarFramingMath.DefaultLayout(16.0 / 9.0)));
            }
            finally
            {
                stage.Dispose();
            }

            Assert.That(_camera.Framing, Is.Null, "Stage gives the camera back when it stops.");
        }

        [Test]
        public void WithoutTheAvatarCamera_ThereIsNoFrame()
        {
            StageService stage = Create(withAvatar: false);
            try
            {
                Assert.That(stage.Screens.Select(s => s.Id), Is.EqualTo(new[] { "game" }));
                Assert.That(stage.AvatarFrameSource, Is.Null);
            }
            finally
            {
                stage.Dispose();
            }
        }

        [Test]
        public void TheAvatarsPlacement_IsSaved_AndReadBackNextTime()
        {
            StageService stage = Create();
            FrameLayout moved = stage.AvatarFrameSource.Layout.Value.WithPlacement(new NormalizedRect(0.6, 0.3, 0.39, 0.7));
            stage.AvatarFrameSource.CommitLayoutAsync(moved, CancellationToken.None).Wait();
            _clock.Advance(0.5);
            _loop.Tick(null);
            stage.ShutdownAsync(CancellationToken.None).Wait();
            stage.Dispose();

            Assert.That(File.ReadAllText(Path.Combine(_directory, StageSettings.FileName)), Does.Contain("\"avatar\""));
            stage = Create();
            try
            {
                Assert.That(stage.AvatarFrameSource.Layout.Value, Is.EqualTo(moved));
            }
            finally
            {
                stage.Dispose();
            }
        }
    }

    internal sealed class FakeAvatarCamera : IAvatarCamera
    {
        public AvatarFraming? Framing { get; private set; }

        public void SetFraming(AvatarFraming? framing)
        {
            Framing = framing;
        }
    }
}
