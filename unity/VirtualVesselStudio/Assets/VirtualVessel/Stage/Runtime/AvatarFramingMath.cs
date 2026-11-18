using System;
using UnityEngine;
using VirtualVessel.Core.Geometry;
using VirtualVessel.Video;

namespace VirtualVessel.Stage
{
    /// <summary>
    /// How the Avatar Camera shoots the avatar so the part of it the avatar frame shows fills the frame (Stage
    /// detailed design 7.2). All functions are pure.
    /// </summary>
    public static class AvatarFramingMath
    {
        /// <summary>The square in front of the avatar, the frame's whole video, as a multiple of the avatar's height.</summary>
        public const float SquarePerHeight = 1.1f;

        /// <summary>How far in front of the avatar the camera stands, in meters.</summary>
        public const float Distance = 2.0f;

        /// <summary>The frame's whole video is a square.</summary>
        public const double SourceAspect = 1.0;

        /// <summary>The part shown without a saved placement: the upper body, as the camera showed it before.</summary>
        public static readonly NormalizedRect DefaultCrop = new NormalizedRect(0.21, 0, 0.58, 0.58);

        /// <summary>
        /// The camera for <paramref name="layout"/>, for an avatar of <paramref name="height"/> meters standing at
        /// <paramref name="spawn"/> and facing its +Z, on a stream <paramref name="streamAspect"/> wide per unit high.
        /// Null when the values cannot make one.
        /// </summary>
        public static AvatarFraming? Framing(Pose spawn, float height, FrameLayout layout, double streamAspect)
        {
            NormalizedRect crop = layout.Crop;
            NormalizedRect frame = layout.Placement;
            if (!(height > 0f) || !(streamAspect > 0) || !(crop.Height > 0) || !(crop.Width > 0) || !(frame.Height > 0))
            {
                return null;
            }

            double side = SquarePerHeight * height;

            // The part shown, across and up from the avatar's feet, as the camera sees it: x to its right.
            double left = (crop.Left - 0.5) * side;
            double right = (crop.Right - 0.5) * side;
            double top = (1 - crop.Top) * side;
            double bottom = (1 - crop.Bottom) * side;

            // Level with the middle of the part shown, so a close shot of the face looks straight at it.
            double middle = (top + bottom) / 2;

            // The whole stream at the avatar's distance: the part's height fills the frame's height, and the width follows.
            double streamHeight = (top - bottom) / frame.Height;
            double streamWidth = streamHeight * streamAspect;

            // Where the stream's edges are, around the part's center, so that center lands on the frame's center.
            // The stream's y runs down from its top; the camera's runs up.
            double centerX = (left + right) / 2;
            double streamLeft = centerX - frame.CenterX * streamWidth;
            double streamTop = frame.CenterY * streamHeight;

            Vector3 facing = spawn.rotation * Vector3.forward;
            Vector3 position = spawn.position + facing * Distance + Vector3.up * (float)middle;
            Quaternion rotation = Quaternion.LookRotation(-facing, Vector3.up);
            return new AvatarFraming(
                position,
                rotation,
                (float)(streamLeft / Distance),
                (float)((streamLeft + streamWidth) / Distance),
                (float)((streamTop - streamHeight) / Distance),
                (float)(streamTop / Distance));
        }

        /// <summary>
        /// The placement without a saved one: <see cref="DefaultCrop"/> the stream's full height, in the middle, close
        /// to how the avatar was shown before the frame.
        /// </summary>
        public static FrameLayout DefaultLayout(double streamAspect)
        {
            double aspect = streamAspect > 0 ? streamAspect : 16.0 / 9.0;
            double width = Math.Min(1.0, DefaultCrop.Width / DefaultCrop.Height * SourceAspect / aspect);
            return new FrameLayout(new NormalizedRect((1 - width) / 2, 0, width, 1), DefaultCrop, order: AvatarFrame.FrontOrder);
        }
    }
}
