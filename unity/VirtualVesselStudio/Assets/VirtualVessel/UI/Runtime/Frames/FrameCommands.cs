using System.Collections.Generic;
using VirtualVessel.Core.Geometry;

namespace VirtualVessel.UI.Frames
{
    /// <summary>How a frame moves in the stacking order.</summary>
    public enum StackMove
    {
        Forward,
        Backward,
        ToFront,
        ToBack,
    }

    /// <summary>
    /// The layout changes behind the frame menu and its shortcuts (UI foundation detailed design 9.4).
    /// Pure; the canvas records and commits the results.
    /// </summary>
    public static class FrameCommands
    {
        public static FrameLayout ToggleFlip(FrameLayout layout)
        {
            return layout.WithFlipHorizontal(!layout.FlipHorizontal);
        }

        public static FrameLayout ToggleLock(FrameLayout layout)
        {
            return layout.WithLocked(!layout.Locked);
        }

        /// <summary>Turning it off brings a frame that is outside back onto the stream screen.</summary>
        public static FrameLayout ToggleAllowOutside(FrameLayout layout)
        {
            bool allow = !layout.AllowOutside;
            return layout.WithAllowOutside(allow).WithPlacement(FrameEditMath.Move(layout.Placement, 0, 0, allow));
        }

        /// <param name="sourceAspect">Width / height of the whole video in pixels.</param>
        public static FrameLayout Fit(FrameLayout layout, double sourceAspect, CanvasSize canvas)
        {
            return layout.WithPlacement(FrameEditMath.Fit(layout.Crop, sourceAspect, canvas));
        }

        /// <param name="sourceAspect">Width / height of the whole video in pixels.</param>
        public static FrameLayout Fill(FrameLayout layout, double sourceAspect, CanvasSize canvas)
        {
            return layout.WithPlacement(FrameEditMath.Fill(layout.Crop, sourceAspect, canvas));
        }

        public static FrameLayout Center(FrameLayout layout)
        {
            return layout.WithPlacement(FrameEditMath.Center(layout.Placement));
        }

        public static FrameLayout ResetCrop(FrameLayout layout)
        {
            return FrameEditMath.Uncrop(layout);
        }

        /// <summary>
        /// The stacking order after moving one frame: one step forward or backward, or all the way to the
        /// front or back. Returns the new <see cref="FrameLayout.Order"/> of every frame, renumbered from
        /// 0 at the back, or the current orders when the frame cannot move that way.
        /// </summary>
        public static int[] Restack(IReadOnlyList<FrameLayout> layouts, int index, StackMove move)
        {
            int[] frontToBack = FrameCanvasMath.FrontToBack(layouts);
            int position = System.Array.IndexOf(frontToBack, index);
            int last = frontToBack.Length - 1;
            int target = move switch
            {
                StackMove.Forward => System.Math.Max(0, position - 1),
                StackMove.Backward => System.Math.Min(last, position + 1),
                StackMove.ToFront => 0,
                _ => last,
            };

            return MoveTo(layouts, index, target);
        }

        /// <summary>
        /// The stacking order after moving one frame to <paramref name="target"/> in the front-to-back
        /// order (0 is the front), as dragging a row of the layer list does. Returns the new
        /// <see cref="FrameLayout.Order"/> of every frame, renumbered from 0 at the back, or the current
        /// orders when the frame is already there.
        /// </summary>
        public static int[] MoveTo(IReadOnlyList<FrameLayout> layouts, int index, int target)
        {
            int[] frontToBack = FrameCanvasMath.FrontToBack(layouts);
            int position = System.Array.IndexOf(frontToBack, index);
            int last = frontToBack.Length - 1;
            target = System.Math.Max(0, System.Math.Min(last, target));

            var orders = new int[layouts.Count];
            if (target == position)
            {
                for (int i = 0; i < orders.Length; i++)
                {
                    orders[i] = layouts[i].Order;
                }

                return orders;
            }

            var stack = new List<int>(frontToBack);
            stack.RemoveAt(position);
            stack.Insert(target, index);
            for (int k = 0; k < stack.Count; k++)
            {
                orders[stack[k]] = last - k;
            }

            return orders;
        }

        /// <summary>Whether a frame at <paramref name="position"/> (0 is the front) of <paramref name="count"/> can move.</summary>
        public static bool CanRestack(int position, int count, StackMove move)
        {
            bool front = move == StackMove.Forward || move == StackMove.ToFront;
            return count > 1 && (front ? position > 0 : position < count - 1);
        }
    }
}
