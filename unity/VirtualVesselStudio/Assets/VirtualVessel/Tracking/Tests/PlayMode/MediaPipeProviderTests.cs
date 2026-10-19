using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VirtualVessel.Capture;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.Tracking.MediaPipe;

namespace VirtualVessel.Tracking.Tests
{
    /// <summary>
    /// The MediaPipe provider end to end on still images instead of a camera: models load, images are
    /// read upright and inferred, results come back, and buffers are given back so it keeps running
    /// (tracking detailed design 9, 14). The images are the plugin's own test images, not copied into the
    /// repository. Compiled only when the plugin is installed.
    /// </summary>
    public sealed class MediaPipeProviderTests
    {
        private const double TimeoutSeconds = 20;

        [UnityTest]
        public IEnumerator AFace_IsFoundUpright_LookingAtTheCamera_AndResultsKeepComing()
        {
            var image = Resources.Load<Texture2D>("lenna");
            Assume.That(image, Is.Not.Null, "The plugin's test image lenna.png.");
            var result = new Result();
            yield return Run(image, TrackingRegions.Face | TrackingRegions.Body, result);

            TestContext.WriteLine($"{result.Observations} observations in {result.Seconds:F1} s; face seen {result.FaceSeen} times.");
            Assert.That(result.State, Is.EqualTo(TrackingState.Running));
            Assert.That(result.FaceSeen, Is.GreaterThan(0), "A face is found only in an upright image.");
            Assert.That(result.Observations, Is.GreaterThanOrEqualTo(20), "Buffers come back, so images keep being inferred.");
            Assert.That(Quaternion.Angle(result.Frame.Face.HeadRotation, Quaternion.identity), Is.LessThan(45f), "Looking roughly into the camera.");
            Assert.That(result.Frame.Face.Supports(FaceShape.EyeBlinkLeft), Is.True);
        }

        [UnityTest]
        public IEnumerator WithoutBackgroundRemoval_TheFaceIsFoundToo()
        {
            var image = Resources.Load<Texture2D>("lenna");
            Assume.That(image, Is.Not.Null);
            var result = new Result();
            yield return Run(image, TrackingRegions.Face | TrackingRegions.Body, result, removeBackground: false);

            Assert.That(result.FaceSeen, Is.GreaterThan(0));
            Assert.That(result.Observations, Is.GreaterThanOrEqualTo(20));
        }

        [UnityTest]
        public IEnumerator AHand_IsFound()
        {
            var image = Resources.Load<Texture2D>("thumb-up");
            Assume.That(image, Is.Not.Null, "The plugin's test image thumb-up.jpg.");
            var result = new Result();
            yield return Run(image, TrackingRegions.Hands, result);

            TestContext.WriteLine($"{result.Observations} observations; hand seen {result.HandSeen} times.");
            Assert.That(result.HandSeen, Is.GreaterThan(0));
        }

        /// <summary>
        /// How fast all three tasks keep up on this PC, and how old an observation is when it is taken
        /// (tracking detailed design 15). Numbers depend on the machine, so it runs only by name.
        /// </summary>
        [UnityTest]
        [Explicit("Measures this PC; run it by name.")]
        [Category("Explicit")]
        public IEnumerator AllTasks_Timing([Values(true, false)] bool removeBackground)
        {
            var image = Resources.Load<Texture2D>("lenna");
            Assume.That(image, Is.Not.Null);
            var clock = new StopwatchMonotonicClock();
            var camera = new StillCamera(image, clock);
            string models = Path.Combine(UnityEngine.Application.streamingAssetsPath, MediaPipeTracking.ModelFolder);
            ITrackingProvider provider = MediaPipeTracking.Create(camera, models, PoseModel.Full, clock, null, null);
            var frame = new TrackingFrame();
            var watch = Stopwatch.StartNew();
            double firstAt = -1;
            int observations = 0;
            double age = 0;
            double oldest = 0;
            provider.Start(TrackingOptions.Default.WithRemoveBackground(removeBackground));
            try
            {
                // 30 camera frames per second, as the tracking camera delivers.
                double nextFrame = 0;
                while (watch.Elapsed.TotalSeconds < 15 && (firstAt < 0 || watch.Elapsed.TotalSeconds - firstAt < 5))
                {
                    if (watch.Elapsed.TotalSeconds >= nextFrame)
                    {
                        camera.NextFrame();
                        nextFrame += 1.0 / 30;
                    }

                    provider.Tick();
                    if (provider.TryTakeLatest(frame))
                    {
                        if (firstAt < 0)
                        {
                            firstAt = watch.Elapsed.TotalSeconds;
                        }

                        double now = clock.GetTimestamp() / (double)clock.Frequency;
                        double newest = TrackingService.Newest(frame);
                        observations++;
                        age += now - newest;
                        oldest = Math.Max(oldest, now - newest);
                    }

                    yield return null;
                }
            }
            finally
            {
                provider.Dispose();
            }

            Assert.That(observations, Is.GreaterThan(0));
            double seconds = watch.Elapsed.TotalSeconds - firstAt;
            TestContext.WriteLine($"Face, Pose (Full), Hand{(removeBackground ? " after background removal" : string.Empty)} on {image.width}×{image.height} at 30 fps: first result after {firstAt:F2} s, " +
                $"{observations / seconds:F1} observations/s, age {age / observations * 1000:F0} ms on average, {oldest * 1000:F0} ms at most.");
        }

        private static IEnumerator Run(Texture2D image, TrackingRegions regions, Result result, bool removeBackground = true)
        {
            var clock = new StopwatchMonotonicClock();
            var camera = new StillCamera(image, clock);
            string models = Path.Combine(UnityEngine.Application.streamingAssetsPath, MediaPipeTracking.ModelFolder);
            Assume.That(Directory.Exists(models), "Run tools/deps/fetch-mediapipe.ps1.");
            ITrackingProvider provider = MediaPipeTracking.Create(camera, models, PoseModel.Full, clock, null, null);
            var frame = new TrackingFrame();
            var watch = Stopwatch.StartNew();
            provider.Start(TrackingOptions.Default.WithRegions(regions).WithRemoveBackground(removeBackground));
            try
            {
                while (watch.Elapsed.TotalSeconds < TimeoutSeconds && result.Observations < 30)
                {
                    camera.NextFrame();
                    provider.Tick();
                    if (provider.TryTakeLatest(frame))
                    {
                        result.Observations++;
                        result.FaceSeen += frame.Face.IsUsable ? 1 : 0;
                        result.HandSeen += frame.LeftHand.IsUsable || frame.RightHand.IsUsable ? 1 : 0;
                    }

                    if (provider.State.Value == TrackingState.Error)
                    {
                        break;
                    }

                    yield return null;
                }

                result.State = provider.State.Value;
                result.Frame = frame;
                result.Seconds = watch.Elapsed.TotalSeconds;
            }
            finally
            {
                provider.Dispose();
            }
        }

        private sealed class Result
        {
            public int Observations { get; set; }

            public int FaceSeen { get; set; }

            public int HandSeen { get; set; }

            public TrackingState State { get; set; }

            public TrackingFrame Frame { get; set; }

            public double Seconds { get; set; }
        }

        /// <summary>A camera that shows the same image as a new frame every time it is asked.</summary>
        private sealed class StillCamera : IVideoCaptureSource
        {
            private readonly IMonotonicClock _clock;

            public StillCamera(Texture texture, IMonotonicClock clock)
            {
                _clock = clock;
                Texture = new ObservableValue<Texture>(texture);
            }

            public string Id => CaptureService.TrackingCameraId;

            public CaptureSourceKind Kind => CaptureSourceKind.Camera;

            public IReadOnlyObservable<CaptureState> State { get; } = new ObservableValue<CaptureState>(CaptureState.Capturing);

            public IReadOnlyObservable<CaptureProblem> Problem { get; } = new ObservableValue<CaptureProblem>(CaptureProblem.None);

            public IReadOnlyObservable<CaptureSourceInfo> Info { get; } = new ObservableValue<CaptureSourceInfo>(default);

            public IReadOnlyObservable<Texture> Texture { get; }

            public long FrameCount { get; private set; }

            public long LastFrameTimestamp { get; private set; }

            public IReadOnlyObservable<CaptureFlip> Flip { get; } = new ObservableValue<CaptureFlip>(CaptureFlip.None);

            public void NextFrame()
            {
                FrameCount++;
                LastFrameTimestamp = _clock.GetTimestamp();
            }

            public void SetFlip(CaptureFlip flip)
            {
            }

            public void Start()
            {
            }

            public void Stop()
            {
            }
        }
    }
}
