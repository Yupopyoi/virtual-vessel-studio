using UnityEngine;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// The head's turn from the tracked face (avatar detailed design 7.1). Person space (Tracking) and
    /// the avatar root's space both have +X on the right, +Y up, and +Z in front, so a rotation carries
    /// over as it is, and only mirroring changes it. Pure.
    /// </summary>
    public static class HeadPose
    {
        /// <summary>The neck's share of the head's turn; the head takes the rest (avatar detailed design 7.1).</summary>
        public const float NeckShare = 0.4f;

        /// <summary>
        /// The largest turn passed on. Beyond it the face is mostly out of the camera's view and the
        /// tracked rotation is unreliable; a head turned further would also look broken.
        /// </summary>
        public const float MaxDegrees = 75f;

        /// <summary>
        /// The same turn seen in a mirror: reflected through the plane X = 0, so turning right becomes
        /// turning left, and nodding stays nodding (avatar detailed design 6.6).
        /// </summary>
        public static Quaternion Mirror(Quaternion rotation)
        {
            return new Quaternion(rotation.x, -rotation.y, -rotation.z, rotation.w);
        }

        /// <summary>The same axis, turned at most <paramref name="maxDegrees"/>.</summary>
        public static Quaternion Limit(Quaternion rotation, float maxDegrees)
        {
            rotation.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
            {
                angle -= 360f;
            }

            if (Mathf.Abs(angle) <= maxDegrees || axis.sqrMagnitude < 1e-12f)
            {
                return rotation;
            }

            return Quaternion.AngleAxis(Mathf.Sign(angle) * maxDegrees, axis);
        }

        /// <summary>The head's rotation in the avatar root's space from the tracked one in person space.</summary>
        public static Quaternion FromTracking(Quaternion tracked, bool mirror)
        {
            Quaternion head = mirror ? Mirror(tracked) : tracked;
            return Limit(Normalized(head), MaxDegrees);
        }

        /// <summary>
        /// The neck's rotation in the root's space when the top of the torso is <paramref name="torsoTop"/>
        /// and the head is <paramref name="head"/>: a share of the way from the torso to the head. The head
        /// bone keeps <paramref name="head"/> itself, so it turns by the rest relative to the neck.
        /// </summary>
        public static Quaternion Neck(Quaternion torsoTop, Quaternion head)
        {
            return Quaternion.Slerp(torsoTop, head, NeckShare);
        }

        private static Quaternion Normalized(Quaternion q)
        {
            float length = Mathf.Sqrt((q.x * q.x) + (q.y * q.y) + (q.z * q.z) + (q.w * q.w));
            return length > 1e-6f ? new Quaternion(q.x / length, q.y / length, q.z / length, q.w / length) : Quaternion.identity;
        }
    }
}
