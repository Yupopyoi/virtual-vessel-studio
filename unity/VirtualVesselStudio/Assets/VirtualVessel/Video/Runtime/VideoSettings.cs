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
    }
}
