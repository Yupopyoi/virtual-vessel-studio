using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using VirtualVessel.Capture.Game;
using VirtualVessel.Capture.SubScreen;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.Capture.Tests
{
    public sealed class CaptureSettingsTests
    {
        private string _directory;
        private ManualClock _clock;
        private FrameLoop _loop;
        private FakeCameraDevices _cameras;
        private FakeDesktopDuplication _monitors;
        private CaptureService _service;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _clock = new ManualClock();
            _loop = new FrameLoop();
            _cameras = new FakeCameraDevices("Board A", "Board B");
            _monitors = new FakeDesktopDuplication(new MonitorDescription(@"\\.\DISPLAY1", 1920, 1080, isPrimary: true), new MonitorDescription(@"\\.\DISPLAY2", 1280, 720, isPrimary: false));
            _service = Create();
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
            _cameras.Dispose();
            _monitors.Dispose();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Test]
        public void Choices_AreSaved_HalfASecondAfterTheLastChange()
        {
            Game.SelectDevice("Board B");
            Game.Start();
            Sub.SetFlip(CaptureFlip.Vertical);
            _loop.Tick(null);
            Assert.That(File.Exists(SettingsFile), Is.False, "Not before the delay.");

            _clock.Advance(0.5);
            _loop.Tick(null);
            Flush();

            CaptureSettings saved = Load();
            Assert.That(saved.GameDeviceId, Is.EqualTo("Board B"));
            Assert.That(saved.GameEnabled, Is.True);
            Assert.That(saved.SubScreenFlip, Is.EqualTo(CaptureFlip.Vertical));
            Assert.That(saved.SubScreenEnabled, Is.False);
        }

        [Test]
        public void SourcesThatWereStarted_StartAgain_WithTheirChoices()
        {
            Game.SelectDevice("Board B");
            Game.Start();
            Sub.Start();

            // Shutting down saves before stopping, so the sources count as started.
            _service.ShutdownAsync(CancellationToken.None).Wait();
            _service.Dispose();
            _service = Create();

            Assert.That(Game.State.Value, Is.Not.EqualTo(CaptureState.Stopped));
            Assert.That(_cameras.Last.Name, Is.EqualTo("Board B"));
            Assert.That(Sub.State.Value, Is.EqualTo(CaptureState.Starting));
        }

        [Test]
        public void TheCursorOption_IsSaved_AndReopensARunningMonitor()
        {
            Sub.Start();
            FakeDuplicationSession first = _monitors.Last;

            ((IPointerOption)Sub).SetDrawPointer(false);

            Assert.That(first.Disposed, Is.True);
            Assert.That(_monitors.Last.DrawPointer, Is.False);
            _clock.Advance(0.5);
            _loop.Tick(null);
            Flush();
            Assert.That(Load().SubScreenDrawPointer, Is.False);
        }

        [Test]
        public void TheTrackingCamera_IsSavedAndRestored_Too()
        {
            Camera.SelectDevice("Board A");
            Camera.Start();
            _service.ShutdownAsync(CancellationToken.None).Wait();

            CaptureSettings saved = Load();
            Assert.That(saved.CameraDeviceId, Is.EqualTo("Board A"));
            Assert.That(saved.CameraEnabled, Is.True);
            Assert.That(saved.GameEnabled, Is.False, "The other sources keep their own choices.");

            _service.Dispose();
            _service = Create();
            Assert.That(Camera.State.Value, Is.Not.EqualTo(CaptureState.Stopped));
        }

        [Test]
        public void IsOpen_MatchesTheDeviceTheOtherSourceHasOpen()
        {
            Game.SelectDevice("Board B");
            Game.Start();
            var game = (CaptureSourceBase)GameSource;

            Assert.That(CaptureService.IsOpen(game, "Board B"), Is.True);
            Assert.That(CaptureService.IsOpen(game, "Board A"), Is.False);
            Assert.That(CaptureService.IsOpen(null, "Board B"), Is.False);
        }

        [Test]
        public void WasStarted_ReadsEachSourcesFlag()
        {
            var settings = new CaptureSettings { GameEnabled = true, CameraEnabled = true };

            Assert.That(CaptureService.WasStarted(settings, WebCamCaptureSource.GameSourceId), Is.True);
            Assert.That(CaptureService.WasStarted(settings, WebCamCaptureSource.CameraSourceId), Is.True);
            Assert.That(CaptureService.WasStarted(settings, SubScreenCaptureSource.SourceId), Is.False);
            Assert.That(CaptureService.WasStarted(settings, "other"), Is.False);
        }

        private string SettingsFile => Path.Combine(_directory, CaptureSettings.FileName);

        private IVideoCaptureSource GameSource => _service.Find(WebCamCaptureSource.GameSourceId);

        private ICaptureDeviceSource Game => (ICaptureDeviceSource)GameSource;

        private ICaptureDeviceSource Sub => (ICaptureDeviceSource)_service.Find(SubScreenCaptureSource.SourceId);

        private ICaptureDeviceSource Camera => (ICaptureDeviceSource)_service.Find(WebCamCaptureSource.CameraSourceId);

        private CaptureService Create()
        {
            ConfigFileStore<CaptureSettings> store = CaptureService.CreateStore(_directory, new UtcSystemClock());
            CaptureSettings settings = store.Load().Value;
            var saves = new ConfigSaveScheduler<CaptureSettings>(store, _clock, TimeSpan.FromSeconds(0.5), null);
            var sources = new CaptureSourceBase[]
            {
                WebCamCaptureSource.ForGame(_cameras, settings, _clock, null, null),
                new SubScreenCaptureSource(_monitors, settings, _clock, null, null),
                WebCamCaptureSource.ForTrackingCamera(_cameras, settings, _clock, null, null),
            };
            var service = new CaptureService(sources, _loop, _clock, null, settings, saves);
            service.InitializeAsync(CancellationToken.None).Wait();
            return service;
        }

        private CaptureSettings Load()
        {
            return CaptureService.CreateStore(_directory, new UtcSystemClock()).Load().Value;
        }

        private void Flush()
        {
            // Shutting down flushes; the sources are stopped afterwards, which the file must not see.
            _service.ShutdownAsync(CancellationToken.None).Wait();
        }
    }
}
