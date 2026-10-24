using System;
using System.Threading;
using System.Threading.Tasks;
using Mediapipe;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using UnityEngine;
using VirtualVessel.Capture;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.Tracking.Threading;
using MediaPipeImage = Mediapipe.Image;

namespace VirtualVessel.Tracking.MediaPipe
{
    /// <summary>
    /// Camera tracking with MediaPipe's Face, Pose, and Hand Landmarkers (tracking detailed design 5.2,
    /// 9, 16): reads each new camera image once, gives it to the enabled tasks, and hands their
    /// converted results to the main thread.
    /// </summary>
    /// <remarks>
    /// <see cref="MediaPipeTasks"/> and the result readers are the only code that uses the plugin's
    /// task API; this class owns the flow. On Windows the models run on the CPU, on MediaPipe's threads.
    /// A new image is read only when the last read is done and a buffer is free, so images never queue
    /// up and add lag. Results arrive on MediaPipe's threads, are converted there by
    /// <see cref="MediaPipeConversion"/>, and are handed over through <see cref="FrameSlot{T}"/>.
    /// Everything else runs on the main thread.
    /// </remarks>
    internal sealed class MediaPipeTrackingProvider : ITrackingProvider
    {
        /// <summary>Camera images handed to MediaPipe at once; more would only wait in its queue.</summary>
        private const int MaxImagesInFlight = 2;

        private static readonly TimeSpan s_stuckAfter = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan s_failureReportInterval = TimeSpan.FromSeconds(10);

        private readonly IVideoCaptureSource _camera;
        private readonly string _modelDirectory;
        private readonly PoseModel _poseModel;
        private readonly IMonotonicClock _clock;
        private readonly ILog _log;
        private readonly MediaPipeCallbacks _callbacks;
        private readonly ObservableValue<TrackingState> _state = new ObservableValue<TrackingState>(TrackingState.Stopped);
        private readonly FrameSlot<BodyPose> _bodySlot = new FrameSlot<BodyPose>(() => new BodyPose());
        private readonly FrameSlot<FacesObservation> _faceSlot = new FrameSlot<FacesObservation>(() => new FacesObservation());
        private readonly FrameSlot<HandsObservation> _handsSlot = new FrameSlot<HandsObservation>(() => new HandsObservation());
        private readonly PoseResultReader _pose = new PoseResultReader();
        private readonly FaceResultReader _face;
        private readonly HandResultReader _hands = new HandResultReader();
        private readonly TaskTiming _faceTiming;
        private readonly TaskTiming _bodyTiming;
        private readonly TaskTiming _handsTiming;
        private readonly PerfTimer _readback;
        private readonly PerfCounter _dropped;

        private ImageBufferPool _buffers;
        private CameraFrameReader _reader;
        private Task<MediaPipeTasks> _loading;
        private MediaPipeTasks _tasks;
        private TrackingRegions _regions;
        private long _lastFrame = -1;
        private long _lastMilliseconds;
        private long _sequence;
        private int _callbackFailures;
        private int _reportedFailures;
        private long _reportedFailuresAt;
        private bool _reportedStuck;

        /// <param name="modelDirectory">The folder with the model files (StreamingAssets/Tracking/MediaPipe).</param>
        /// <param name="faceSidesMirrored">Whether MediaPipe's left and right face values are the image's instead of the person's (checked with a camera).</param>
        public MediaPipeTrackingProvider(IVideoCaptureSource camera, string modelDirectory, PoseModel poseModel, bool faceSidesMirrored, IMonotonicClock clock, IPerformanceMetrics metrics, ILog log)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _modelDirectory = modelDirectory ?? throw new ArgumentNullException(nameof(modelDirectory));
            _poseModel = poseModel;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log;
            _face = new FaceResultReader(faceSidesMirrored);
            _callbacks = new MediaPipeCallbacks(OnFace, OnPose, OnHands);
            _faceTiming = new TaskTiming(clock, metrics?.Timer("Tracking", "face.Inference"));
            _bodyTiming = new TaskTiming(clock, metrics?.Timer("Tracking", "body.Inference"));
            _handsTiming = new TaskTiming(clock, metrics?.Timer("Tracking", "hands.Inference"));
            _readback = metrics?.Timer("Tracking", "Readback");
            _dropped = metrics?.Counter("Tracking", "Dropped");
        }

        /// <summary>One camera's image: depth along its axis is estimated, and weakest for limbs pointing at it.</summary>
        public ObservationTraits Traits => ObservationTraits.MonocularDepth;

        public TrackingRegions Supported => TrackingRegions.All;

        public IReadOnlyObservable<TrackingState> State => _state;

        public void Start(TrackingOptions options)
        {
            SetOptions(options);
            if (_state.Value == TrackingState.Starting || _state.Value == TrackingState.Running)
            {
                return;
            }

            _state.Set(TrackingState.Starting);
            _buffers = new ImageBufferPool();
            _reader = new CameraFrameReader(OnImageRead);
            _lastFrame = -1;
            _reportedStuck = false;

            // Reading the models and building the graphs takes seconds; not on the main thread (system design 15.7).
            string directory = _modelDirectory;
            PoseModel poseModel = _poseModel;
            MediaPipeCallbacks callbacks = _callbacks;
            _loading = Task.Run(() => MediaPipeTasks.Create(directory, poseModel, callbacks));
        }

        public void SetOptions(TrackingOptions options)
        {
            // All tasks run once started; a disabled one is simply not given images.
            _regions = options.Regions;
        }

        public void Tick()
        {
            if (_loading != null && _loading.IsCompleted)
            {
                FinishLoading();
            }

            ReportCallbackFailures();
            if (_state.Value != TrackingState.Running)
            {
                return;
            }

            ReportStuckBuffers();
            ReadNextImage();
        }

        public bool TryTakeLatest(TrackingFrame into)
        {
            bool any = false;
            if (_bodySlot.TryTake(out BodyPose body))
            {
                into.Body.CopyFrom(body);
                _bodyTiming.Report();
                any = true;
            }

            // Body first: the faces and hands are matched to it.
            if (_faceSlot.TryTake(out FacesObservation faces))
            {
                faces.AssignTo(into);
                _faceTiming.Report();
                any = true;
            }

            if (_handsSlot.TryTake(out HandsObservation hands))
            {
                hands.AssignTo(into);
                _handsTiming.Report();
                any = true;
            }

            if (any)
            {
                into.Sequence = ++_sequence;
                into.Time = _clock.GetTimestamp() / (double)_clock.Frequency;
            }

            return any;
        }

        public void Stop()
        {
            if (_state.Value == TrackingState.Stopped)
            {
                return;
            }

            _state.Set(TrackingState.Stopped);
            if (_loading != null && !_loading.IsCompleted)
            {
                // Closed when it finishes; graphs cannot be stopped while being built.
                _loading.ContinueWith(task =>
                {
                    if (task.Status == TaskStatus.RanToCompletion)
                    {
                        task.Result.Dispose();
                    }
                }, TaskScheduler.Default);
            }

            _loading = null;
            MediaPipeTasks tasks = _tasks;
            _tasks = null;
            tasks?.Dispose();
            _reader?.Dispose();
            _reader = null;
            int leaked = _buffers?.FreeAll() ?? 0;
            if (leaked > 0)
            {
                _log?.Warning($"{leaked} camera image buffers were still held by MediaPipe when tracking stopped and were left allocated.");
            }

            _buffers = null;
        }

        public void Dispose()
        {
            Stop();
        }

        private void FinishLoading()
        {
            Task<MediaPipeTasks> loading = _loading;
            _loading = null;
            if (loading.Status == TaskStatus.RanToCompletion)
            {
                _tasks = loading.Result;
                _state.Set(TrackingState.Running);
                _log?.Information($"MediaPipe tasks ready: Face, Pose ({_poseModel}), Hand.");
                return;
            }

            _log?.Error("MediaPipe tasks could not start. If the plugin or its models are missing, run tools/deps/fetch-mediapipe.ps1.", loading.Exception?.GetBaseException());
            _state.Set(TrackingState.Error);
        }

        private void ReadNextImage()
        {
            Texture texture = _camera.Texture.Value;
            long frame = _camera.FrameCount;
            if (texture == null || frame == _lastFrame || _reader.Busy)
            {
                return;
            }

            _lastFrame = frame;
            (int width, int height) = CameraFrameReader.Size(texture.width, texture.height);
            ImageBuffer buffer = _buffers.InUse < MaxImagesInFlight ? _buffers.Take(width * height * 4) : null;
            if (buffer == null)
            {
                // MediaPipe is still busy with earlier images; this one is skipped, not queued.
                _dropped?.Increment();
                return;
            }

            double imageTime = _camera.LastFrameTimestamp / (double)_clock.Frequency;
            _reader.Read(texture, buffer, width, height, imageTime);
        }

        private void OnImageRead(ImageBuffer buffer, int width, int height, double imageTime, double readbackSeconds)
        {
            _readback?.Record(TimeSpan.FromSeconds(readbackSeconds));

            // Each task needs its timestamps to grow: the image time in milliseconds, kept increasing.
            long milliseconds = Math.Max(_lastMilliseconds + 1, (long)(imageTime * 1000));
            _lastMilliseconds = milliseconds;
            Detect(buffer, width, height, milliseconds);
        }

        /// <summary>Gives the image in <paramref name="buffer"/> to the enabled landmark tasks.</summary>
        private void Detect(ImageBuffer buffer, int width, int height, long milliseconds)
        {
            MediaPipeTasks tasks = _tasks;
            if (tasks == null)
            {
                buffer.Cancel();
                return;
            }

            TrackingRegions regions = _regions;
            bool face = (regions & TrackingRegions.Face) != 0;
            bool body = (regions & TrackingRegions.Body) != 0;
            bool hands = (regions & TrackingRegions.Hands) != 0;
            int images = (face ? 1 : 0) + (body ? 1 : 0) + (hands ? 1 : 0);
            if (images == 0)
            {
                buffer.Cancel();
                return;
            }

            buffer.Hold(images, _clock.GetTimestamp());
            if (face)
            {
                Detect(tasks, TrackingRegions.Face, _faceTiming, buffer, width, height, milliseconds);
            }

            if (body)
            {
                Detect(tasks, TrackingRegions.Body, _bodyTiming, buffer, width, height, milliseconds);
            }

            if (hands)
            {
                Detect(tasks, TrackingRegions.Hands, _handsTiming, buffer, width, height, milliseconds);
            }
        }

        private void Detect(MediaPipeTasks tasks, TrackingRegions part, TaskTiming timing, ImageBuffer buffer, int width, int height, long milliseconds)
        {
            // The image takes over the buffer without copying; the task moves it into a packet. When
            // MediaPipe releases it, the pool's deleter gives the buffer back.
            var image = new MediaPipeImage(ImageFormat.Types.Format.Srgba, width, height, width * 4, buffer.Data, ImageBufferPool.Deleter);
            try
            {
                timing.Submitted();
                tasks.Detect(part, image, milliseconds);
            }
            catch (Exception exception)
            {
                if (!image.isDisposed)
                {
                    image.Dispose();
                }

                CountFailure();
                _log?.Error($"MediaPipe did not take a camera image ({part}).", exception);
            }
        }

        // The callbacks below run on MediaPipe's threads: no Unity API, no waiting.
        private void OnPose(PoseLandmarkerResult result, MediaPipeImage image, long milliseconds)
        {
            try
            {
                BodyPose body = _bodySlot.Writing;
                double time = milliseconds / 1000.0;
                if (_pose.TryRead(result))
                {
                    MediaPipeConversion.WriteBody(_pose.World, _pose.Image, _pose.Visibility, time, body);
                }
                else
                {
                    MarkLost(body, time);
                }

                _bodyTiming.Completed();
                _bodySlot.Publish();
            }
            catch (Exception)
            {
                CountFailure();
            }
        }

        private void OnFace(FaceLandmarkerResult result, MediaPipeImage image, long milliseconds)
        {
            try
            {
                _face.Read(result, milliseconds / 1000.0, _faceSlot.Writing);
                _faceTiming.Completed();
                _faceSlot.Publish();
            }
            catch (Exception)
            {
                CountFailure();
            }
        }

        private void OnHands(HandLandmarkerResult result, MediaPipeImage image, long milliseconds)
        {
            try
            {
                _hands.Read(result, milliseconds / 1000.0, _handsSlot.Writing);
                _handsTiming.Completed();
                _handsSlot.Publish();
            }
            catch (Exception)
            {
                CountFailure();
            }
        }

        private static void MarkLost(TrackedPart part, double time)
        {
            part.Status = TrackingStatus.Lost;
            part.Timestamp = time;
        }

        private void CountFailure()
        {
            // Counted here and logged on the main thread (ReportCallbackFailures), not per result.
            Interlocked.Increment(ref _callbackFailures);
        }

        private void ReportCallbackFailures()
        {
            int failures = Volatile.Read(ref _callbackFailures);
            long now = _clock.GetTimestamp();
            if (failures == _reportedFailures || now - _reportedFailuresAt < s_failureReportInterval.TotalSeconds * _clock.Frequency)
            {
                return;
            }

            // Gathered and at most every 10 s, not once per frame (CLAUDE.md 20).
            _log?.Warning($"{failures - _reportedFailures} MediaPipe images or results failed ({failures} in total).");
            _reportedFailures = failures;
            _reportedFailuresAt = now;
        }

        private void ReportStuckBuffers()
        {
            if (_reportedStuck || _buffers.LongestHeld(_clock.GetTimestamp()) <= s_stuckAfter.TotalSeconds * _clock.Frequency)
            {
                return;
            }

            // MediaPipe should release each image soon after its result. If it does not, tracking
            // waits here instead of reusing memory MediaPipe may still read.
            _reportedStuck = true;
            _log?.Warning("MediaPipe has held a camera image for more than 2 s; tracking waits for it.");
        }

        /// <summary>How long one task takes from receiving an image to its result, for the metrics.</summary>
        private sealed class TaskTiming
        {
            private readonly IMonotonicClock _clock;
            private readonly PerfTimer _timer;
            private long _submittedAt;
            private long _lastTicks;

            public TaskTiming(IMonotonicClock clock, PerfTimer timer)
            {
                _clock = clock;
                _timer = timer;
            }

            /// <summary>Main thread, when an image is given to the task.</summary>
            public void Submitted()
            {
                Volatile.Write(ref _submittedAt, _clock.GetTimestamp());
            }

            /// <summary>MediaPipe's thread, when the result arrives.</summary>
            public void Completed()
            {
                Volatile.Write(ref _lastTicks, _clock.GetTimestamp() - Volatile.Read(ref _submittedAt));
            }

            /// <summary>Main thread, when the result is taken: records the last duration.</summary>
            public void Report()
            {
                _timer?.Record(_clock.GetElapsed(0, Volatile.Read(ref _lastTicks)));
            }
        }
    }
}
