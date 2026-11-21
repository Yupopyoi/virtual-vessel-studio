using UnityEngine;

namespace VirtualVessel.Video
{
    /// <summary>
    /// How the Avatar Camera shoots the avatar (video detailed design 7.6): from where, facing which way, and a
    /// perspective projection whose center may be shifted, given by its edges at distance 1 in front of the camera.
    /// </summary>
    public readonly struct AvatarFraming
    {
        public AvatarFraming(Vector3 position, Quaternion rotation, float left, float right, float bottom, float top)
        {
            Position = position;
            Rotation = rotation;
            Left = left;
            Right = right;
            Bottom = bottom;
            Top = top;
        }

        /// <summary>The camera's world position.</summary>
        public Vector3 Position { get; }

        /// <summary>The camera's world rotation; it looks along its +Z.</summary>
        public Quaternion Rotation { get; }

        public float Left { get; }

        public float Right { get; }

        public float Bottom { get; }

        public float Top { get; }

        /// <summary>The projection with these edges, between <paramref name="near"/> and <paramref name="far"/>. Pure.</summary>
        public Matrix4x4 Projection(float near, float far)
        {
            return Matrix4x4.Frustum(Left * near, Right * near, Bottom * near, Top * near, near, far);
        }
    }

    /// <summary>Where the avatar is shot from; Stage sets it from the avatar frame (Stage detailed design 7.2).</summary>
    public interface IAvatarCamera
    {
        /// <summary>Shoots the avatar as <paramref name="framing"/> says; null follows the Main Camera (video detailed design 7.2).</summary>
        void SetFraming(AvatarFraming? framing);
    }
}
