using System;
using UnityEngine;
using VirtualVessel.Core.Geometry;

namespace VirtualVessel.Stage
{
    /// <summary>
    /// From a placement to the quad on the flat video layer (Stage detailed design 6.2). Pure
    /// functions; the layer's local space is 0 to 1 over the stream screen, top-left first.
    /// </summary>
    public static class ScreenPlacementMath
    {
        /// <summary>Depth between stacking steps; the layer camera sees 2 units, so about a thousand steps fit.</summary>
        public const float OrderDepth = 0.001f;

        /// <summary>The quad's local position and scale. The y scale is negative because the layer's y grows downwards.</summary>
        public static (Vector3 Position, Vector3 Scale) Quad(NormalizedRect placement, int order)
        {
            var position = new Vector3((float)(placement.X + (placement.Width * 0.5)), (float)(placement.Y + (placement.Height * 0.5)), -order * OrderDepth);
            return (position, new Vector3((float)placement.Width, (float)-placement.Height, 1f));
        }

        /// <summary>
        /// The texture's scale (xy) and offset (zw) that show <paramref name="crop"/>. Textures start
        /// at the bottom while crops start at the top; a horizontal flip reads the crop right to left.
        /// </summary>
        public static Vector4 TextureScaleOffset(NormalizedRect crop, bool flipHorizontal)
        {
            float bottom = (float)(1 - crop.Bottom);
            return flipHorizontal
                ? new Vector4((float)-crop.Width, (float)crop.Height, (float)crop.Right, bottom)
                : new Vector4((float)crop.Width, (float)crop.Height, (float)crop.Left, bottom);
        }

        /// <summary>Whether a placement can be shown: finite, with a size, and a crop inside the video.</summary>
        public static bool IsUsable(FrameLayout layout)
        {
            NormalizedRect p = layout.Placement;
            NormalizedRect c = layout.Crop;
            const double Epsilon = 1e-9;
            return IsFinite(p) && IsFinite(c)
                && p.Width > 0 && p.Height > 0
                && c.Width > 0 && c.Height > 0
                && c.Left >= -Epsilon && c.Top >= -Epsilon && c.Right <= 1 + Epsilon && c.Bottom <= 1 + Epsilon;
        }

        private static bool IsFinite(NormalizedRect rect)
        {
            return IsFinite(rect.X) && IsFinite(rect.Y) && IsFinite(rect.Width) && IsFinite(rect.Height);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    /// <summary>Placements for the first use (Stage detailed design 6.5).</summary>
    public static class StageDefaults
    {
        public static FrameLayout For(string screenId)
        {
            return screenId switch
            {
                "game" => new FrameLayout(NormalizedRect.Full, NormalizedRect.Full, order: 0),
                "subscreen" => new FrameLayout(new NormalizedRect(0.66, 0.04, 0.3, 0.3), NormalizedRect.Full, order: 1),
                _ => new FrameLayout(new NormalizedRect(0.25, 0.25, 0.5, 0.5), NormalizedRect.Full, order: 2),
            };
        }

        /// <summary>
        /// The default spawn point: the origin, turned to face the Main Camera, which looks along +Z from
        /// in front of it (video detailed design 7.5).
        /// </summary>
        public static Pose AvatarSpawn => new Pose(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));

        /// <summary>
        /// The default light's direction: from above the camera and a little to its left, so the face is
        /// lit from the front with soft shading toward one side.
        /// </summary>
        public static Quaternion LightRotation => Quaternion.Euler(35f, 15f, 0f);

        /// <summary>
        /// Where the provisional backdrop stands: 2 m behind the avatar, at the camera's height, facing it
        /// (video detailed design 7.5: from (0, 1.25, -1.9) with a vertical view of 30°).
        /// </summary>
        public static Vector3 BackdropPosition => new Vector3(0f, 1.25f, 2f);

        /// <summary>
        /// The backdrop's size: the camera's view at its distance, 2.09 × 3.72 m, with about 15% to spare,
        /// in the stream's 16:9 shape.
        /// </summary>
        public static Vector2 BackdropSize => new Vector2(2.4f * 16f / 9f, 2.4f);

        /// <summary>The string table key of a capture source's name on the stream screen.</summary>
        public static string NameKey(string screenId)
        {
            if (screenId == null)
            {
                throw new ArgumentNullException(nameof(screenId));
            }

            return "stage.screen." + screenId;
        }
    }
}
