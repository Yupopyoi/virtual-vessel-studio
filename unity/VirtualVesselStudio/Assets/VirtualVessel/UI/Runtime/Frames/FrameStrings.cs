using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Frames
{
    /// <summary>String table keys of the placement editor.</summary>
    [StringKeys]
    public static class FrameStrings
    {
        /// <summary>Badge on a locked frame (components.md 3.19).</summary>
        public const string Locked = "frame.locked";

        public const string Mode = "frame.mode";
        public const string ModePlace = "frame.mode.place";
        public const string ModeCrop = "frame.mode.crop";
        public const string ModeTip = "frame.mode.tip";

        // The frame menu (UI foundation detailed design 9.4): each entry and what it does.
        public const string Flip = "frame.flip";
        public const string FlipTip = "frame.flip.tip";
        public const string Lock = "frame.lock";
        public const string LockTip = "frame.lock.tip";
        public const string AllowOutside = "frame.allow-outside";
        public const string AllowOutsideTip = "frame.allow-outside.tip";
        public const string CropMode = "frame.crop-mode";
        public const string CropModeTip = "frame.crop-mode.tip";
        public const string ResetCrop = "frame.reset-crop";
        public const string ResetCropTip = "frame.reset-crop.tip";
        public const string Fit = "frame.fit";
        public const string FitTip = "frame.fit.tip";
        public const string Fill = "frame.fill";
        public const string FillTip = "frame.fill.tip";
        public const string Center = "frame.center";
        public const string CenterTip = "frame.center.tip";
        public const string BringForward = "frame.forward";
        public const string BringForwardTip = "frame.forward.tip";
        public const string SendBackward = "frame.backward";
        public const string SendBackwardTip = "frame.backward.tip";
        public const string BringToFront = "frame.front";
        public const string BringToFrontTip = "frame.front.tip";
        public const string SendToBack = "frame.back";
        public const string SendToBackTip = "frame.back.tip";
    }
}
