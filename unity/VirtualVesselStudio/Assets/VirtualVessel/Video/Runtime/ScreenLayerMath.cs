using UnityEngine;

namespace VirtualVessel.Video
{
    /// <summary>The coordinates of the flat video layer (video detailed design 7.3). Pure functions.</summary>
    public static class ScreenLayerMath
    {
        /// <summary>Half the orthographic height: the screen camera sees a height of 1.</summary>
        public const float OrthographicSize = 0.5f;

        /// <summary>How far in front of the screen camera the layer root sits.</summary>
        public const float RootDistance = 1f;

        /// <summary>
        /// The root's position and scale so that its local (0, 0) is the stream screen's top-left and
        /// (1, 1) its bottom-right, for a camera at the origin looking along +Z.
        /// </summary>
        public static (Vector3 Position, Vector3 Scale) RootPlacement(float aspect)
        {
            return (new Vector3(-aspect * 0.5f, OrthographicSize, RootDistance), new Vector3(aspect, -1f, 1f));
        }

        /// <summary>Where a point of the layer lands in the stream video, in pixels from its bottom-left, as Unity reads textures.</summary>
        public static Vector2 ToPixels(Vector2 layerPoint, VideoSize size)
        {
            return new Vector2(layerPoint.x * size.Width, (1f - layerPoint.y) * size.Height);
        }
    }
}
