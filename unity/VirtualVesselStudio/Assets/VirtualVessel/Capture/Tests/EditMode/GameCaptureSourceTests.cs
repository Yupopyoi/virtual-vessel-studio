using System.Linq;
using NUnit.Framework;
using VirtualVessel.Capture.Game;
using VirtualVessel.Capture.SubScreen;

namespace VirtualVessel.Capture.Tests
{
    /// <summary>The game screen from a capture board or a game's window on this PC (capture detailed design 8.6).</summary>
    public sealed class GameCaptureSourceTests
    {
        private const double FrameTime = 1.0 / 60;
        private const string Board = "MiraBox Capture";
        private const string GameWindow = "window:game.exe";

        private ManualClock _clock;
        private FakeCameraDevices _cameras;
        private FakeDesktopDuplication _plugin;
        private WebCamCaptureSource _source;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _cameras = new FakeCameraDevices(Board);
            _plugin = new FakeDesktopDuplication();
            _plugin.Windows.Add(new WindowDescription("game.exe", "My Game", 1280, 720));
        }

        [TearDown]
        public void TearDown()
        {
            _source?.Dispose();
            _cameras.Dispose();
            _plugin.Dispose();
        }

        [Test]
        public void TheDevices_AreTheBoards_ThenTheWindows()
        {
            Create(null);
            _source.RefreshDevices();

            Assert.That(_source.Devices.Select(d => d.Id), Is.EqualTo(new[] { Board, GameWindow }));
            Assert.That(_source.Devices[1].DisplayName, Is.EqualTo("My Game (game.exe)"));
        }

        [Test]
        public void AChosenWindow_IsCaptured_WithoutTheCursor_AndItsFramesAreShown()
        {
            Create(GameWindow);
            _source.Start();

            Assert.That(_plugin.Last.DeviceName, Is.EqualTo("game.exe"), "Opened by its program.");
            Assert.That(_plugin.Last.DrawPointer, Is.False, "A game draws its own cursor.");
            Assert.That(_cameras.Opened, Is.Empty, "No camera is opened for a window.");
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Starting));

            _plugin.Last.State = DuplicationState.Running;
            _plugin.Last.SendFrame(1280, 720);
            Tick();

            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Capturing));
            Assert.That(_source.Texture.Value, Is.Not.Null);
            Assert.That(_source.Info.Value.Width, Is.EqualTo(1280));
            Assert.That(_source.Info.Value.DeviceName, Is.EqualTo("My Game (game.exe)"));

            // A still window sends nothing; that is not a missing signal.
            for (int i = 0; i < 600; i++)
            {
                Tick();
            }

            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Capturing));
            Assert.That(_source.Problem.Value, Is.EqualTo(CaptureProblem.None));
        }

        [Test]
        public void AClosedGame_IsWaitedFor_AndCapturedAgainWhenItComesBack()
        {
            Create(GameWindow);
            _source.Start();
            FakeDuplicationSession first = _plugin.Last;
            first.State = DuplicationState.Running;
            first.SendFrame(1280, 720);
            Tick();

            first.State = DuplicationState.MonitorLost;
            _plugin.Windows.Clear();
            Tick();
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Unavailable));
            Assert.That(first.Disposed, Is.True);
            Assert.That(_source.SelectedDeviceId, Is.EqualTo(GameWindow), "It waits for the same game, not another window.");

            _clock.Advance(CaptureSourceBase.RetryInterval.TotalSeconds);
            Tick();
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Unavailable), "Not running yet.");

            _plugin.Windows.Add(new WindowDescription("game.exe", "My Game", 1280, 720));
            _clock.Advance(CaptureSourceBase.RetryInterval.TotalSeconds);
            Tick();
            Assert.That(_plugin.Last, Is.Not.SameAs(first), "Opened again.");
        }

        [Test]
        public void ChoosingABoardAfterAWindow_ClosesTheWindow_AndOpensTheBoard()
        {
            Create(GameWindow);
            _source.Start();
            FakeDuplicationSession window = _plugin.Last;

            _source.SelectDevice(Board);

            Assert.That(window.Disposed, Is.True);
            Assert.That(_cameras.Opened.Count, Is.EqualTo(1));
        }

        [Test]
        public void WithoutThePlugin_TheBoardsAreStillOffered()
        {
            _plugin.PluginMissing = true;
            Create(null);
            _source.RefreshDevices();

            Assert.That(_source.Devices.Select(d => d.Id), Is.EqualTo(new[] { Board }));
        }

        [Test]
        public void WindowIds_NameTheProgram()
        {
            Assert.That(CaptureWindow.IdOf("game.exe"), Is.EqualTo(GameWindow));
            Assert.That(CaptureWindow.ProgramOf(GameWindow), Is.EqualTo("game.exe"));
            Assert.That(CaptureWindow.ProgramOf(Board), Is.Null);
            Assert.That(CaptureWindow.IsWindow("window:"), Is.False);
            Assert.That(CaptureWindow.IsWindow(null), Is.False);
        }

        private void Create(string deviceId)
        {
            _source = WebCamCaptureSource.ForGame(_cameras, new CaptureSettings { GameDeviceId = deviceId }, _clock, null, null, windows: _plugin);
        }

        private void Tick()
        {
            _clock.Advance(FrameTime);
            _source.Tick(_clock.Now);
            _cameras.Last?.EndFrame();
        }
    }
}
