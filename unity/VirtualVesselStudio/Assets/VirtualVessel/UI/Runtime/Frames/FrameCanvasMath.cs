using System;
using System.Collections.Generic;
using System.Globalization;
using VirtualVessel.Core.Geometry;

namespace VirtualVessel.UI.Frames
{
    /// <summary>Mouse cursor shapes for placement editing (components.md 3.19).</summary>
    public enum CursorKind
    {
        Default,
        Move,
        ResizeHorizontal,
        ResizeVertical,

        /// <summary>↘↖: top-left and bottom-right corners.</summary>
        ResizeDiagonalDown,

        /// <summary>↗↙: top-right and bottom-left corners.</summary>
        ResizeDiagonalUp,
        NotAllowed,
    }

    /// <summary>
    /// Hit testing and display decisions of the frame canvas. Pure; geometry itself is
    /// <see cref="FrameEditMath"/>.
    /// </summary>
    public static class FrameCanvasMath
    {
        /// <summary>Handles in the order of <see cref="FrameHandle"/>: clockwise from the top left.</summary>
        public static readonly FrameHandle[] Handles =
        {
            FrameHandle.TopLeft, FrameHandle.Top, FrameHandle.TopRight, FrameHandle.Right,
            FrameHandle.BottomRight, FrameHandle.Bottom, FrameHandle.BottomLeft, FrameHandle.Left,
        };

        /// <summary>Where a handle sits across the frame, 0 to 1 on each axis.</summary>
        public static (double X, double Y) HandleFraction(FrameHandle handle)
        {
            return handle switch
            {
                FrameHandle.TopLeft => (0, 0),
                FrameHandle.Top => (0.5, 0),
                FrameHandle.TopRight => (1, 0),
                FrameHandle.Right => (1, 0.5),
                FrameHandle.BottomRight => (1, 1),
                FrameHandle.Bottom => (0.5, 1),
                FrameHandle.BottomLeft => (0, 1),
                _ => (0, 0.5),
            };
        }

        /// <summary>
        /// The handle under a point, or null. Coordinates are pixels in the stream area of
        /// <paramref name="areaWidth"/> × <paramref name="areaHeight"/>; a handle can be grabbed within
        /// <paramref name="radius"/> pixels of its center on each axis. Corners win over edges.
        /// </summary>
        public static FrameHandle? HandleAt(NormalizedRect placement, double x, double y, double areaWidth, double areaHeight, double radius)
        {
            FrameHandle? edge = null;
            foreach (FrameHandle handle in Handles)
            {
                (double fx, double fy) = HandleFraction(handle);
                double hx = (placement.X + placement.Width * fx) * areaWidth;
                double hy = (placement.Y + placement.Height * fy) * areaHeight;
                if (Math.Abs(x - hx) > radius || Math.Abs(y - hy) > radius)
                {
                    continue;
                }

                if (IsCorner(handle))
                {
                    return handle;
                }

                edge ??= handle;
            }

            return edge;
        }

        /// <summary>
        /// The index of the frontmost frame containing the normalized point, or -1. Higher
        /// <see cref="FrameLayout.Order"/> is in front; among equal orders, the later one is.
        /// </summary>
        public static int FrameAt(IReadOnlyList<FrameLayout> layouts, double x, double y)
        {
            int found = -1;
            for (int i = 0; i < layouts.Count; i++)
            {
                NormalizedRect p = layouts[i].Placement;
                bool inside = x >= p.Left && x <= p.Right && y >= p.Top && y <= p.Bottom;
                if (inside && (found < 0 || layouts[i].Order >= layouts[found].Order))
                {
                    found = i;
                }
            }

            return found;
        }

        /// <summary>The cursor for what is under the pointer.</summary>
        public static CursorKind CursorFor(FrameHandle? handle, bool overFrame, bool locked)
        {
            if (handle.HasValue)
            {
                return handle.Value switch
                {
                    FrameHandle.Top or FrameHandle.Bottom => CursorKind.ResizeVertical,
                    FrameHandle.Left or FrameHandle.Right => CursorKind.ResizeHorizontal,
                    FrameHandle.TopLeft or FrameHandle.BottomRight => CursorKind.ResizeDiagonalDown,
                    _ => CursorKind.ResizeDiagonalUp,
                };
            }

            if (!overFrame)
            {
                return CursorKind.Default;
            }

            return locked ? CursorKind.NotAllowed : CursorKind.Move;
        }

        /// <summary>
        /// Size and position in output pixels, such as <c>1280 × 720 · 320, 180</c>. Numbers only, so it
        /// reads the same in every language.
        /// </summary>
        public static string Readout(NormalizedRect placement, CanvasSize canvas)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} × {1} · {2}, {3}",
                Math.Round(placement.Width * canvas.Width),
                Math.Round(placement.Height * canvas.Height),
                Math.Round(placement.X * canvas.Width),
                Math.Round(placement.Y * canvas.Height));
        }

        /// <summary>The readout goes below the frame when the frame touches the top of the stream screen.</summary>
        public static bool ReadoutBelow(NormalizedRect placement)
        {
            return placement.Y < 0.08;
        }

        /// <summary>
        /// The frame Tab selects next, in front-to-back order, or -1 to let focus leave the canvas after
        /// the last one (and before the first one with Shift+Tab).
        /// </summary>
        public static int NextSelection(int current, int count, bool backwards)
        {
            if (count == 0)
            {
                return -1;
            }

            if (current < 0)
            {
                return backwards ? count - 1 : 0;
            }

            int next = backwards ? current - 1 : current + 1;
            return next < 0 || next >= count ? -1 : next;
        }

        /// <summary>Indices of the layouts from front to back.</summary>
        public static int[] FrontToBack(IReadOnlyList<FrameLayout> layouts)
        {
            var indices = new int[layouts.Count];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            // Stable: equal orders keep the later one in front, as FrameAt does.
            Array.Sort(indices, (a, b) =>
            {
                int byOrder = layouts[b].Order.CompareTo(layouts[a].Order);
                return byOrder != 0 ? byOrder : b.CompareTo(a);
            });
            return indices;
        }

        private static bool IsCorner(FrameHandle handle)
        {
            return handle == FrameHandle.TopLeft || handle == FrameHandle.TopRight
                || handle == FrameHandle.BottomLeft || handle == FrameHandle.BottomRight;
        }
    }
}
