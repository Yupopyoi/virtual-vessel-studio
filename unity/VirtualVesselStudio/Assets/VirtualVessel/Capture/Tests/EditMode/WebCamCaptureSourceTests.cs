using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Capture.Game;

namespace VirtualVessel.Capture.Tests
{
    public sealed class GameCaptureSourceTests
    {
        private const double FrameTime = 1.0 / 60;

        private ManualClock _clock;
        private FakeCameraDevices _devices;
        private CaptureSettings _settings;
        private GameCaptureSource _source;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _devices = new FakeCameraDevices("Board A", "Board B");
            _settings = new CaptureSettings { GameDeviceId = "Board A" };
            _source = null;
        }

        [TearDown]
        public void TearDown()
        {
            _source?.Dispose();
            _devices.Dispose();
        }

        [Test]
        public void Start_OpensTheChosenBoardByName_WithTheRequestedFormat()
        {
            _settings.GameDeviceId = "Board B";
            _settings.GameWidth = 1280;
            _settings.GameHeight = 720;
            _settings.GameFrameRate = 30;
            Create();

            _source.Start();

            Assert.That(_devices.Opened.Count, Is.EqualTo(1));
            Assert.That(_devices.Last.Name, Is.EqualTo("Board B"));
            Assert.That(_devices.Last.Requested, Is.EqualTo((1280, 720, 30)));
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Ready));
            Assert.That(_source.Info.Value.DeviceName, Is.EqualTo("Board B"));
            Assert.That(_source.Texture.Value, Is.Null, "No texture before the first image.");
        }

        [Test]
        public void WithoutAChosenBoard_NothingOpens_UntilOneIsChosen()
        {
            _settings.GameDeviceId = null;
            Create();

            _source.Start();

            Assert.That(_devices.Opened, Is.Empty, "The first camera may be a webcam.");
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Stopped));
            Assert.That(_source.Problem.Value, Is.EqualTo(CaptureProblem.DeviceNotChosen));
            Assert.That(_source.Devices.Count, Is.EqualTo(2), "The list is ready to choose from.");

            _source.SelectDevice("Board B");

            Assert.That(_devices.Last.Name, Is.EqualTo("Board B"), "The start that waited goes on.");
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Ready));
        }

        [Test]
        public void AFlip_ShowsAMirroredCopy_AndNoFlipTheCameraItself()
        {
            _settings.GameFlip = CaptureFlip.Horizontal;
            Create();
            _source.Start();
            SendFrames(1);

            Assert.That(_source.Flip.Value, Is.EqualTo(CaptureFlip.Horizontal));
            Assert.That(_source.Texture.Value, Is.InstanceOf<RenderTexture>());

            _source.SetFlip(CaptureFlip.None);
            Assert.That(_source.Texture.Value, Is.SameAs(_devices.Last.Texture));
        }

        [Test]
        public void TheFirstRealImage_MakesItCapturing_AndPublishesTheTexture()
        {
            Create();
            _source.Start();

            // Until its first image a camera reports a 16-pixel placeholder.
            _devices.Last.SendFrame(16, 16);
            Tick();
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Ready));

            _devices.Last.SendFrame(1920, 1080);
            Tick();

            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Capturing));
            Assert.That(_source.Texture.Value, Is.SameAs(_devices.Last.Texture));
            Assert.That(_source.Info.Value.Width, Is.EqualTo(1920));
            Assert.That(_source.Info.Value.Height, Is.EqualTo(1080));
            Assert.That(_source.Info.Value.DeviceName, Is.EqualTo("Board A"));
        }

        [Test]
        public void TheFrameRate_IsWhatActuallyArrives()
        {
            Create();
            _source.Start();

            // 30 images a second while Unity updates 60 times a second.
            for (int i = 0; i < 70; i++)
            {
                if (i % 2 == 0)
                {
                    _devices.Last.SendFrame(1920, 1080);
                }

                Tick();
            }

            Assert.That(_source.Info.Value.FrameRate, Is.EqualTo(30.0).Within(0.1));
        }

        [Test]
        public void NothingForFiveSeconds_AfterOpening_IsNoSignal()
        {
            Create();
            _source.Start();

            _clock.Advance(4.9);
            Tick();
            Assert.That(_source.Problem.Value, Is.EqualTo(CaptureProblem.None));

            _clock.Advance(0.1);
            Tick();
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Ready));
            Assert.That(_source.Problem.Value, Is.EqualTo(CaptureProblem.NoSignal));
        }

        [Test]
        public void ImagesThatStop_AreNoSignal_UntilTheyComeBack()
        {
            Create();
            _source.Start();
            SendFrames(90);
            Assert.That(_source.Info.Value.FrameRate, Is.GreaterThan(0));

            _clock.Advance(5.0);
            Tick();

            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Ready));
            Assert.That(_source.Problem.Value, Is.EqualTo(CaptureProblem.NoSignal));
            Assert.That(_source.Info.Value.FrameRate, Is.EqualTo(0), "Nothing arrives now.");
            Assert.That(_source.Texture.Value, Is.SameAs(_devices.Last.Texture), "The last image is kept.");

            SendFrames(1);
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Capturing));
            Assert.That(_source.Problem.Value, Is.EqualTo(CaptureProblem.None));
        }

        [Test]
        public void TheDeviceList_IsNotQueried_WhileImagesArrive()
        {
            Create();
            _source.Start();
            int listedOnStart = _devices.ListCalls;

            SendFrames(600);

            Assert.That(_devices.ListCalls, Is.EqualTo(listedOnStart));
        }

        [Test]
        public void ABoardThatIsUnplugged_IsUnavailable_AndReopensWhenPluggedBackIn()
        {
            Create();
            _source.Start();
            SendFrames(180);
            FakeCameraStream first = _devices.Last;

            _devices.Unplug("Board A");
            _clock.Advance(0.9);
            Tick();
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Capturing), "Not checked before a quiet second.");

            _clock.Advance(0.1);
            Tick();
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Unavailable));
            Assert.That(_source.Problem.Value, Is.EqualTo(CaptureProblem.DeviceNotFound));
            Assert.That(first.Disposed, Is.True);
            Assert.That(_source.Texture.Value, Is.Null);

            _devices.Plug("Board A");
            _clock.Advance(2.0);
            Tick();
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Ready));
            Assert.That(_devices.Last, Is.Not.SameAs(first));
        }

        [Test]
        public void ABoardThatIsStillThere_IsCheckedEveryTwoSeconds_WhileQuiet()
        {
            Create();
            _source.Start();
            SendFrames(180);
            int listed = _devices.ListCalls;

            // Quiet for six seconds: checked after one, three, and five seconds.
            for (int i = 0; i < 360; i++)
            {
                Tick();
            }

            Assert.That(_devices.ListCalls - listed, Is.EqualTo(3));
            Assert.That(_source.Problem.Value, Is.EqualTo(CaptureProblem.NoSignal));
        }

        [Test]
        public void ABoardThatDoesNotPlay_IsAStartFailure()
        {
            _devices.Plays = false;
            Create();

            _source.Start();

            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Error));
            Assert.That(_source.Problem.Value, Is.EqualTo(CaptureProblem.StartFailed));
            Assert.That(_devices.Last.Disposed, Is.True);
        }

        [Test]
        public void Stop_ReleasesTheCamera()
        {
            Create();
            _source.Start();
            SendFrames(10);

            _source.Stop();

            Assert.That(_devices.Last.Disposed, Is.True);
            Assert.That(_source.Texture.Value, Is.Null);
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Stopped));
        }

        [Test]
        public void ShouldCheckDevice_OnlyAfterAQuietSecond_AndNotTooOften()
        {
            const long second = 1_000;

            Assert.That(GameCaptureSource.ShouldCheckDevice(quietSince: 0, lastCheck: -10 * second, now: 999, quietTicks: second, checkTicks: 2 * second), Is.False, "Still within the quiet second.");
            Assert.That(GameCaptureSource.ShouldCheckDevice(0, -10 * second, second, second, 2 * second), Is.True);
            Assert.That(GameCaptureSource.ShouldCheckDevice(0, 0, second, second, 2 * second), Is.False, "Checked too recently.");
            Assert.That(GameCaptureSource.ShouldCheckDevice(0, 0, 2 * second, second, 2 * second), Is.True);
        }

        private void Create()
        {
            _source = new GameCaptureSource(_devices, _settings, _clock, null, null);
        }

        private void Tick()
        {
            _clock.Advance(FrameTime);
            _source.Tick(_clock.Now);
            _devices.Last?.EndFrame();
        }

        private void SendFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _devices.Last.SendFrame(1920, 1080);
                Tick();
            }
        }
    }

    internal sealed class FakeCameraDevices : ICameraDevices, IDisposable
    {
        private readonly List<string> _plugged;

        public FakeCameraDevices(params string[] names)
        {
            _plugged = new List<string>(names);
        }

        public bool Plays { get; set; } = true;

        public int ListCalls { get; private set; }

        public List<FakeCameraStream> Opened { get; } = new List<FakeCameraStream>();

        public FakeCameraStream Last => Opened.Count > 0 ? Opened[Opened.Count - 1] : null;

        public IReadOnlyList<CaptureDevice> List()
        {
            ListCalls++;
            return _plugged.ConvertAll(name => new CaptureDevice(name, name));
        }

        public ICameraStream Open(string deviceName, int width, int height, int frameRate)
        {
            var stream = new FakeCameraStream(deviceName, (width, height, frameRate), Plays);
            Opened.Add(stream);
            return stream;
        }

        public void Unplug(string name)
        {
            _plugged.Remove(name);
        }

        public void Plug(string name)
        {
            _plugged.Add(name);
        }

        public void Dispose()
        {
            foreach (FakeCameraStream stream in Opened)
            {
                stream.Dispose();
            }
        }
    }

    internal sealed class FakeCameraStream : ICameraStream
    {
        private Texture2D _texture = new Texture2D(4, 4);

        public FakeCameraStream(string name, (int Width, int Height, int FrameRate) requested, bool playing)
        {
            Name = name;
            Requested = requested;
            IsPlaying = playing;
            Width = 16;
            Height = 16;
        }

        public string Name { get; }

        public (int Width, int Height, int FrameRate) Requested { get; }

        public bool Disposed { get; private set; }

        public Texture Texture => _texture;

        public bool DidUpdateThisFrame { get; private set; }

        public bool IsPlaying { get; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public void SendFrame(int width, int height)
        {
            Width = width;
            Height = height;
            DidUpdateThisFrame = true;
        }

        public void EndFrame()
        {
            DidUpdateThisFrame = false;
        }

        public void Dispose()
        {
            Disposed = true;
            if (_texture != null)
            {
                UnityEngine.Object.DestroyImmediate(_texture);
                _texture = null;
            }
        }
    }
}
