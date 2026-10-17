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
            foreach (CaptureEvent captureEvent in new[] { CaptureEvent.Opened, CaptureEvent.FrameArrived, CaptureEvent.DeviceMissing, CaptureEvent.Failed, CaptureEvent.NoFrame })
            {
                Assert.That(CaptureStateMachine.Next(CaptureStatus.Stopped, captureEvent, CaptureSourceKind.GameCapture), Is.EqualTo(CaptureStatus.Stopped), captureEvent.ToString());
            }
        }

        [Test]
        public void NoFrame_FlagsNoSignal_OnlyWhileReady()
        {
            Assert.That(CaptureStateMachine.Next(Status(CaptureState.Ready), CaptureEvent.NoFrame, CaptureSourceKind.GameCapture), Is.EqualTo(Status(CaptureState.Ready, CaptureProblem.NoSignal)));
            Assert.That(CaptureStateMachine.Next(Status(CaptureState.Capturing), CaptureEvent.NoFrame, CaptureSourceKind.GameCapture), Is.EqualTo(Status(CaptureState.Capturing)));
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

    public sealed class CaptureSourceBaseTests
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
        public void SelectDevice_WhileRunning_ReopensOnTheChosenDevice()
        {
            _source.Start();
            _source.SelectDevice("board-2");

            Assert.That(_source.Closes, Is.EqualTo(1));
            Assert.That(_source.OpenedId, Is.EqualTo("board-2"));
            Assert.That(_source.State.Value, Is.EqualTo(CaptureState.Ready));
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

        protected override IReadOnlyList<CaptureDevice> ListDevices()
        {
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

            if (FrameNow)
            {
                if (_texture == null)
                {
                    _texture = new Texture2D(4, 4);
                    SetTexture(_texture);
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
