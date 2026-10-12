using System;
using System.Collections.Generic;

namespace VirtualVessel.Core.Geometry
{
    /// <summary>The eight resize handles of a frame.</summary>
    public enum FrameHandle
    {
        TopLeft,
        Top,
        TopRight,
        Right,
        BottomRight,
        Bottom,
        BottomLeft,
        Left,
    }

    /// <summary>Size of the stream screen in output pixels, such as 1920 x 1080.</summary>
    /// <remarks>
    /// Needed because aspect ratios, minimum sizes, and nudges are defined in pixels, while placement
    /// is stored normalized.
    /// </remarks>
    public readonly struct CanvasSize
    {
        public CanvasSize(double width, double height)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Canvas size must be positive.");
            }

            Width = width;
            Height = height;
        }

        public double Width { get; }

        public double Height { get; }
    }

    public readonly struct ResizeOptions
    {
        public ResizeOptions(bool keepAspect, bool fromCenter)
        {
            KeepAspect = keepAspect;
            FromCenter = fromCenter;
        }

        /// <summary>The default: keep the aspect ratio and fix the opposite side.</summary>
        public static ResizeOptions Default => new ResizeOptions(keepAspect: true, fromCenter: false);

        public bool KeepAspect { get; }

        public bool FromCenter { get; }
    }

    /// <summary>
    /// Lines a moving or resized frame snaps to, in normalized canvas coordinates, with the snap
    /// distance per axis.
    /// </summary>
    public sealed class SnapLines
    {
        public static readonly SnapLines None = new SnapLines(Array.Empty<double>(), Array.Empty<double>(), 0, 0);

        public SnapLines(IReadOnlyList<double> vertical, IReadOnlyList<double> horizontal, double thresholdX, double thresholdY)
        {
            Vertical = vertical ?? throw new ArgumentNullException(nameof(vertical));
            Horizontal = horizontal ?? throw new ArgumentNullException(nameof(horizontal));
            ThresholdX = thresholdX;
            ThresholdY = thresholdY;
        }

        /// <summary>X positions of vertical lines.</summary>
        public IReadOnlyList<double> Vertical { get; }

        /// <summary>Y positions of horizontal lines.</summary>
        public IReadOnlyList<double> Horizontal { get; }

        public double ThresholdX { get; }

        public double ThresholdY { get; }
    }

    /// <summary>A placement after an edit, with the snap lines it ended up on (for drawing guides).</summary>
    public readonly struct FrameEditResult
    {
        public FrameEditResult(NormalizedRect rect, double? guideX, double? guideY)
        {
            Rect = rect;
            GuideX = guideX;
            GuideY = guideY;
        }

        public NormalizedRect Rect { get; }

        /// <summary>X of the vertical line the frame snapped to, or null.</summary>
        public double? GuideX { get; }

        /// <summary>Y of the horizontal line the frame snapped to, or null.</summary>
        public double? GuideY { get; }
    }
}
