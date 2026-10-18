using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mediapipe;
using Mediapipe.Tasks.Components.Containers;
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
using BaseOptions = Mediapipe.Tasks.Core.BaseOptions;
using Landmark = Mediapipe.Tasks.Components.Containers.Landmark;
using MediaPipeImage = Mediapipe.Image;
using NormalizedLandmark = Mediapipe.Tasks.Components.Containers.NormalizedLandmark;
using RunningMode = Mediapipe.Tasks.Vision.Core.RunningMode;

namespace VirtualVessel.Tracking.MediaPipe
{
    /// <summary>
    /// Camera tracking with MediaPipe's Face, Pose, and Hand Landmarkers through MediaPipeUnityPlugin
    /// (tracking detailed design 5.2, 9, 16). The only class that calls the plugin's tasks: when its
    /// API changes, this is what changes. Results are converted by <see cref="MediaPipeConversion"/>.
    /// </summary>
    /// <remarks>
    /// On Windows the models run on the CPU, on MediaPipe's threads. Each camera image is read back
    /// once and given to the enabled tasks in LIVE_STREAM mode; a new image is read only when the
    /// last read is done and a buffer is free, so images never queue up and add lag. Results arrive
    /// on MediaPipe's threads, are converted there, and are handed over through
    /// <see cref="FrameSlot{T}"/>. Everything else runs on the main thread.
    /// </remarks>
    internal sealed class MediaPipeTrackingProvider : ITrackingProvider
    {
        public const string ProviderName = "mediapipe";

        public const string FaceModel = "face_landmarker_v2_with_blendshapes.bytes";
        public const string HandModel = "hand_landmarker.bytes";

        /// <summary>Images handed to MediaPipe at once; more would only wait in its queue.</summary>
        private const int MaxImagesInFlight = 2;

        private static readonly TimeSpan s_stuckAfter = TimeSpan.FromSeconds(2);

        private readonly IVideoCaptureSource _camera;
        private readonly string _modelDirectory;
        private readonly PoseModel _poseModel;
        private readonly bool _faceSidesMirrored;
        private readonly IMonotonicClock _clock;
        private readonly ILog _log;
        private readonly ObservableValue<TrackingState> _state = new ObservableValue<TrackingState>(TrackingState.Stopped);
        private readonly FrameSlot<BodyPose> _bodySlot = new FrameSlot<BodyPose>(() => new BodyPose());
        private readonly FrameSlot<FacePose> _faceSlot = new FrameSlot<FacePose>(() => new FacePose());
        private readonly FrameSlot<HandsObservation> _handsSlot = new FrameSlot<HandsObservation>(() => new HandsObservation());
        private readonly PerfTimer _readback;
        private readonly PerfTimer _faceInference;
        private readonly PerfTimer _bodyInference;
        private readonly PerfTimer _handsInference;
        private readonly PerfCounter _dropped;

        // Scratch for converting results; each is used by one task's callbacks only.
        private readonly PoseScratch _pose = new PoseScratch();
        private readonly FaceScratch _face = new FaceScratch();
        private readonly HandScratch _hands = new HandScratch();

        private ImageBufferPool _buffers;
        private CameraFrameReader _reader;
        private Task<Tasks> _loading;
        private Tasks _tasks;
        private TrackingRegions _regions;
        private long _lastFrame = -1;
        private long _lastMilliseconds;
        private long _sequence;
        private int _callbackFailures;
        private int _reportedFailures;
        private bool _reportedStuck;

        /// <param name="modelDirectory">The folder with the model files (StreamingAssets/Tracking/MediaPipe).</param>
        /// <param name="faceSidesMirrored">Whether MediaPipe's left and right face values are the image's instead of the person's (checked with a camera).</param>
        public MediaPipeTrackingProvider(IVideoCaptureSource camera, string modelDirectory, PoseModel poseModel, bool faceSidesMirrored, IMonotonicClock clock, IPerformanceMetrics metrics, ILog log)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _modelDirectory = modelDirectory ?? throw new ArgumentNullException(nameof(modelDirectory));
            _poseModel = poseModel;
            _faceSidesMirrored = faceSidesMirrored;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log;
            _readback = metrics?.Timer("Tracking", "Readback");
            _faceInference = metrics?.Timer("Tracking", "face.Inference");
            _bodyInference = metrics?.Timer("Tracking", "body.Inference");
            _handsInference = metrics?.Timer("Tracking", "hands.Inference");
            _dropped = metrics?.Counter("Tracking", "Dropped");
        }

        public TrackingRegions Supported => TrackingRegions.All;

        public IReadOnlyObservable<TrackingState> State => _state;

        /// <summary>The Pose model file for <paramref name="model"/>. Pure.</summary>
        public static string PoseModelFile(PoseModel model)
        {
            return model switch
            {
                PoseModel.Lite => "pose_landmarker_lite.bytes",
                PoseModel.Heavy => "pose_landmarker_heavy.bytes",
                _ => "pose_landmarker_full.bytes",
            };
        }

        public void Start(TrackingRegions regions)
        {
            _regions = regions;
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
            _loading = Task.Run(() => CreateTasks(directory, poseModel));
        }

        public void SetRegions(TrackingRegions regions)
        {
            // All three tasks run once started; a disabled one is simply not given images.
            _regions = regions;
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

            long now = _clock.GetTimestamp();
            if (!_reportedStuck && _buffers.LongestHeld(now) > s_stuckAfter.TotalSeconds * _clock.Frequency)
            {
                // MediaPipe should release each image soon after its result. If it does not, tracking
                // stops here instead of reusing memory MediaPipe may still read.
                _reportedStuck = true;
                _log?.Warning("MediaPipe has held a camera image for more than 2 s; tracking waits for it.");
            }

            ReadNextImage();
        }

        public bool TryTakeLatest(TrackingFrame into)
        {
            bool any = false;
            if (_bodySlot.TryTake(out BodyPose body))
            {
                into.Body.CopyFrom(body);
                _bodyInference?.Record(TimeSpan.FromSeconds(_pose.LastSeconds));
                any = true;
            }

            if (_faceSlot.TryTake(out FacePose face))
            {
                into.Face.CopyFrom(face);
                _faceInference?.Record(TimeSpan.FromSeconds(_face.LastSeconds));
                any = true;
            }

            if (_handsSlot.TryTake(out HandsObservation hands))
            {
                hands.AssignTo(into);
                _handsInference?.Record(TimeSpan.FromSeconds(_hands.LastSeconds));
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
                Task<Tasks> loading = _loading;
                loading.ContinueWith(task =>
                {
                    if (task.Status == TaskStatus.RanToCompletion)
                    {
                        task.Result.Close();
                    }
                }, TaskScheduler.Default);
            }

            _loading = null;
            _tasks?.Close();
            _tasks = null;
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

        private Tasks CreateTasks(string directory, PoseModel poseModel)
        {
            // Models are given as bytes so the plugin's own asset loading is not needed.
            byte[] face = File.ReadAllBytes(Path.Combine(directory, FaceModel));
            byte[] pose = File.ReadAllBytes(Path.Combine(directory, PoseModelFile(poseModel)));
            byte[] hand = File.ReadAllBytes(Path.Combine(directory, HandModel));

            var tasks = new Tasks();
            try
            {
                tasks.Face = FaceLandmarker.CreateFromOptions(new FaceLandmarkerOptions(
                    new BaseOptions(BaseOptions.Delegate.CPU, modelAssetBuffer: face),
                    RunningMode.LIVE_STREAM,
                    numFaces: 1,
                    outputFaceBlendshapes: true,
                    outputFaceTransformationMatrixes: true,
                    resultCallback: OnFace));
                tasks.Pose = PoseLandmarker.CreateFromOptions(new PoseLandmarkerOptions(
                    new BaseOptions(BaseOptions.Delegate.CPU, modelAssetBuffer: pose),
                    RunningMode.LIVE_STREAM,
                    numPoses: 1,
                    resultCallback: OnPose));
                tasks.Hand = HandLandmarker.CreateFromOptions(new HandLandmarkerOptions(
                    new BaseOptions(BaseOptions.Delegate.CPU, modelAssetBuffer: hand),
                    RunningMode.LIVE_STREAM,
                    numHands: 2,
                    resultCallback: OnHands));
                return tasks;
            }
            catch
            {
                tasks.Close();
                throw;
            }
        }

        private void FinishLoading()
        {
            Task<Tasks> loading = _loading;
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

            (int width, int height) = CameraFrameReader.Size(texture.width, texture.height);
            ImageBuffer buffer = _buffers.Held < MaxImagesInFlight ? _buffers.TakeFree(width * height * 4) : null;
            if (buffer == null)
            {
                // MediaPipe is still busy with earlier images; this one is skipped, not queued.
                _dropped?.Increment();
                _lastFrame = frame;
                return;
            }

            _lastFrame = frame;
            double imageTime = _camera.LastFrameTimestamp / (double)_clock.Frequency;
            _reader.Read(texture, buffer, width, height, imageTime);
        }

        private void OnImageRead(ImageBuffer buffer, int width, int height, double imageTime, double readbackSeconds)
        {
            _readback?.Record(TimeSpan.FromSeconds(readbackSeconds));
            Tasks tasks = _tasks;
            if (tasks == null)
            {
                return;
            }

            // Each task needs its own timestamp to grow; the image time in milliseconds, kept increasing.
            long milliseconds = Math.Max(_lastMilliseconds + 1, (long)(imageTime * 1000));
            _lastMilliseconds = milliseconds;

            TrackingRegions regions = _regions;
            bool face = (regions & TrackingRegions.Face) != 0;
            bool body = (regions & TrackingRegions.Body) != 0;
            bool hands = (regions & TrackingRegions.Hands) != 0;
            int images = (face ? 1 : 0) + (body ? 1 : 0) + (hands ? 1 : 0);
            if (images == 0)
            {
                return;
            }

            buffer.Hold(images);
            buffer.HeldSince = _clock.GetTimestamp();
            long submitted = _clock.GetTimestamp();
            if (face)
            {
                _face.SubmittedAt = submitted;
                Detect(tasks, TrackingRegions.Face, buffer, width, height, milliseconds);
            }

            if (body)
            {
                _pose.SubmittedAt = submitted;
                Detect(tasks, TrackingRegions.Body, buffer, width, height, milliseconds);
            }

            if (hands)
            {
                _hands.SubmittedAt = submitted;
                Detect(tasks, TrackingRegions.Hands, buffer, width, height, milliseconds);
            }
        }

        private void Detect(Tasks tasks, TrackingRegions task, ImageBuffer buffer, int width, int height, long milliseconds)
        {
            // An image takes over the buffer without copying; DetectAsync moves it into a packet. When
            // MediaPipe releases it, the pool's deleter gives the buffer back.
            var image = new MediaPipeImage(ImageFormat.Types.Format.Srgba, width, height, width * 4, buffer.Data, ImageBufferPool.Deleter);
            try
            {
                switch (task)
                {
                    case TrackingRegions.Face:
                        tasks.Face.DetectAsync(image, milliseconds);
                        break;
                    case TrackingRegions.Body:
                        tasks.Pose.DetectAsync(image, milliseconds);
                        break;
                    default:
                        tasks.Hand.DetectAsync(image, milliseconds);
                        break;
                }
            }
            catch (Exception exception)
            {
                if (!image.isDisposed)
                {
                    image.Dispose();
                }

                Interlocked.Increment(ref _callbackFailures);
                _log?.Error("MediaPipe did not take a camera image.", exception);
            }
        }

        // The callbacks below run on MediaPipe's threads: no Unity API, no waiting.
        private void OnPose(PoseLandmarkerResult result, MediaPipeImage image, long milliseconds)
        {
            try
            {
                BodyPose body = _bodySlot.Writing;
                double time = milliseconds / 1000.0;
                if (!_pose.TryRead(result))
                {
                    body.Status = TrackingStatus.Lost;
                    body.Timestamp = time;
                }
                else
                {
                    MediaPipeConversion.WriteBody(_pose.World, _pose.Image, _pose.Visibility, time, body);
                }

                _pose.LastSeconds = Seconds(_pose.SubmittedAt);
                _bodySlot.Publish();
            }
            catch (Exception)
            {
                Interlocked.Increment(ref _callbackFailures);
            }
        }

        private void OnFace(FaceLandmarkerResult result, MediaPipeImage image, long milliseconds)
        {
            try
            {
                FacePose face = _faceSlot.Writing;
                double time = milliseconds / 1000.0;
                if (!_face.TryRead(result, _faceSidesMirrored))
                {
                    face.Status = TrackingStatus.Lost;
                    face.Timestamp = time;
                }
                else
                {
                    MediaPipeConversion.WriteFace(_face.Shapes, _face.Supported, _face.Matrix, time, face);
                }

                _face.LastSeconds = Seconds(_face.SubmittedAt);
                _faceSlot.Publish();
            }
            catch (Exception)
            {
                Interlocked.Increment(ref _callbackFailures);
            }
        }

        private void OnHands(HandLandmarkerResult result, MediaPipeImage image, long milliseconds)
        {
            try
            {
                _hands.Read(result, milliseconds / 1000.0, _handsSlot.Writing);
                _hands.LastSeconds = Seconds(_hands.SubmittedAt);
                _handsSlot.Publish();
            }
            catch (Exception)
            {
                Interlocked.Increment(ref _callbackFailures);
            }
        }

        private double Seconds(long since)
        {
            return (_clock.GetTimestamp() - since) / (double)_clock.Frequency;
        }

        private void ReportCallbackFailures()
        {
            int failures = Volatile.Read(ref _callbackFailures);
            if (failures != _reportedFailures)
            {
                // Logged on the main thread and once per change, not once per frame (CLAUDE.md 20).
                _log?.Warning($"{failures - _reportedFailures} MediaPipe results could not be converted ({failures} in total).");
                _reportedFailures = failures;
            }
        }

        /// <summary>The three tasks, created together and closed together.</summary>
        private sealed class Tasks
        {
            public FaceLandmarker Face { get; set; }

            public PoseLandmarker Pose { get; set; }

            public HandLandmarker Hand { get; set; }

            public void Close()
            {
                // Closing waits for the graphs to finish, which releases their images.
                Face?.Close();
                Pose?.Close();
                Hand?.Close();
            }
        }

        /// <summary>Pose results copied out of the plugin's types, for the conversion.</summary>
        private sealed class PoseScratch
        {
            public Vector3[] World { get; } = new Vector3[MediaPipeConversion.PoseLandmarkCount];

            public Vector2[] Image { get; } = new Vector2[MediaPipeConversion.PoseLandmarkCount];

            public float[] Visibility { get; } = new float[MediaPipeConversion.PoseLandmarkCount];

            public long SubmittedAt { get; set; }

            public double LastSeconds { get; set; }

            public bool TryRead(PoseLandmarkerResult result)
            {
                if (result.poseLandmarks == null || result.poseLandmarks.Count == 0 || result.poseWorldLandmarks == null || result.poseWorldLandmarks.Count == 0)
                {
                    return false;
                }

                List<NormalizedLandmark> image = result.poseLandmarks[0].landmarks;
                List<Landmark> world = result.poseWorldLandmarks[0].landmarks;
                if (image == null || world == null || image.Count < MediaPipeConversion.PoseLandmarkCount || world.Count < MediaPipeConversion.PoseLandmarkCount)
                {
                    return false;
                }

                for (int i = 0; i < MediaPipeConversion.PoseLandmarkCount; i++)
                {
                    World[i] = new Vector3(world[i].x, world[i].y, world[i].z);
                    Image[i] = new Vector2(image[i].x, image[i].y);
                    Visibility[i] = image[i].visibility ?? 1f;
                }

                return true;
            }
        }

        /// <summary>Face results copied out of the plugin's types, with the blendshape names resolved once.</summary>
        private sealed class FaceScratch
        {
            private int[] _shapeOf;

            public float[] Shapes { get; } = new float[FacePose.ShapeCount];

            public ulong Supported { get; private set; }

            public Matrix4x4 Matrix { get; private set; }

            public long SubmittedAt { get; set; }

            public double LastSeconds { get; set; }

            public bool TryRead(FaceLandmarkerResult result, bool sidesMirrored)
            {
                if (result.faceBlendshapes == null || result.faceBlendshapes.Count == 0 || result.facialTransformationMatrixes == null || result.facialTransformationMatrixes.Count == 0)
                {
                    return false;
                }

                List<Category> categories = result.faceBlendshapes[0].categories;
                if (_shapeOf == null || _shapeOf.Length != categories.Count)
                {
                    _shapeOf = MapShapes(categories, sidesMirrored, out ulong supported);
                    Supported = supported;
                }

                Array.Clear(Shapes, 0, Shapes.Length);
                for (int i = 0; i < categories.Count; i++)
                {
                    if (_shapeOf[i] >= 0)
                    {
                        Shapes[_shapeOf[i]] = categories[i].score;
                    }
                }

                Matrix = result.facialTransformationMatrixes[0];
                return true;
            }

            private static int[] MapShapes(List<Category> categories, bool sidesMirrored, out ulong supported)
            {
                var map = new int[categories.Count];
                supported = 0;
                for (int i = 0; i < categories.Count; i++)
                {
                    map[i] = -1;
                    if (MediaPipeConversion.TryParseShape(categories[i].categoryName, out FaceShape shape))
                    {
                        if (sidesMirrored)
                        {
                            shape = MediaPipeConversion.OtherSide(shape);
                        }

                        map[i] = (int)shape;
                        supported |= 1UL << (int)shape;
                    }
                }

                return map;
            }
        }

        /// <summary>Hand results copied out of the plugin's types.</summary>
        private sealed class HandScratch
        {
            private readonly Vector3[] _world = new Vector3[MediaPipeConversion.HandLandmarkCount];
            private readonly Vector2[] _image = new Vector2[MediaPipeConversion.HandLandmarkCount];

            public long SubmittedAt { get; set; }

            public double LastSeconds { get; set; }

            public void Read(HandLandmarkerResult result, double time, HandsObservation into)
            {
                into.Time = time;
                into.Count = 0;
                int count = Math.Min(2, Math.Min(result.handLandmarks?.Count ?? 0, result.handWorldLandmarks?.Count ?? 0));
                for (int h = 0; h < count; h++)
                {
                    List<NormalizedLandmark> image = result.handLandmarks[h].landmarks;
                    List<Landmark> world = result.handWorldLandmarks[h].landmarks;
                    if (image == null || world == null || image.Count < MediaPipeConversion.HandLandmarkCount || world.Count < MediaPipeConversion.HandLandmarkCount)
                    {
                        continue;
                    }

                    for (int i = 0; i < MediaPipeConversion.HandLandmarkCount; i++)
                    {
                        _world[i] = new Vector3(world[i].x, world[i].y, world[i].z);
                        _image[i] = new Vector2(image[i].x, image[i].y);
                    }

                    Category category = result.handedness != null && result.handedness.Count > h && result.handedness[h].categories?.Count > 0
                        ? result.handedness[h].categories[0]
                        : default;

                    // Placed at zero here; the main thread moves it to the body's wrist once the side is known.
                    int slot = into.Count++;
                    MediaPipeConversion.WriteHand(_world, _image, category.score, Vector3.zero, left: false, time, into.Hands[slot]);
                    into.Handedness[slot] = category.categoryName;
                }
            }
        }

        /// <summary>Up to two hands as detected, before their sides are decided.</summary>
        private sealed class HandsObservation
        {
            public HandPose[] Hands { get; } = { new HandPose(), new HandPose() };

            public string[] Handedness { get; } = new string[2];

            public int Count { get; set; }

            public double Time { get; set; }

            /// <summary>Decides each hand's side from the body's wrists and moves it there (tracking detailed design 6.4).</summary>
            public void AssignTo(TrackingFrame frame)
            {
                BodyPose body = frame.Body;
                bool bodySeen = body.IsUsable;
                Vector2 leftWrist = body.ImagePositions[(int)BodyJoint.LeftWrist];
                Vector2 rightWrist = body.ImagePositions[(int)BodyJoint.RightWrist];

                bool firstLeft = Count switch
                {
                    1 => MediaPipeConversion.IsLeft(Hands[0].ImagePositions[0], bodySeen, leftWrist, rightWrist, Handedness[0]),
                    2 => MediaPipeConversion.FirstIsLeft(Hands[0].ImagePositions[0], Hands[1].ImagePositions[0], bodySeen, leftWrist, rightWrist, Handedness[0]),
                    _ => false,
                };

                Place(frame.LeftHand, Count >= 1 && firstLeft ? Hands[0] : Count == 2 && !firstLeft ? Hands[1] : null, true, body);
                Place(frame.RightHand, Count >= 1 && !firstLeft ? Hands[0] : Count == 2 && firstLeft ? Hands[1] : null, false, body);
            }

            private void Place(HandPose target, HandPose detected, bool left, BodyPose body)
            {
                if (detected == null)
                {
                    target.Status = TrackingStatus.Lost;
                    target.Timestamp = Time;
                    return;
                }

                target.CopyFrom(detected);
                Vector3 wrist = body.IsUsable ? body[left ? BodyJoint.LeftWrist : BodyJoint.RightWrist] : Vector3.zero;
                for (int i = 0; i < HandPose.JointCount; i++)
                {
                    target.Positions[i] += wrist;
                }

                target.PalmRotation = MediaPipeConversion.PalmRotation(
                    target[HandJoint.Wrist], target[HandJoint.IndexMcp], target[HandJoint.MiddleMcp], target[HandJoint.LittleMcp], left);
            }
        }
    }
}
