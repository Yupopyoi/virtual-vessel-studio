using System;
using System.Collections.Generic;

namespace VirtualVessel.Core.Geometry
{
    /// <summary>
    /// Placement editing math for frames on the stream screen: move, resize, snap, crop, fit
    /// (UI foundation detailed design 9).
    /// </summary>
    /// <remarks>
    /// Pure functions without Unity types, so that every interaction can be tested in EditMode. The UI
    /// converts pointer movement to normalized deltas and calls these on each pointer event.
    /// Operations are relative to the state at the start of a drag, not accumulated per event, so that
    /// rounding and clamping do not drift.
    /// </remarks>
    public static class FrameEditMath
    {
        /// <summary>
        /// Smallest frame width and height, as a fraction of the stream screen. The prototype used a
        /// fixed 200 px; a fraction keeps the same feel at any output resolution.
        /// </summary>
        public const double MinimumSizeFraction = 0.05;

        /// <summary>Largest zoom into a video while cropping.</summary>
        public const double MaximumCropZoom = 16;

        /// <summary>Smallest crop side, as a fraction of the video.</summary>
        public const double MinimumCropFraction = 1.0 / MaximumCropZoom;

        /// <summary>
        /// Moves <paramref name="start"/> by a normalized delta. Without <paramref name="allowOutside"/>,
        /// the frame stays on the stream screen.
        /// </summary>
        public static NormalizedRect Move(NormalizedRect start, double dx, double dy, bool allowOutside)
        {
            var moved = start.WithPosition(start.X + dx, start.Y + dy);
            return allowOutside ? moved : KeepInside(moved);
        }

        /// <summary>Moves by whole output pixels, as the arrow keys do.</summary>
        public static NormalizedRect Nudge(NormalizedRect start, int pixelsX, int pixelsY, CanvasSize canvas, bool allowOutside)
        {
            return Move(start, pixelsX / canvas.Width, pixelsY / canvas.Height, allowOutside);
        }

        /// <summary>
        /// Moves and snaps the frame's left, center, or right (and top, center, or bottom) to the
        /// nearest line within the threshold.
        /// </summary>
        public static FrameEditResult MoveWithSnap(NormalizedRect start, double dx, double dy, bool allowOutside, SnapLines snap)
        {
            if (snap == null)
            {
                throw new ArgumentNullException(nameof(snap));
            }

            NormalizedRect moved = start.WithPosition(start.X + dx, start.Y + dy);

            double? guideX = FindSnap(snap.Vertical, snap.ThresholdX, moved.Left, moved.CenterX, moved.Right, out double offsetX);
            double? guideY = FindSnap(snap.Horizontal, snap.ThresholdY, moved.Top, moved.CenterY, moved.Bottom, out double offsetY);
            NormalizedRect snapped = moved.WithPosition(moved.X + offsetX, moved.Y + offsetY);

            if (!allowOutside)
            {
                NormalizedRect inside = KeepInside(snapped);

                // Clamping can move the frame off the line; then the guide would be misleading.
                guideX = SameValue(inside.X, snapped.X) ? guideX : null;
                guideY = SameValue(inside.Y, snapped.Y) ? guideY : null;
                snapped = inside;
            }

            return new FrameEditResult(snapped, guideX, guideY);
        }

        /// <summary>
        /// Resizes by dragging <paramref name="handle"/> by a normalized pointer delta.
        /// </summary>
        /// <remarks>
        /// Corner handles fix the opposite corner; edge handles fix the opposite edge (its center when
        /// keeping the aspect ratio). With <see cref="ResizeOptions.FromCenter"/>, the center is fixed.
        /// When keeping the aspect ratio with a corner, the axis the pointer changed relatively more
        /// decides the scale, which feels natural for diagonal drags.
        /// </remarks>
        public static FrameEditResult Resize(
            NormalizedRect start,
            FrameHandle handle,
            double dx,
            double dy,
            ResizeOptions options,
            CanvasSize canvas,
            bool allowOutside,
            SnapLines snap = null)
        {
            // Work in pixels: aspect ratios and minimum sizes are only meaningful there.
            double w = canvas.Width;
            double h = canvas.Height;
            double left = start.Left * w;
            double top = start.Top * h;
            double right = start.Right * w;
            double bottom = start.Bottom * h;
            double startWidth = right - left;
            double startHeight = bottom - top;
            (int hx, int hy) = Direction(handle);

            double movingX = (hx < 0 ? left : hx > 0 ? right : (left + right) * 0.5) + dx * w;
            double movingY = (hy < 0 ? top : hy > 0 ? bottom : (top + bottom) * 0.5) + dy * h;

            double? snappedX = null;
            double? snappedY = null;
            if (snap != null)
            {
                if (hx != 0 && TryNearest(snap.Vertical, movingX / w, snap.ThresholdX, out double lineX))
                {
                    movingX = lineX * w;
                    snappedX = lineX;
                }

                if (hy != 0 && TryNearest(snap.Horizontal, movingY / h, snap.ThresholdY, out double lineY))
                {
                    movingY = lineY * h;
                    snappedY = lineY;
                }
            }

            double anchorX = options.FromCenter || hx == 0 ? (left + right) * 0.5 : hx < 0 ? right : left;
            double anchorY = options.FromCenter || hy == 0 ? (top + bottom) * 0.5 : hy < 0 ? bottom : top;
            double spread = options.FromCenter ? 2 : 1;

            double width = hx != 0 ? Math.Max(0, (movingX - anchorX) * hx * spread) : startWidth;
            double height = hy != 0 ? Math.Max(0, (movingY - anchorY) * hy * spread) : startHeight;

            double minWidth = MinimumSizeFraction * w;
            double minHeight = MinimumSizeFraction * h;
            double maxWidth = AvailableExtent(anchorX, w, options.FromCenter || hx == 0 ? 0 : hx);
            double maxHeight = AvailableExtent(anchorY, h, options.FromCenter || hy == 0 ? 0 : hy);

            if (options.KeepAspect && startWidth > 0 && startHeight > 0)
            {
                double scaleX = width / startWidth;
                double scaleY = height / startHeight;
                double scale = hx != 0 && hy != 0
                    ? (Math.Abs(scaleX - 1) >= Math.Abs(scaleY - 1) ? scaleX : scaleY)
                    : hx != 0 ? scaleX : scaleY;

                double minScale = Math.Max(minWidth / startWidth, minHeight / startHeight);
                scale = Math.Max(scale, minScale);
                if (!allowOutside)
                {
                    double maxScale = Math.Min(maxWidth / startWidth, maxHeight / startHeight);

                    // A frame already outside the screen (AllowOutside was just turned off) may not
                    // fit at any size; then the minimum wins instead of producing a negative size.
                    if (maxScale >= minScale)
                    {
                        scale = Math.Min(scale, maxScale);
                    }
                }

                width = startWidth * scale;
                height = startHeight * scale;
            }
            else
            {
                if (hx != 0)
                {
                    width = ClampSize(width, minWidth, maxWidth, allowOutside);
                }

                if (hy != 0)
                {
                    height = ClampSize(height, minHeight, maxHeight, allowOutside);
                }
            }

            double newLeft = options.FromCenter || hx == 0 ? anchorX - width * 0.5 : hx > 0 ? anchorX : anchorX - width;
            double newTop = options.FromCenter || hy == 0 ? anchorY - height * 0.5 : hy > 0 ? anchorY : anchorY - height;
            var result = new NormalizedRect(newLeft / w, newTop / h, width / w, height / h);

            // Report a guide only when the dragged edge really ended on the line, not when the aspect
            // ratio or a limit moved it away.
            double? guideX = snappedX.HasValue && SameValue((hx > 0 ? result.Right : result.Left) * w, snappedX.Value * w, 0.5) ? snappedX : null;
            double? guideY = snappedY.HasValue && SameValue((hy > 0 ? result.Bottom : result.Top) * h, snappedY.Value * h, 0.5) ? snappedY : null;
            return new FrameEditResult(result, guideX, guideY);
        }

        /// <summary>Snap lines from the stream screen's edges and center and from other frames.</summary>
        /// <param name="thresholdX">Snap distance as a fraction of the stream screen width.</param>
        /// <param name="thresholdY">Snap distance as a fraction of the stream screen height.</param>
        public static SnapLines CreateSnapLines(IEnumerable<NormalizedRect> otherFrames, double thresholdX, double thresholdY)
        {
            var vertical = new List<double> { 0, 0.5, 1 };
            var horizontal = new List<double> { 0, 0.5, 1 };
            if (otherFrames != null)
            {
                foreach (NormalizedRect frame in otherFrames)
                {
                    vertical.Add(frame.Left);
                    vertical.Add(frame.CenterX);
                    vertical.Add(frame.Right);
                    horizontal.Add(frame.Top);
                    horizontal.Add(frame.CenterY);
                    horizontal.Add(frame.Bottom);
                }
            }

            return new SnapLines(vertical, horizontal, thresholdX, thresholdY);
        }

        /// <summary>
        /// Zooms the crop by <paramref name="factor"/> (greater than 1 zooms in) keeping the video point
        /// under the pointer in place.
        /// </summary>
        /// <param name="pivotX">Pointer position across the shown video, 0 (left) to 1 (right).</param>
        /// <param name="pivotY">Pointer position down the shown video, 0 (top) to 1 (bottom).</param>
        /// <remarks>For a flipped frame, the caller passes <c>1 - x</c> as the pivot.</remarks>
        public static NormalizedRect ZoomCrop(NormalizedRect crop, double factor, double pivotX, double pivotY)
        {
            if (factor <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(factor));
            }

            // Scale both sides by the same ratio so the shown aspect ratio does not change.
            double ratio = 1 / factor;
            double maxRatio = Math.Min(1 / crop.Width, 1 / crop.Height);
            double minRatio = MinimumCropFraction / Math.Min(crop.Width, crop.Height);
            ratio = Math.Max(Math.Min(ratio, maxRatio), Math.Min(minRatio, maxRatio));

            double width = crop.Width * ratio;
            double height = crop.Height * ratio;
            double sourceX = crop.X + pivotX * crop.Width;
            double sourceY = crop.Y + pivotY * crop.Height;
            var zoomed = new NormalizedRect(sourceX - pivotX * width, sourceY - pivotY * height, width, height);
            return KeepInside(zoomed);
        }

        /// <summary>
        /// Pans a zoomed crop as the pointer drags the video by a fraction of the frame size.
        /// </summary>
        /// <remarks>Dragging right reveals the left part, like moving a photo under a window.</remarks>
        public static NormalizedRect PanCrop(NormalizedRect crop, double dragFractionX, double dragFractionY)
        {
            return KeepInside(crop.WithPosition(crop.X - dragFractionX * crop.Width, crop.Y - dragFractionY * crop.Height));
        }

        /// <summary>Zoom level of a crop in percent, for display (100% is the whole video).</summary>
        public static double ZoomPercent(NormalizedRect crop)
        {
            return 100 / Math.Max(crop.Width, crop.Height);
        }

        /// <summary>
        /// Crops by dragging an edge (Alt + handle): the frame edge and the video edge move together, so
        /// the video keeps its scale on the stream screen.
        /// </summary>
        /// <param name="dx">Normalized horizontal pointer delta on the stream screen.</param>
        /// <param name="dy">Normalized vertical pointer delta on the stream screen.</param>
        public static FrameLayout CropEdge(FrameLayout layout, FrameHandle handle, double dx, double dy)
        {
            NormalizedRect p = layout.Placement;
            NormalizedRect c = layout.Crop;
            double scaleX = p.Width / c.Width;
            double scaleY = p.Height / c.Height;
            double pl = p.Left;
            double pr = p.Right;
            double pt = p.Top;
            double pb = p.Bottom;
            double cl = c.Left;
            double cr = c.Right;
            double ct = c.Top;
            double cb = c.Bottom;
            (int hx, int hy) = Direction(handle);

            // With a horizontal flip, the frame's left edge shows the video's right edge.
            bool flip = layout.FlipHorizontal;
            if ((hx < 0 && !flip) || (hx > 0 && flip))
            {
                double frameDelta = hx < 0 ? dx : -dx;
                double newLeft = Clamp(cl + frameDelta / scaleX, 0, cr - MinimumCropFraction);
                double applied = (newLeft - cl) * scaleX;
                cl = newLeft;
                if (hx < 0)
                {
                    pl += applied;
                }
                else
                {
                    pr -= applied;
                }
            }
            else if (hx != 0)
            {
                double frameDelta = hx > 0 ? dx : -dx;
                double newRight = Clamp(cr + frameDelta / scaleX, cl + MinimumCropFraction, 1);
                double applied = (newRight - cr) * scaleX;
                cr = newRight;
                if (hx > 0)
                {
                    pr += applied;
                }
                else
                {
                    pl -= applied;
                }
            }

            if (hy < 0)
            {
                double newTop = Clamp(ct + dy / scaleY, 0, cb - MinimumCropFraction);
                pt += (newTop - ct) * scaleY;
                ct = newTop;
            }
            else if (hy > 0)
            {
                double newBottom = Clamp(cb + dy / scaleY, ct + MinimumCropFraction, 1);
                pb += (newBottom - cb) * scaleY;
                cb = newBottom;
            }

            return layout
                .WithPlacement(NormalizedRect.FromEdges(pl, pt, pr, pb))
                .WithCrop(NormalizedRect.FromEdges(cl, ct, cr, cb));
        }

        /// <summary>The largest placement that fits on the stream screen, centered.</summary>
        /// <param name="sourceAspect">Width / height of the whole video in pixels.</param>
        public static NormalizedRect Fit(NormalizedRect crop, double sourceAspect, CanvasSize canvas)
        {
            return FitOrFill(crop, sourceAspect, canvas, fill: false);
        }

        /// <summary>The smallest placement that covers the stream screen, centered.</summary>
        /// <param name="sourceAspect">Width / height of the whole video in pixels.</param>
        public static NormalizedRect Fill(NormalizedRect crop, double sourceAspect, CanvasSize canvas)
        {
            return FitOrFill(crop, sourceAspect, canvas, fill: true);
        }

        public static NormalizedRect Center(NormalizedRect placement)
        {
            return placement.WithPosition((1 - placement.Width) * 0.5, (1 - placement.Height) * 0.5);
        }

        internal static (int X, int Y) Direction(FrameHandle handle)
        {
            switch (handle)
            {
                case FrameHandle.TopLeft:
                    return (-1, -1);
                case FrameHandle.Top:
                    return (0, -1);
                case FrameHandle.TopRight:
                    return (1, -1);
                case FrameHandle.Right:
                    return (1, 0);
                case FrameHandle.BottomRight:
                    return (1, 1);
                case FrameHandle.Bottom:
                    return (0, 1);
                case FrameHandle.BottomLeft:
                    return (-1, 1);
                case FrameHandle.Left:
                    return (-1, 0);
                default:
                    throw new ArgumentOutOfRangeException(nameof(handle), handle, null);
            }
        }

        private static NormalizedRect FitOrFill(NormalizedRect crop, double sourceAspect, CanvasSize canvas, bool fill)
        {
            if (sourceAspect <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceAspect));
            }

            double contentAspect = sourceAspect * crop.Width / crop.Height;
            double canvasAspect = canvas.Width / canvas.Height;
            bool matchWidth = fill ? contentAspect < canvasAspect : contentAspect >= canvasAspect;

            double width;
            double height;
            if (matchWidth)
            {
                width = 1;
                height = canvas.Width / contentAspect / canvas.Height;
            }
            else
            {
                height = 1;
                width = canvas.Height * contentAspect / canvas.Width;
            }

            return Center(new NormalizedRect(0, 0, width, height));
        }

        /// <summary>
        /// Keeps a rectangle within 0 to 1. A rectangle larger than the area keeps covering it instead.
        /// </summary>
        private static NormalizedRect KeepInside(NormalizedRect rect)
        {
            double x = Clamp(rect.X, Math.Min(0, 1 - rect.Width), Math.Max(0, 1 - rect.Width));
            double y = Clamp(rect.Y, Math.Min(0, 1 - rect.Height), Math.Max(0, 1 - rect.Height));
            return rect.WithPosition(x, y);
        }

        private static double AvailableExtent(double anchor, double size, int direction)
        {
            if (direction > 0)
            {
                return size - anchor;
            }

            if (direction < 0)
            {
                return anchor;
            }

            return 2 * Math.Min(anchor, size - anchor);
        }

        private static double ClampSize(double value, double min, double max, bool allowOutside)
        {
            value = Math.Max(value, min);
            if (!allowOutside && max >= min)
            {
                value = Math.Min(value, max);
            }

            return value;
        }

        private static double? FindSnap(IReadOnlyList<double> lines, double threshold, double low, double middle, double high, out double offset)
        {
            double bestOffset = 0;
            double? guide = null;
            double best = threshold;
            for (int i = 0; i < lines.Count; i++)
            {
                double line = lines[i];
                Consider(line, low);
                Consider(line, middle);
                Consider(line, high);
            }

            offset = bestOffset;
            return guide;

            void Consider(double line, double edge)
            {
                double distance = Math.Abs(line - edge);
                if (distance <= best)
                {
                    best = distance;
                    bestOffset = line - edge;
                    guide = line;
                }
            }
        }

        private static bool TryNearest(IReadOnlyList<double> lines, double value, double threshold, out double nearest)
        {
            nearest = 0;
            double best = threshold;
            bool found = false;
            for (int i = 0; i < lines.Count; i++)
            {
                double distance = Math.Abs(lines[i] - value);
                if (distance <= best)
                {
                    best = distance;
                    nearest = lines[i];
                    found = true;
                }
            }

            return found;
        }

        private static double Clamp(double value, double min, double max)
        {
            return value < min ? min : value > max ? max : value;
        }

        private static bool SameValue(double a, double b, double tolerance = 1e-9)
        {
            return Math.Abs(a - b) <= tolerance;
        }
    }
}
