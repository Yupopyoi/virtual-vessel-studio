using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Capture.SubScreen;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Performance;

namespace VirtualVessel.Capture.Tests
{
    public sealed class SubScreenCaptureSourceTests
    {
        private const string MainMonitor = @"\\.\DISPLAY1";
        private const string SubMonitor = @"\\.\DISPLAY2";
        private const double FrameTime = 1.0 / 60;

        private ManualClock _clock;
        private FakeDesktopDuplication _duplication;
        private PerformanceMetricsService _metrics;
        private SubScreenCaptureSource _source;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _duplication = new FakeDesktopDuplication(new MonitorDescription(MainMonitor, 3840, 2160, isPrimary: true), new MonitorDescription(SubMonitor, 1920, 1080, isPrimary: false));
            _metrics = new PerformanceMetricsService(new PerformanceSettings { DetailedEnabled = true }, _clock, new UtcSystemClock(), log: null);
            _source = new SubScreenCaptureSource(_duplication, new CaptureSettings(), _clock, _metrics, null);
        }

        [TearDown]
        public void TearDown()
        {
            _source.Dispose();
            _duplication.Dispose();
        }

        [Test]
        public void Start_OpensTheFirstMonitorThatIsNotTheMainOne_WithoutWaiting()
        {
            _source.Start();

            Assert.That(_duplication.Last.DeviceName, Is.EqualTo(SubMonitor));
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Starting), "The native side opens it on its own thread.");
            Assert.That(_source.Info.Value.DeviceName, Is.EqualTo("DISPLAY2 (1920×1080)"));
        }

        [Test]
        public void TheChosenMonitor_IsOpenedByItsDeviceName()
        {
            var source = new SubScreenCaptureSource(_duplication, new CaptureSettings { SubScreenMonitorId = MainMonitor }, _clock, null, null);
            try
            {
                source.Start();
                Assert.That(_duplication.Last.DeviceName, Is.EqualTo(MainMonitor));
            }
            finally
            {
                source.Dispose();
            }
        }

        [Test]
        public void DefaultMonitor_IsTheMainOne_WhenItIsAlone()
        {
            var main = new MonitorDescription(MainMonitor, 1920, 1080, isPrimary: true);
            var sub = new MonitorDescription(SubMonitor, 1280, 1024, isPrimary: false);

            Assert.That(SubScreenCaptureSource.DefaultMonitor(new[] { main, sub }), Is.EqualTo(SubMonitor));
            Assert.That(SubScreenCaptureSource.DefaultMonitor(new[] { main }), Is.EqualTo(MainMonitor));
            Assert.That(SubScreenCaptureSource.DefaultMonitor(Array.Empty<MonitorDescription>()), Is.Null);
        }

        [Test]
        public void TheDeviceList_NamesMonitorsWithoutWords()
        {
            _source.RefreshDevices();

            Assert.That(_source.Devices.Select(d => d.Id), Is.EqualTo(new[] { MainMonitor, SubMonitor }));
            Assert.That(_source.Devices.Select(d => d.DisplayName), Is.EqualTo(new[] { "DISPLAY1 (3840×2160)", "DISPLAY2 (1920×1080)" }));
        }

        [Test]
        public void Running_IsReady_AndTheFirstFrameIsUploadedAsItIs()
        {
            _source.Start();
            _duplication.Last.State = DuplicationState.Running;
            Tick();
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Ready));

            _duplication.Last.SendFrame(4, 2, blue: 10, green: 20, red: 30);
            Tick();

            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Capturing));
            var texture = (Texture2D)_source.Texture.Value;
            Assert.That(texture.width, Is.EqualTo(4));
            Assert.That(texture.height, Is.EqualTo(2));
            Assert.That(texture.format, Is.EqualTo(TextureFormat.BGRA32));
            Color32 pixel = texture.GetPixels32()[0];
            Assert.That((pixel.r, pixel.g, pixel.b, pixel.a), Is.EqualTo(((byte)30, (byte)20, (byte)10, (byte)255)), "B8G8R8A8 bytes are BGRA32.");
            Assert.That(_source.Info.Value.Width, Is.EqualTo(4));
            Assert.That(Timer("subscreen.Upload").SampleCount, Is.EqualTo(1));
        }

        [Test]
        public void OnlyNewFrames_AreTakenAndUploaded()
        {
            StartRunning();
            _duplication.Last.SendFrame(4, 2);
            Tick();
            Tick();
            Tick();

            Assert.That(_duplication.Last.Takes, Is.EqualTo(1));
            Assert.That(Timer("subscreen.Upload").SampleCount, Is.EqualTo(1));

            _duplication.Last.SendFrame(4, 2);
            Tick();
            Assert.That(_duplication.Last.Takes, Is.EqualTo(2));
        }

        [Test]
        public void ANewSize_GivesANewTexture_AndReleasesTheOld()
        {
            StartRunning();
            _duplication.Last.SendFrame(4, 2);
            Tick();
            Texture first = _source.Texture.Value;

            _duplication.Last.SendFrame(8, 4);
            Tick();

            Assert.That(_source.Texture.Value, Is.Not.SameAs(first));
            Assert.That(_source.Texture.Value.width, Is.EqualTo(8));
            Assert.That(first == null, Is.True, "The old texture is destroyed.");
        }

        [Test]
        public void OverwrittenFrames_AndRecoveries_AreCounted()
        {
            StartRunning();
            _duplication.Last.SendFrame(4, 2);
            _duplication.Last.Overwritten = 3;
            _duplication.Last.Recoveries = 1;
            Tick();
            _duplication.Last.Overwritten = 5;
            Tick();

            Assert.That(_metrics.Counter("Capture", "subscreen.Skipped").Value, Is.EqualTo(5));
            Assert.That(_metrics.Counter("Capture", "subscreen.Recovered").Value, Is.EqualTo(1));
        }

        [Test]
        public void Recovering_KeepsCapturing_WithTheLastImage()
        {
            StartRunning();
            _duplication.Last.SendFrame(4, 2);
            Tick();
            Texture texture = _source.Texture.Value;

            _duplication.Last.State = DuplicationState.Recovering;
            _duplication.Last.LastError = unchecked((int)0x887A0026);
            Tick();

            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Capturing));
            Assert.That(_source.Texture.Value, Is.SameAs(texture));
        }

        [Test]
        public void AMonitorThatGoesAway_IsUnavailable_AndReopensWhenBack()
        {
            StartRunning();
            _duplication.Last.SendFrame(4, 2);
            Tick();
            FakeDuplicationSession first = _duplication.Last;

            first.State = DuplicationState.MonitorLost;
            _duplication.Monitors.RemoveAt(1);
            Tick();
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Unavailable));
            Assert.That(_source.Problem.Value, Is.EqualTo(CaptureProblem.MonitorNotFound));
            Assert.That(first.Disposed, Is.True);
            Assert.That(_source.Texture.Value, Is.Null);

            _clock.Advance(2.0);
            Tick();
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Unavailable), "The chosen monitor is not back yet.");

            _duplication.Monitors.Add(new MonitorDescription(SubMonitor, 1920, 1080, isPrimary: false));
            _clock.Advance(2.0);
            Tick();
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Starting));
            Assert.That(_duplication.Last, Is.Not.SameAs(first));
            Assert.That(_duplication.Last.DeviceName, Is.EqualTo(SubMonitor));
        }

        [Test]
        public void ADuplicationThatGivesUp_IsAStartFailure()
        {
            StartRunning();
            _duplication.Last.State = DuplicationState.Failed;
            _duplication.Last.LastError = unchecked((int)0x887A0022);

            Tick();

            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Error));
            Assert.That(_source.Problem.Value, Is.EqualTo(CaptureProblem.StartFailed));
            Assert.That(_duplication.Last.Disposed, Is.True);
        }

        [Test]
        public void AMissingPlugin_IsPluginMissing()
        {
            _duplication.PluginMissing = true;

            _source.Start();

            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Error));
            Assert.That(_source.Problem.Value, Is.EqualTo(CaptureProblem.PluginMissing));
        }

        [Test]
        public void AStillScreen_KeepsCapturing_ButShowsNoFrameRate()
        {
            StartRunning();
            for (int i = 0; i < 120; i++)
            {
                _duplication.Last.SendFrame(4, 2);
                Tick();
            }

            Assert.That(_source.Info.Value.FrameRate, Is.GreaterThan(50));

            for (int i = 0; i < 61; i++)
            {
                Tick();
            }

            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Capturing), "A still screen sends no frames; that is not a failure.");
            Assert.That(_source.Info.Value.FrameRate, Is.EqualTo(0));
        }

        [Test]
        public void Stop_ClosesTheSession_AndReleasesTheTexture()
        {
            StartRunning();
            _duplication.Last.SendFrame(4, 2);
            Tick();
            Texture texture = _source.Texture.Value;

            _source.Stop();

            Assert.That(_duplication.Last.Disposed, Is.True);
            Assert.That(_source.Texture.Value, Is.Null);
            Assert.That(texture == null, Is.True);
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Stopped));
        }

        private void StartRunning()
        {
            _source.Start();
            _duplication.Last.State = DuplicationState.Running;
            Tick();
        }

        private void Tick()
        {
            _clock.Advance(FrameTime);
            _source.Tick(_clock.Now);
        }

        private MetricSnapshot Timer(string name)
        {
            return _metrics.GetSnapshot().Metrics.Single(m => m.Module == "Capture" && m.Name == name);
        }
    }

    internal sealed class FakeDesktopDuplication : IDesktopDuplication, IDisposable
    {
        public FakeDesktopDuplication(params MonitorDescription[] monitors)
        {
            Monitors = new List<MonitorDescription>(monitors);
        }

        public List<MonitorDescription> Monitors { get; }

        public bool PluginMissing { get; set; }

        public List<FakeDuplicationSession> Opened { get; } = new List<FakeDuplicationSession>();

        public FakeDuplicationSession Last => Opened.Count > 0 ? Opened[Opened.Count - 1] : null;

        public IReadOnlyList<MonitorDescription> ListMonitors()
        {
            if (PluginMissing)
            {
                throw new CapturePluginMissingException("The plugin is missing in this test.");
            }

            return Monitors.ToArray();
        }

        public IDuplicationSession Open(string deviceName)
        {
            var session = new FakeDuplicationSession(deviceName);
            Opened.Add(session);
            return session;
        }

        public void Dispose()
        {
            foreach (FakeDuplicationSession session in Opened)
            {
                session.Dispose();
            }
        }
    }

    internal sealed class FakeDuplicationSession : IDuplicationSession
    {
        private IntPtr _pixels;
        private int _capacity;
        private DuplicationFrame _latest;

        public FakeDuplicationSession(string deviceName)
        {
            DeviceName = deviceName;
        }

        public string DeviceName { get; }

        public DuplicationState State { get; set; } = DuplicationState.Starting;

        public int LastError { get; set; }

        public ulong Overwritten { get; set; }

        public uint Recoveries { get; set; }

        public int Takes { get; private set; }

        public bool Disposed { get; private set; }

        /// <summary>A frame of one color in native memory, as the plugin hands them over.</summary>
        public void SendFrame(int width, int height, byte blue = 10, byte green = 20, byte red = 30)
        {
            int size = width * height * 4;
            if (size > _capacity)
            {
                Free();
                _pixels = Marshal.AllocHGlobal(size);
                _capacity = size;
            }

            var bytes = new byte[size];
            for (int i = 0; i < size; i += 4)
            {
                bytes[i] = blue;
                bytes[i + 1] = green;
                bytes[i + 2] = red;
                bytes[i + 3] = 255;
            }

            Marshal.Copy(bytes, 0, _pixels, size);
            _latest = new DuplicationFrame(_pixels, width, height, _latest.Number + 1);
        }

        public DuplicationStatus GetStatus()
        {
            ThrowIfDisposed();
            return new DuplicationStatus(State, LastError, _latest.Width, _latest.Height, _latest.Number, Overwritten, Recoveries);
        }

        public bool TryTakeFrame(ulong after, out DuplicationFrame frame)
        {
            ThrowIfDisposed();
            Takes++;
            frame = _latest.Number > after ? _latest : default;
            return _latest.Number > after;
        }

        public void Dispose()
        {
            Disposed = true;
            Free();
        }

        private void Free()
        {
            if (_pixels != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_pixels);
                _pixels = IntPtr.Zero;
                _capacity = 0;
            }
        }

        private void ThrowIfDisposed()
        {
            if (Disposed)
            {
                throw new ObjectDisposedException(nameof(FakeDuplicationSession));
            }
        }
    }
}
