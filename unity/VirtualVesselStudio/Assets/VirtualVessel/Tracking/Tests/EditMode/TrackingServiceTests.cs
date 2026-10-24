using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Capture;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.Tracking.Recording;

namespace VirtualVessel.Tracking.Tests
{
    public sealed class TrackingStatusTests
    {
        private static (TrackingState, TrackingProblem) Describe(CaptureState camera, CaptureProblem problem = CaptureProblem.None, TrackingState provider = TrackingState.Running, bool personSeen = true, bool wanted = true, bool hasProvider = true)
        {
            return TrackingService.Describe(wanted, hasProvider, true, camera, problem, provider, personSeen);
        }

        [Test]
        public void NotStarted_IsStopped_WhateverTheCamera()
        {
            Assert.That(Describe(CaptureState.Error, wanted: false), Is.EqualTo((TrackingState.Stopped, TrackingProblem.None)));
        }

        [Test]
        public void CameraProblems_BecomeTrackingProblems()
        {
            Assert.That(Describe(CaptureState.Stopped, CaptureProblem.DeviceNotChosen), Is.EqualTo((TrackingState.Unavailable, TrackingProblem.CameraNotChosen)));
            Assert.That(Describe(CaptureState.Unavailable, CaptureProblem.DeviceNotFound), Is.EqualTo((TrackingState.Unavailable, TrackingProblem.CameraNotFound)));
            Assert.That(Describe(CaptureState.Error, CaptureProblem.DeviceInUse), Is.EqualTo((TrackingState.Error, TrackingProblem.CameraInUse)));
            Assert.That(Describe(CaptureState.Error, CaptureProblem.StartFailed), Is.EqualTo((TrackingState.Error, TrackingProblem.StartFailed)));
            Assert.That(Describe(CaptureState.Ready), Is.EqualTo((TrackingState.Starting, TrackingProblem.None)), "Waiting for the first image.");
        }

        [Test]
        public void WithTheCameraCapturing_TheProviderDecides()
        {
            Assert.That(Describe(CaptureState.Capturing, provider: TrackingState.Starting), Is.EqualTo((TrackingState.Starting, TrackingProblem.None)));
            Assert.That(Describe(CaptureState.Capturing, provider: TrackingState.Error), Is.EqualTo((TrackingState.Error, TrackingProblem.StartFailed)));
            Assert.That(Describe(CaptureState.Capturing), Is.EqualTo((TrackingState.Running, TrackingProblem.None)));
            Assert.That(Describe(CaptureState.Capturing, personSeen: false), Is.EqualTo((TrackingState.Running, TrackingProblem.NoPerson)));
        }

        [Test]
        public void WithoutAProvider_TrackingCannotStart()
        {
            Assert.That(Describe(CaptureState.Capturing, hasProvider: false), Is.EqualTo((TrackingState.Error, TrackingProblem.StartFailed)));
        }

        [Test]
        public void FullBody_IsNotAvailableYet()
        {
            Assert.That(TrackingOptions.Available(BodyRange.FullBody), Is.EqualTo(BodyRange.UpperBody));
            Assert.That(TrackingOptions.Available(BodyRange.UpperBody), Is.EqualTo(BodyRange.UpperBody));
        }
    }

    public sealed class TrackingServiceTests
    {
        private string _directory;
        private SecondsClock _clock;
        private FrameLoop _loop;
        private FakeCamera _camera;
        private TrackingService _service;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _clock = new SecondsClock();
            _loop = new FrameLoop();
            _camera = new FakeCamera();
        }

        [TearDown]
        public void TearDown()
        {
            // Shutting down finishes a pending save before the folder is deleted.
            _service?.ShutdownAsync(CancellationToken.None).Wait();
            _service?.Dispose();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Test]
        public void Starting_StartsTheCamera_AndStoppingStopsIt()
        {
            _service = Create();

            _service.Start();
            Assert.That(_camera.Started, Is.True);
            Assert.That(_service.State.Value, Is.EqualTo(TrackingState.Running));

            _service.Stop();
            Assert.That(_camera.Started, Is.False, "The tracking camera is used only for tracking.");
            Assert.That(_service.State.Value, Is.EqualTo(TrackingState.Stopped));
        }

        [Test]
        public void ReplayedObservations_AreSmoothedIntoCurrent()
        {
            _service = Create();
            _service.Start();

            for (int i = 0; i < 20; i++)
            {
                Frame();
            }

            Assert.That(_service.Current.Body.Status, Is.EqualTo(TrackingStatus.Tracked));
            Assert.That(_service.Current.Body[BodyJoint.RightShoulder], Is.EqualTo(new Vector3(0.18f, 0.45f, 0f)));
            Assert.That(_service.Problem.Value, Is.EqualTo(TrackingProblem.None));
        }

        [Test]
        public void InTwoD_TheBodyLosesItsDepth_AndTheFaceAndRecordingsKeepTheirs()
        {
            _service = Create();
            _service.SetOptions(_service.Options.Value.WithBodyDepth(BodyDepth.Flat));
            var recorded = new List<float>();
            _service.Observed += frame => recorded.Add(frame.Body[BodyJoint.Nose].z);
            _service.Start();

            for (int i = 0; i < 20; i++)
            {
                Frame();
            }

            Assert.That(_service.Current.Body[BodyJoint.Nose], Is.EqualTo(new Vector3(0f, 0.62f, 0f)), "Across and up as observed, at the hips' depth.");
            Assert.That(recorded, Has.All.EqualTo(0.1f), "What the provider observed, for recordings.");

            _service.SetOptions(_service.Options.Value.WithBodyDepth(BodyDepth.Tracked));
            for (int i = 0; i < 20; i++)
            {
                Frame();
            }

            Assert.That(_service.Current.Body[BodyJoint.Nose].z, Is.GreaterThan(0.05f).And.LessThanOrEqualTo(0.1f), "Back in depth (3D), gliding there through the smoothing.");
        }

        [Test]
        public void TrackedImages_AreCountedPerSecond_AndMirrorIsOnByDefault()
        {
            _service = Create();
            _service.Start();
            for (int i = 0; i < 150; i++)
            {
                Frame();
            }

            // The replay has images at 30 per second, taken by 60 frames per second.
            Assert.That(_service.ObservationsPerSecond, Is.EqualTo(30).Within(2));
            Assert.That(_service.Options.Value.Mirror, Is.True);
        }

        [Test]
        public void ADisabledPart_IsReportedDisabled()
        {
            _service = Create();
            _service.SetOptions(_service.Options.Value.WithRegions(TrackingRegions.All & ~TrackingRegions.Face));
            _service.Start();
            for (int i = 0; i < 5; i++)
            {
                Frame();
            }

            Assert.That(_service.Current.Face.Status, Is.EqualTo(TrackingStatus.Disabled));
        }

        [TestCase(0, SmoothingLevel.Standard)]
        [TestCase(1, SmoothingLevel.Default)]
        [TestCase(2, SmoothingLevel.Responsive)]
        public void AFileWithThreePresets_ReadsAsTheirLevels_KeepingTheOtherValues(int preset, int level)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, TrackingSettings.FileName), $"{{ \"schemaVersion\": 1, \"smoothing\": {preset}, \"mirror\": false, \"running\": true, \"removeBackground\": false }}");

            ConfigLoadResult<TrackingSettings> loaded = TrackingService.CreateStore(_directory, new UtcSystemClock()).Load();

            Assert.That(loaded.Value.SmoothingLevel, Is.EqualTo(level));
            Assert.That(loaded.Value.Mirror, Is.False);
            Assert.That(loaded.Value.Running, Is.True);
        }

        [TestCase(5, SmoothingLevel.Standard)]
        [TestCase(3, SmoothingLevel.Standard)]
        [TestCase(1, 3)]
        [TestCase(7, SmoothingLevel.Responsive)]
        [TestCase(10, SmoothingLevel.Max)]
        public void AVersion2Level_MovesTwoStepsUp_AndItsStandardBecomesTheNewOne(int saved, int level)
        {
            // Version 2's standard 5 was the default nearly everyone had; the steadier 3 is the new standard.
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, TrackingSettings.FileName), $"{{ \"schemaVersion\": 2, \"smoothingLevel\": {saved}, \"mirror\": false }}");

            ConfigLoadResult<TrackingSettings> loaded = TrackingService.CreateStore(_directory, new UtcSystemClock()).Load();

            Assert.That(loaded.Value.SmoothingLevel, Is.EqualTo(level));
            Assert.That(loaded.Value.Mirror, Is.False);
        }

        [Test]
        public void Choices_AreSaved_AndRunningTrackingStartsAgain()
        {
            _service = Create();
            _service.SetOptions(_service.Options.Value
                .WithSmoothing(3)
                .WithMirror(false)
                .WithBodyDepth(BodyDepth.Flat)
                .WithRegions(TrackingRegions.Face | TrackingRegions.Body));
            _service.Start();
            _service.ShutdownAsync(CancellationToken.None).Wait();
            _service.Dispose();

            ConfigFileStore<TrackingSettings> store = TrackingService.CreateStore(_directory, new UtcSystemClock());
            TrackingSettings saved = store.Load().Value;
            Assert.That(saved.SmoothingLevel, Is.EqualTo(3));
            Assert.That(saved.Regions, Is.EqualTo(TrackingRegions.Face | TrackingRegions.Body));
            Assert.That(saved.Running, Is.True);
            Assert.That(saved.Mirror, Is.False);
            Assert.That(saved.BodyDepth, Is.EqualTo(BodyDepth.Flat));

            _camera = new FakeCamera();
            _service = Create(saved);
            Assert.That(_service.State.Value, Is.EqualTo(TrackingState.Running), "Started again at startup.");
            Assert.That(_service.Options.Value.Smoothing, Is.EqualTo(3));
            Assert.That(_service.Options.Value.Mirror, Is.False);
            Assert.That(_service.Options.Value.BodyDepth, Is.EqualTo(BodyDepth.Flat));
        }

        [Test]
        public void WithoutAProvider_StartingReportsAProblem_AndNothingBreaks()
        {
            _service = new TrackingService(null, _camera, _loop, _clock, new TrackingSettings(), null, null, null);
            _service.InitializeAsync(CancellationToken.None).Wait();

            _service.Start();
            Frame();

            Assert.That(_service.State.Value, Is.EqualTo(TrackingState.Error));
            Assert.That(_service.Problem.Value, Is.EqualTo(TrackingProblem.StartFailed));
        }

        private TrackingService Create(TrackingSettings settings = null)
        {
            ConfigFileStore<TrackingSettings> store = TrackingService.CreateStore(_directory, new UtcSystemClock());
            var saves = new ConfigSaveScheduler<TrackingSettings>(store, _clock, TimeSpan.FromSeconds(0.5), null);
            TrackingFrame[] frames = SyntheticMotion.RaiseAndWaveFrames(2, 30, 0f, seed: 0);
            var provider = new TrackingReplayProvider(frames, TrackingRegions.Body | TrackingRegions.Face, () => _clock.Seconds, loop: true);
            var service = new TrackingService(provider, _camera, _loop, _clock, settings ?? new TrackingSettings(), saves, null, null);
            service.InitializeAsync(CancellationToken.None).Wait();
            return service;
        }

        private void Frame()
        {
            _clock.Seconds += 1.0 / 60;
            _loop.Tick(null);
        }

        /// <summary>A clock driven by the test, in seconds with microsecond ticks.</summary>
        internal sealed class SecondsClock : IMonotonicClock
        {
            public double Seconds { get; set; } = 10.0;

            public long Frequency => 1_000_000;

            public long GetTimestamp()
            {
                return (long)(Seconds * Frequency);
            }

            public TimeSpan GetElapsed(long startTimestamp, long endTimestamp)
            {
                return TimeSpan.FromSeconds((endTimestamp - startTimestamp) / (double)Frequency);
            }
        }

        /// <summary>A camera that captures as soon as it is started.</summary>
        internal sealed class FakeCamera : IVideoCaptureSource
        {
            private readonly ObservableValue<CaptureState> _state = new ObservableValue<CaptureState>(CaptureState.Stopped);

            public bool Started => _state.Value != CaptureState.Stopped;

            public string Id => "camera";

            public CaptureSourceKind Kind => CaptureSourceKind.Camera;

            public IReadOnlyObservable<CaptureState> State => _state;

            public IReadOnlyObservable<CaptureProblem> Problem { get; } = new ObservableValue<CaptureProblem>(CaptureProblem.None);

            public IReadOnlyObservable<CaptureSourceInfo> Info { get; } = new ObservableValue<CaptureSourceInfo>(default);

            public IReadOnlyObservable<Texture> Texture { get; } = new ObservableValue<Texture>(null);

            public long FrameCount => 0;

            public long LastFrameTimestamp => 0;

            public IReadOnlyObservable<CaptureFlip> Flip { get; } = new ObservableValue<CaptureFlip>(CaptureFlip.None);

            public void SetFlip(CaptureFlip flip)
            {
            }

            public void Start()
            {
                _state.Set(CaptureState.Capturing);
            }

            public void Stop()
            {
                _state.Set(CaptureState.Stopped);
            }
        }
    }
}
