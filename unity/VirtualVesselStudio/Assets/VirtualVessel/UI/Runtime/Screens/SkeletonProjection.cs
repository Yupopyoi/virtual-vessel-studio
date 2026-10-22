using UnityEngine;
using VirtualVessel.Avatar;
using VirtualVessel.Tracking;

namespace VirtualVessel.UI.Screens
{
    /// <summary>A line of the skeleton comparison, in meters on a view's plane.</summary>
    public readonly struct SkeletonSegment
    {
        public SkeletonSegment(Vector2 from, Vector2 to, bool avatar)
        {
            From = from;
            To = to;
            Avatar = avatar;
        }

        public Vector2 From { get; }

        public Vector2 To { get; }

        /// <summary>The avatar's bone (drawn dashed) rather than the tracked one (drawn solid).</summary>
        public bool Avatar { get; }
    }

    /// <summary>Which plane the skeleton is seen on.</summary>
    public enum SkeletonPlane
    {
        /// <summary>From the front: X to the right, Y up.</summary>
        Front,

        /// <summary>From the avatar's right: Z (its front) to the right, Y up.</summary>
        Side,
    }

    /// <summary>
    /// The skeleton comparison's geometry (UI foundation detailed design 7.7): the tracked upper body and the
    /// avatar's, in the avatar root's space and mirrored as the avatar is, so where they differ shows
    /// whether tracking or the retargeting is off. The avatar's arms are drawn from the tracked shoulders
    /// along the avatar's bones with the tracked lengths, so matching directions overlap. Pure.
    /// </summary>
    public static class SkeletonProjection
    {
        /// <summary>The most lines one view has: torso, shoulder line, and two arms of two bones, tracked and avatar.</summary>
        public const int MaxSegments = 12;

        /// <summary>A point of person or root space on <paramref name="plane"/>.</summary>
        public static Vector2 Project(Vector3 point, SkeletonPlane plane)
        {
            return plane == SkeletonPlane.Front ? new Vector2(point.x, point.y) : new Vector2(point.z, point.y);
        }

        /// <summary>
        /// Writes the lines of <paramref name="body"/> and, when given, <paramref name="avatar"/> on
        /// <paramref name="plane"/> into <paramref name="into"/>; returns how many. Writes only the caller's
        /// buffer, reused every refresh.
        /// </summary>
        public static int Segments(BodyPose body, BodyTarget avatar, bool mirror, SkeletonPlane plane, SkeletonSegment[] into)
        {
            if (body == null || !body.IsUsable)
            {
                return 0;
            }

            int count = 0;
            Vector3 hips = PoseRetargeting.Joint(body, BodyJoint.Hips, mirror);
            Vector3 neck = PoseRetargeting.Joint(body, BodyJoint.Neck, mirror);
            Vector3 left = PoseRetargeting.Joint(body, BodyJoint.LeftShoulder, mirror);
            Vector3 right = PoseRetargeting.Joint(body, BodyJoint.RightShoulder, mirror);
            into[count++] = Line(hips, neck, avatar: false, plane);
            into[count++] = Line(left, right, avatar: false, plane);
            count = Arm(body, mirror, left: true, plane, into, count);
            count = Arm(body, mirror, left: false, plane, into, count);
            if (avatar == null)
            {
                return count;
            }

            // The avatar's torso from the tracked hips, its length and shoulder width the tracked ones.
            Quaternion top = avatar.TorsoTop;
            Vector3 avatarNeck = hips + (top * Vector3.up * Vector3.Distance(hips, neck));
            Vector3 half = top * Vector3.right * (Vector3.Distance(left, right) * 0.5f);
            into[count++] = Line(hips, avatarNeck, avatar: true, plane);
            into[count++] = Line(avatarNeck - half, avatarNeck + half, avatar: true, plane);
            count = AvatarArm(body, avatar, mirror, left: true, plane, into, count);
            count = AvatarArm(body, avatar, mirror, left: false, plane, into, count);
            return count;
        }

        /// <summary>How far above level <paramref name="direction"/> points, in degrees.</summary>
        public static float Elevation(Vector3 direction)
        {
            return Mathf.Asin(Mathf.Clamp(direction.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>How far toward the front <paramref name="direction"/> points from the side, in degrees: 0 out sideways, 90 straight forward.</summary>
        public static float Forward(Vector3 direction)
        {
            return Mathf.Atan2(direction.z, Mathf.Abs(direction.x)) * Mathf.Rad2Deg;
        }

        /// <summary>The upper arm's and forearm's directions as tracked, on the avatar's side; false when unseen.</summary>
        public static bool TrackedArm(BodyPose body, bool mirror, bool left, out Vector3 upper, out Vector3 fore)
        {
            upper = Vector3.zero;
            fore = Vector3.zero;
            if (body == null || !body.IsUsable)
            {
                return false;
            }

            Vector3 shoulder = PoseRetargeting.Joint(body, left ? BodyJoint.LeftShoulder : BodyJoint.RightShoulder, mirror);
            Vector3 elbow = PoseRetargeting.Joint(body, left ? BodyJoint.LeftElbow : BodyJoint.RightElbow, mirror);
            Vector3 wrist = PoseRetargeting.Joint(body, left ? BodyJoint.LeftWrist : BodyJoint.RightWrist, mirror);
            upper = elbow - shoulder;
            fore = wrist - elbow;
            return upper.sqrMagnitude > 1e-8f && fore.sqrMagnitude > 1e-8f;
        }

        /// <summary>The avatar's upper arm and forearm directions.</summary>
        public static (Vector3 Upper, Vector3 Fore) AvatarArm(BodyTarget avatar, bool left)
        {
            Vector3 rest = PoseRetargeting.RestDirection(left);
            return (avatar[left ? UpperBodyBone.LeftUpperArm : UpperBodyBone.RightUpperArm] * rest, avatar[left ? UpperBodyBone.LeftLowerArm : UpperBodyBone.RightLowerArm] * rest);
        }

        private static int Arm(BodyPose body, bool mirror, bool left, SkeletonPlane plane, SkeletonSegment[] into, int count)
        {
            Vector3 shoulder = PoseRetargeting.Joint(body, left ? BodyJoint.LeftShoulder : BodyJoint.RightShoulder, mirror);
            Vector3 elbow = PoseRetargeting.Joint(body, left ? BodyJoint.LeftElbow : BodyJoint.RightElbow, mirror);
            Vector3 wrist = PoseRetargeting.Joint(body, left ? BodyJoint.LeftWrist : BodyJoint.RightWrist, mirror);
            into[count++] = Line(shoulder, elbow, avatar: false, plane);
            into[count++] = Line(elbow, wrist, avatar: false, plane);
            return count;
        }

        private static int AvatarArm(BodyPose body, BodyTarget avatar, bool mirror, bool left, SkeletonPlane plane, SkeletonSegment[] into, int count)
        {
            Vector3 shoulder = PoseRetargeting.Joint(body, left ? BodyJoint.LeftShoulder : BodyJoint.RightShoulder, mirror);
            Vector3 elbow = PoseRetargeting.Joint(body, left ? BodyJoint.LeftElbow : BodyJoint.RightElbow, mirror);
            Vector3 wrist = PoseRetargeting.Joint(body, left ? BodyJoint.LeftWrist : BodyJoint.RightWrist, mirror);
            (Vector3 upper, Vector3 fore) = AvatarArm(avatar, left);
            Vector3 avatarElbow = shoulder + (upper * Vector3.Distance(shoulder, elbow));
            Vector3 avatarWrist = avatarElbow + (fore * Vector3.Distance(elbow, wrist));
            into[count++] = Line(shoulder, avatarElbow, avatar: true, plane);
            into[count++] = Line(avatarElbow, avatarWrist, avatar: true, plane);
            return count;
        }

        private static SkeletonSegment Line(Vector3 from, Vector3 to, bool avatar, SkeletonPlane plane)
        {
            return new SkeletonSegment(Project(from, plane), Project(to, plane), avatar);
        }
    }
}
