using System;
using System.IO;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using BaseOptions = Mediapipe.Tasks.Core.BaseOptions;
using MediaPipeImage = Mediapipe.Image;
using RunningMode = Mediapipe.Tasks.Vision.Core.RunningMode;

namespace VirtualVessel.Tracking.MediaPipe
{
    /// <summary>The callbacks the tasks deliver their results to, on MediaPipe's threads.</summary>
    internal sealed class MediaPipeCallbacks
    {
        public MediaPipeCallbacks(FaceLandmarkerOptions.ResultCallback face, PoseLandmarkerOptions.ResultCallback pose, HandLandmarkerOptions.ResultCallback hands)
        {
            Face = face ?? throw new ArgumentNullException(nameof(face));
            Pose = pose ?? throw new ArgumentNullException(nameof(pose));
            Hands = hands ?? throw new ArgumentNullException(nameof(hands));
        }

        public FaceLandmarkerOptions.ResultCallback Face { get; }

        public PoseLandmarkerOptions.ResultCallback Pose { get; }

        public HandLandmarkerOptions.ResultCallback Hands { get; }
    }

    /// <summary>
    /// The MediaPipe tasks, created together from the model files and closed together (tracking
    /// detailed design 9.3, 16.2). With the result readers, the only code that uses the plugin's task
    /// API: when the plugin changes, the changes stay here.
    /// </summary>
    internal sealed class MediaPipeTasks : IDisposable
    {
        public const string FaceModel = "face_landmarker_v2_with_blendshapes.bytes";
        public const string HandModel = "hand_landmarker.bytes";

        private FaceLandmarker _face;
        private PoseLandmarker _pose;
        private HandLandmarker _hand;

        private MediaPipeTasks()
        {
        }

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

        /// <summary>
        /// Reads the models from <paramref name="directory"/> and builds the graphs, in LIVE_STREAM mode on
        /// the CPU. Takes seconds; called off the main thread. Models go to the plugin as bytes, so its own
        /// asset loading is not needed.
        /// </summary>
        public static MediaPipeTasks Create(string directory, PoseModel poseModel, MediaPipeCallbacks callbacks)
        {
            byte[] face = File.ReadAllBytes(Path.Combine(directory, FaceModel));
            byte[] pose = File.ReadAllBytes(Path.Combine(directory, PoseModelFile(poseModel)));
            byte[] hand = File.ReadAllBytes(Path.Combine(directory, HandModel));

            var tasks = new MediaPipeTasks();
            try
            {
                tasks._face = FaceLandmarker.CreateFromOptions(new FaceLandmarkerOptions(
                    new BaseOptions(BaseOptions.Delegate.CPU, modelAssetBuffer: face),
                    RunningMode.LIVE_STREAM,
                    numFaces: 1,
                    outputFaceBlendshapes: true,
                    outputFaceTransformationMatrixes: true,
                    resultCallback: callbacks.Face));
                tasks._pose = PoseLandmarker.CreateFromOptions(new PoseLandmarkerOptions(
                    new BaseOptions(BaseOptions.Delegate.CPU, modelAssetBuffer: pose),
                    RunningMode.LIVE_STREAM,
                    numPoses: 1,
                    resultCallback: callbacks.Pose));
                tasks._hand = HandLandmarker.CreateFromOptions(new HandLandmarkerOptions(
                    new BaseOptions(BaseOptions.Delegate.CPU, modelAssetBuffer: hand),
                    RunningMode.LIVE_STREAM,
                    numHands: 2,
                    resultCallback: callbacks.Hands));
                return tasks;
            }
            catch
            {
                tasks.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Gives <paramref name="image"/> to the task of <paramref name="part"/> (Face, Body, or Hands).
        /// The image is moved into the task's packet and must not be used afterwards.
        /// </summary>
        public void Detect(TrackingRegions part, MediaPipeImage image, long milliseconds)
        {
            switch (part)
            {
                case TrackingRegions.Face:
                    _face.DetectAsync(image, milliseconds);
                    break;
                case TrackingRegions.Body:
                    _pose.DetectAsync(image, milliseconds);
                    break;
                case TrackingRegions.Hands:
                    _hand.DetectAsync(image, milliseconds);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(part), part, "Face, Body, or Hands.");
            }
        }

        /// <summary>Closes the graphs; closing waits for them to finish, which releases their images.</summary>
        public void Dispose()
        {
            _face?.Close();
            _pose?.Close();
            _hand?.Close();
            _face = null;
            _pose = null;
            _hand = null;
        }
    }
}
