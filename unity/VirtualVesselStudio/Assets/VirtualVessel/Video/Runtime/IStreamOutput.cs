using UnityEngine;
using VirtualVessel.Core.State;

namespace VirtualVessel.Video
{
    /// <summary>The stream video and where flat videos go on it (video detailed design 5).</summary>
    public interface IStreamOutput
    {
        /// <summary>The Stream Render Target: what is streamed, recorded, and previewed. A new texture when the size changes.</summary>
        IReadOnlyObservable<Texture> Video { get; }

        VideoSize Size { get; }

        IStreamScreenLayer Screens { get; }

        /// <summary>
        /// The Unity layer avatars must use: drawn by the Avatar Camera, in front of the flat videos
        /// (video detailed design 7.2). -1 before the video starts.
        /// </summary>
        int AvatarLayer { get; }

        /// <summary>
        /// Where the Main Camera shoots the stage from, for the stage view's camera guide (Stage detailed design 7.4.6);
        /// null before the video starts.
        /// </summary>
        StreamCameraPose? Camera { get; }
    }

    /// <summary>The Main Camera's place and lens: from where, facing which way (+Z), its vertical view, and its shape.</summary>
    public readonly struct StreamCameraPose
    {
        public StreamCameraPose(Vector3 position, Quaternion rotation, float fieldOfView, float aspect)
        {
            Position = position;
            Rotation = rotation;
            FieldOfView = fieldOfView;
            Aspect = aspect;
        }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        /// <summary>The vertical view in degrees.</summary>
        public float FieldOfView { get; }

        /// <summary>Width over height.</summary>
        public float Aspect { get; }
    }

    /// <summary>The layer of videos placed flat on the stream screen, above the stage and below the avatar.</summary>
    public interface IStreamScreenLayer
    {
        /// <summary>
        /// Children's local x goes from 0 (left) to 1 (right) and y from 0 (top) to 1 (bottom) over the
        /// stream screen. A smaller local z is drawn in front.
        /// </summary>
        Transform Root { get; }

        /// <summary>The Unity layer children must use, so that only the screen camera draws them.</summary>
        int Layer { get; }
    }

    public readonly struct VideoSize
    {
        public VideoSize(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; }

        public int Height { get; }

        public float Aspect => Height > 0 ? (float)Width / Height : 1f;

        public override string ToString()
        {
            return $"{Width}×{Height}";
        }
    }
}
