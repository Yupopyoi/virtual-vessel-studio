using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;

namespace VirtualVessel.Capture.Tests
{
    public sealed class CaptureStateMachineTests
    {
        private static CaptureStatus Status(CaptureState state, CaptureProblem problem = CaptureProblem.None)
        {
            return new CaptureStatus(state, problem);
        }

        [Test]
        public void ANormalStart_GoesStartingReadyCapturing()
        {
            CaptureStatus status = CaptureStatus.Stopped;
            status = CaptureStateMachine.Next(status, CaptureEvent.StartRequested, CaptureSourceKind.GameCapture);
            Assert.That(status, Is.EqualTo(Status(CaptureState.Starting)));
            status = CaptureStateMachine.Next(status, CaptureEvent.Opened, CaptureSourceKind.GameCapture);
            Assert.That(status, Is.EqualTo(Status(CaptureState.Ready)));
            status = CaptureStateMachine.Next(status, CaptureEvent.FrameArrived, CaptureSourceKind.GameCapture);
            Assert.That(status, Is.EqualTo(Status(CaptureState.Capturing)));
        }

        [TestCase(CaptureSourceKind.GameCapture, CaptureProblem.DeviceNotFound)]
        [TestCase(CaptureSourceKind.SubScreenCapture, CaptureProblem.MonitorNotFound)]
        public void AMissingDevice_IsUnavailable_WithTheRightProblem(CaptureSourceKind kind, CaptureProblem problem)
        {
            CaptureStatus status = CaptureStateMachine.Next(Status(CaptureState.Capturing), CaptureEvent.DeviceMissing, kind);

            Assert.That(status, Is.EqualTo(Status(CaptureState.Unavailable, problem)));
            Assert.That(CaptureStateMachine.Next(status, CaptureEvent.Opened, kind), Is.EqualTo(Status(CaptureState.Ready)), "It reopens by itself.");
        }

        [Test]
        public void AStoppedSource_IgnoresEverythingButAStart()
        {
            foreach (CaptureEvent captureEvent in new[] { CaptureEvent.Opening, CaptureEvent.Opened, CaptureEvent.FrameArrived, CaptureEvent.DeviceMissing, CaptureEvent.Failed, CaptureEvent.NoFrame })
            {
                Assert.That(CaptureStateMachine.Next(CaptureStatus.Stopped, captureEvent, CaptureSourceKind.GameCapture), Is.EqualTo(CaptureStatus.Stopped), captureEvent.ToString());
            }
        }

        [Test]
        public void NotChosen_StopsAndSaysSo()
        {
            Assert.That(CaptureStateMachine.Next(Status(CaptureState.Starting), CaptureEvent.NotChosen, CaptureSourceKind.GameCapture), Is.EqualTo(Status(CaptureState.Stopped, CaptureProblem.DeviceNotChosen)));
            Assert.That(CaptureStateMachine.Next(Status(CaptureState.Stopped, CaptureProblem.DeviceNotChosen), CaptureEvent.StartRequested, CaptureSourceKind.GameCapture), Is.EqualTo(Status(CaptureState.Starting)));
        }

        [TestCase(CaptureFlip.None, 1f, 1f, 0f, 0f)]
        [TestCase(CaptureFlip.Horizontal, -1f, 1f, 1f, 0f)]
        [TestCase(CaptureFlip.Vertical, 1f, -1f, 0f, 1f)]
        [TestCase(CaptureFlip.Horizontal | CaptureFlip.Vertical, -1f, -1f, 1f, 1f)]
        public void FlipTransform_MirrorsTheUvs(CaptureFlip flip, float scaleX, float scaleY, float offsetX, float offsetY)
        {
            (Vector2 scale, Vector2 offset) = CaptureSourceBase.FlipTransform(flip);

            Assert.That(scale, Is.EqualTo(new Vector2(scaleX, scaleY)));
            Assert.That(offset, Is.EqualTo(new Vector2(offsetX, offsetY)));
        }

        [Test]
        public void Opening_StartsAgain_FromUnavailable()
        {
            Assert.That(CaptureStateMachine.Next(Status(CaptureState.Unavailable, CaptureProblem.MonitorNotFound), CaptureEvent.Opening, CaptureSourceKind.SubScreenCapture), Is.EqualTo(Status(CaptureState.Starting)));
            Assert.That(CaptureStateMachine.Next(Status(CaptureState.Starting), CaptureEvent.Opening, CaptureSourceKind.SubScreenCapture), Is.EqualTo(Status(CaptureState.Starting)));
            Assert.That(CaptureStateMachine.Next(Status(CaptureState.Capturing), CaptureEvent.Opening, CaptureSourceKind.SubScreenCapture), Is.EqualTo(Status(CaptureState.Capturing)));
        }

        [Test]
        public void NoFrame_FlagsNoSignal_WhileOpen()
        {
            Assert.That(CaptureStateMachine.Next(Status(CaptureState.Ready), CaptureEvent.NoFrame, CaptureSourceKind.GameCapture), Is.EqualTo(Status(CaptureState.Ready, CaptureProblem.NoSignal)));
            Assert.That(CaptureStateMachine.Next(Status(CaptureState.Capturing), CaptureEvent.NoFrame, CaptureSourceKind.GameCapture), Is.EqualTo(Status(CaptureState.Ready, CaptureProblem.NoSignal)), "Frames that stop are no signal too.");
            Assert.That(CaptureStateMachine.Next(Status(CaptureState.Unavailable, CaptureProblem.DeviceNotFound), CaptureEvent.NoFrame, CaptureSourceKind.GameCapture), Is.EqualTo(Status(CaptureState.Unavailable, CaptureProblem.DeviceNotFound)));
            Assert.That(CaptureStateMachine.Next(Status(CaptureState.Ready, CaptureProblem.NoSignal), CaptureEvent.FrameArrived, CaptureSourceKind.GameCapture), Is.EqualTo(Status(CaptureState.Capturing)), "A frame clears it.");
        }

        [Test]
        public void FailuresAndStop()
        {
            Assert.That(CaptureStateMachine.Next(Status(CaptureState.Starting), CaptureEvent.Failed, CaptureSourceKind.GameCapture), Is.EqualTo(Status(CaptureState.Error, CaptureProblem.StartFailed)));
            Assert.That(CaptureStateMachine.Next(Status(CaptureState.Starting), CaptureEvent.PluginMissing, CaptureSourceKind.SubScreenCapture), Is.EqualTo(Status(CaptureState.Error, CaptureProblem.PluginMissing)));
            Assert.That(CaptureStateMachine.Next(Status(CaptureState.Error, CaptureProblem.StartFailed), CaptureEvent.StartRequested, CaptureSourceKind.GameCapture), Is.EqualTo(Status(CaptureState.Starting)), "The user can start again.");
            Assert.That(CaptureStateMachine.Next(Status(CaptureState.Capturing), CaptureEvent.StopRequested, CaptureSourceKind.GameCapture), Is.EqualTo(CaptureStatus.Stopped));
        }

        [Test]
        public void Choose_PrefersTheChosenDevice_ThenTheDefault()
        {
            var devices = new[] { new CaptureDevice("a", "A"), new CaptureDevice("b", "B") };

            Assert.That(CaptureStateMachine.Choose(devices, "b", null).Id, Is.EqualTo("b"));
            Assert.That(CaptureStateMachine.Choose(devices, null, null).Id, Is.EqualTo("a"));
            Assert.That(CaptureStateMachine.Choose(devices, null, list => list[1]).Id, Is.EqualTo("b"));
            Assert.That(CaptureStateMachine.Choose(devices, "gone", null), Is.Null, "A chosen device that is gone is not replaced silently.");
            Assert.That(CaptureStateMachine.Choose(Array.Empty<CaptureDevice>(), null, null), Is.Null);
        }

        [Test]
        public void ShouldCheckAgain_OnlyWhenUnavailable_AfterTheInterval()
        {
            Assert.That(CaptureStateMachine.ShouldCheckAgain(CaptureState.Unavailable, 100, 299, 200), Is.False);
            Assert.That(CaptureStateMachine.ShouldCheckAgain(CaptureState.Unavailable, 100, 300, 200), Is.True);
            Assert.That(CaptureStateMachine.ShouldCheckAgain(CaptureState.Error, 100, 1000, 200), Is.False);
        }
    }

    public sealed class FrameRateMeterTests
    {
        private const long Second = 1_000_000;

        [Test]
        public void RateAt_IsZero_OnceASecondPassesWithoutAFrame()
        {
            long now = 5 * Second;
            FrameRateMeter meter = default(FrameRateMeter).Next(now, Second);
            for (int i = 0; i < 25; i++)
            {
                now += Second / 25;
                meter = meter.Next(now, Second);
            }

            Assert.That(meter.RateAt(now + (Second / 2), Second), Is.EqualTo(25.0));
            Assert.That(meter.RateAt(now + Second, Second), Is.EqualTo(0));
            Assert.That(default(FrameRateMeter).RateAt(now, Second), Is.EqualTo(0));
        }

        [Test]
        public void TheRate_IsCountedOverAboutASecond()
        {
            long now = 5 * Second;
            FrameRateMeter meter = default(FrameRateMeter).Next(now, Second);

            for (int i = 0; i < 24; i++)
            {
                now += Second / 25;
                meter = meter.Next(now, Second);
            }

            Assert.That(meter.Rate, Is.EqualTo(0), "Unknown until a second is complete.");

            now += Second / 25;
            meter = meter.Next(now, Second);
            Assert.That(meter.Rate, Is.EqualTo(25.0));
        }

        [Test]
        public void AGapOfASecond_StartsOver_InsteadOfLoweringTheRate()
        {
            long now = 5 * Second;
            FrameRateMeter meter = default(FrameRateMeter).Next(now, Second);
            for (int i = 0; i < 50; i++)
            {
                now += Second / 25;
                meter = meter.Next(now, Second);
            }

            now += 3 * Second;
            meter = meter.Next(now, Second);
            Assert.That(meter.Rate, Is.EqualTo(0));

            for (int i = 0; i < 25; i++)
            {
                now += Second / 25;
                meter = meter.Next(now, Second);
            }

            Assert.That(meter.Rate, Is.EqualTo(25.0));
        }
    }

    public sealed partial class CaptureSourceBaseTests
    {
        private ManualClock _clock;
        private FakeSource _source;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _source = new FakeSource(_clock);
        }

        [TearDown]
        public void TearDown()
        {
            _source.Dispose();
        }

        [Test]
        public void Start_OpensTheDefaultDevice_AndFramesMakeItCapturing()
        {
            var states = new List<CaptureState>();
            _source.State.Subscribe(states.Add);

            _source.Start();
            Assert.That(_source.OpenedId, Is.EqualTo("board-1"));
            _source.Tick(_clock.Now);
            _source.FrameNow = true;
            _source.Tick(_clock.Now);

            Assert.That(states, Is.EqualTo(new[] { CaptureState.Starting, CaptureState.Ready, CaptureState.Capturing }));
            Assert.That(_source.Texture.Value, Is.Not.Null);
        }

        [Test]
        public void AMissingDevice_IsUnavailable_AndIsLookedForAgain()
        {
            _source.Available.Clear();
            _source.Start();
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Unavailable));
            Assert.That(_source.Problem.Value, Is.EqualTo(CaptureProblem.DeviceNotFound));

            _source.Available.Add(new CaptureDevice("board-1", "Board 1"));
            _clock.Advance(1.0);
            _source.Tick(_clock.Now);
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Unavailable), "Not before the retry interval.");

            _clock.Advance(1.0);
            _source.Tick(_clock.Now);
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Ready), "Found again after two seconds.");
        }

        [Test]
        public void ADefaultDevice_StaysChosen_AndIsWaitedForWhenItGoesAway()
        {
            _source.Start();
            Assert.That(_source.SelectedDeviceId, Is.EqualTo("board-1"));

            _source.Available.RemoveAt(0);
            _source.LoseDevice = true;
            _source.Tick(_clock.Now);
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Unavailable));

            _clock.Advance(2.0);
            _source.Tick(_clock.Now);
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Unavailable), "board-2 is there, but it was not chosen.");
            Assert.That(_source.OpenedId, Is.Null);
        }

        [Test]
        public void SelectDevice_WhileRunning_ReopensOnTheChosenDevice()
        {
            _source.Start();
            _source.SelectDevice("board-2");

            Assert.That(_source.Closes, Is.EqualTo(1));
            Assert.That(_source.OpenedId, Is.EqualTo("board-2"));
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Ready));
        }

        [Test]
        public void AFlip_MirrorsTheVideo_AndIsReleasedOnStop()
        {
            _source.SetFlip(CaptureFlip.Horizontal);
            _source.Start();
            _source.FrameNow = true;
            _source.Tick(_clock.Now);

            var mirrored = _source.Texture.Value as RenderTexture;
            Assert.That(mirrored, Is.Not.Null);
            Color32[] pixels = ReadBack(mirrored);
            Assert.That(pixels[0].b, Is.EqualTo(255), "Blue was on the right and is now on the left.");
            Assert.That(pixels[1].r, Is.EqualTo(255));

            _source.Stop();
            Assert.That(mirrored == null, Is.True, "The mirrored copy is destroyed.");
        }

        [Test]
        public void SelectDevice_WhileStopped_OnlyRemembers()
        {
            _source.SelectDevice("board-2");

            Assert.That(_source.SelectedDeviceId, Is.EqualTo("board-2"));
            Assert.That(_source.OpenedId, Is.Null);
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Stopped));
        }

        [Test]
        public void Stop_ReleasesTheDevice_AndClearsTheTexture()
        {
            _source.Start();
            _source.FrameNow = true;
            _source.Tick(_clock.Now);

            _source.Stop();

            Assert.That(_source.Closes, Is.EqualTo(1));
            Assert.That(_source.Texture.Value, Is.Null);
            Assert.That(_source.Info.Value, Is.EqualTo(default(CaptureSourceInfo)), "Nothing is delivered any more.");
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Stopped));
        }

        [Test]
        public void AFailureWhileCapturing_ClosesAndReportsAnError()
        {
            _source.Start();
            _source.ThrowOnTick = true;

            _source.Tick(_clock.Now);

            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Error));
            Assert.That(_source.Problem.Value, Is.EqualTo(CaptureProblem.StartFailed));
            Assert.That(_source.Closes, Is.EqualTo(1));
        }

        [Test]
        public void AMissingPlugin_IsAPluginProblem_AndRefreshingKeepsTheState()
        {
            _source.ThrowOnList = new CapturePluginMissingException("no plugin");

            _source.RefreshDevices();
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Stopped), "Refreshing the list changes no state.");
            Assert.That(_source.Devices, Is.Empty);

            _source.Start();
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Error));
            Assert.That(_source.Problem.Value, Is.EqualTo(CaptureProblem.PluginMissing));
        }

        [Test]
        public void AFailedListing_WhileCapturing_LeavesTheCaptureAlone()
        {
            _source.Start();
            _source.FrameNow = true;
            _source.Tick(_clock.Now);
            _source.ThrowOnList = new InvalidOperationException("listing failed");

            _source.RefreshDevices();

            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Capturing));
            Assert.That(_source.Closes, Is.EqualTo(0));
        }

        [Test]
        public void AFailureWhileOpening_IsAnError_AndStartingAgainWorks()
        {
            _source.ThrowOnOpen = true;
            _source.Start();
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Error));

            _source.ThrowOnOpen = false;
            _source.Start();
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Ready));
        }
    }

    public sealed partial class CaptureSourceBaseTests
    {
        private static Color32[] ReadBack(RenderTexture texture)
        {
            RenderTexture active = RenderTexture.active;
            var read = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = texture;
                read.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                read.Apply();
                return read.GetPixels32();
            }
            finally
            {
                RenderTexture.active = active;
                UnityEngine.Object.DestroyImmediate(read);
            }
        }
    }

    public sealed class CaptureServiceTests
    {
        [Test]
        public void TheService_TicksItsSourcesEveryFrame_AndStopsThemOnShutdown()
        {
            var clock = new ManualClock();
            var loop = new FrameLoop();
            var source = new FakeSource(clock);
            var service = new CaptureService(new CaptureSourceBase[] { source }, loop, clock, null);

            service.InitializeAsync(CancellationToken.None).Wait();
            source.Start();
            source.FrameNow = true;
            loop.Tick(null);
            Assert.That(source.State.Value, Is.EqualTo(CaptureState.Capturing), "Ticked by the frame loop.");
            Assert.That(service.Find("fake"), Is.SameAs(source));
            Assert.That(service.Find("nothing"), Is.Null);

            service.ShutdownAsync(CancellationToken.None).Wait();
            Assert.That(source.State.Value, Is.EqualTo(CaptureState.Stopped));
            service.Dispose();
            Assert.That(loop.Count, Is.EqualTo(0), "The frame work is removed on dispose.");
        }
    }

    internal sealed class FakeSource : CaptureSourceBase
    {
        private Texture2D _texture;

        public FakeSource(IMonotonicClock clock)
            : base("fake", CaptureSourceKind.GameCapture, clock, null, null)
        {
        }

        public List<CaptureDevice> Available { get; } = new List<CaptureDevice> { new CaptureDevice("board-1", "Board 1"), new CaptureDevice("board-2", "Board 2") };

        public string OpenedId { get; private set; }

        public int Closes { get; private set; }

        public bool FrameNow { get; set; }

        public bool ThrowOnOpen { get; set; }

        public bool ThrowOnTick { get; set; }

        public Exception ThrowOnList { get; set; }

        public bool LoseDevice { get; set; }

        protected override IReadOnlyList<CaptureDevice> ListDevices()
        {
            if (ThrowOnList != null)
            {
                throw ThrowOnList;
            }

            return Available.ToArray();
        }

        protected override void Open(CaptureDevice device)
        {
            if (ThrowOnOpen)
            {
                throw new InvalidOperationException("cannot open");
            }

            OpenedId = device.Id;
            Report(CaptureEvent.Opened);
        }

        protected override void Close()
        {
            if (OpenedId == null && _texture == null)
            {
                return;
            }

            Closes++;
            OpenedId = null;
            if (_texture != null)
            {
                UnityEngine.Object.DestroyImmediate(_texture);
                _texture = null;
            }
        }

        protected override void OnTick(long now)
        {
            if (ThrowOnTick)
            {
                throw new InvalidOperationException("lost");
            }

            if (LoseDevice)
            {
                LoseDevice = false;
                ReportDeviceLost();
                return;
            }

            if (FrameNow)
            {
                if (_texture == null)
                {
                    // Red on the left, blue on the right, to see a flip.
                    _texture = new Texture2D(2, 1, TextureFormat.RGBA32, false);
                    _texture.SetPixels32(new[] { new Color32(255, 0, 0, 255), new Color32(0, 0, 255, 255) });
                    _texture.Apply();
                    SetTexture(_texture);
                    SetInfo(new CaptureSourceInfo(4, 4, 0, OpenDevice.DisplayName));
                }

                ReportFrame(now);
            }
        }
    }

    internal sealed class ManualClock : IMonotonicClock
    {
        private long _now = 1_000_000;

        public long Frequency => 1_000_000;

        public long Now => _now;

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
