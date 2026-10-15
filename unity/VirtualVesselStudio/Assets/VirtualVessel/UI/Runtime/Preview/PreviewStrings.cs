using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Preview
{
    /// <summary>String table keys of the stream preview.</summary>
    [StringKeys]
    public static class PreviewStrings
    {
        /// <summary>The label before the resolution and bitrate.</summary>
        public const string Label = "preview.label";

        /// <summary>What the frame rate chip means.</summary>
        public const string FrameRateTip = "preview.frame-rate.tip";

        public const string Fullscreen = "preview.fullscreen";
        public const string ExitFullscreen = "preview.exit-fullscreen";

        /// <summary>Shown for a moment after entering full screen.</summary>
        public const string FullscreenHint = "preview.fullscreen-hint";

        /// <summary>Shown while there is no stream video yet.</summary>
        public const string NoVideo = "preview.no-video";
    }
}
