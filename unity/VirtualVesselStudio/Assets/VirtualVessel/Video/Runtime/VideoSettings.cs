using UnityEngine;

namespace VirtualVessel.Video
{
    /// <summary>The stream video's size, frame rate, and background (video detailed design 6.1). Not saved yet.</summary>
    public sealed class VideoSettings
    {
        public int Width { get; set; } = 1920;

        public int Height { get; set; } = 1080;

        public int TargetFrameRate { get; set; } = 60;

        /// <summary>What shows where the stage draws nothing; stream content, so a fixed color rather than a UI token.</summary>
        public Color BackgroundColor { get; set; } = new Color(0.08f, 0.07f, 0.13f, 1f);

        /// <summary>
        /// Where the Main Camera starts, until Camera Points exist (video detailed design 7.5): in front of
        /// an avatar standing at the origin, framing its upper body. Null leaves the camera where the scene put it.
        /// </summary>
        public Vector3? CameraPosition { get; set; } = new Vector3(0f, 1.25f, -1.9f);

        /// <summary>Vertical field of view in degrees. A narrow one keeps a close-up of the face free of wide-angle distortion.</summary>
        public float? CameraFieldOfView { get; set; } = 30f;
    }
}
