using UnityEngine;
using VirtualVessel.Tracking;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// The upper body's bone rotations from a tracked body (avatar detailed design 7.1, 7.2; system design
    /// 7.6): the torso from the shoulder line and the neck, shared among Spine, Chest, and UpperChest; the
    /// shoulders raised a little with the arms; and each arm from the shoulder, elbow, and wrist. Only
    /// directions are used, never positions, so a person and an avatar of different builds point their
    /// bones alike. Pure: everything comes from the arguments, and only the caller's buffer is written.
    /// </summary>
    /// <remarks>
    /// Person space (Tracking) and the avatar root's space share their axes (+X right, +Y up, +Z front),
    /// so a direction carries over as it is; mirroring reflects through X = 0 and swaps the sides. In the
    /// reference pose (UniVRM's control rig) every bone's rotation is identity, the arms pointing sideways.
    /// </remarks>
    public static class PoseRetargeting
    {
        /// <summary>The torso's turn given to Spine, and to Spine and Chest together; UpperChest takes it all.</summary>
        public const float SpineShare = 0.30f;
        public const float ChestShare = 0.65f;

        /// <summary>The torso stops here, so a misread does not bend it unnaturally.</summary>
        public const float MaxTiltDegrees = 30f;
        public const float MaxLeanDegrees = 25f;
        public const float MaxTwistDegrees = 40f;

        /// <summary>The share of an arm raised above level that the shoulder takes, up to a limit.</summary>
        public const float ShoulderShare = 0.25f;
        public const float MaxShoulderRaiseDegrees = 20f;

        /// <summary>A joint less visible than this (MediaPipe's visibility) does not move the avatar.</summary>
        public const float MinVisibility = 0.5f;

        // How bent an arm must be for its own bend to set the upper arm's twist (the sine of the angle
        // between upper arm and forearm). A nearly straight arm's bend says nothing about the twist, so
        // below the first value the twist follows the torso, and between the two it blends.
        private const float StraightBelow = 0.10f;
        private const float BentAbove = 0.35f;

        /// <summary>
        /// Writes the upper body's rotations for <paramref name="body"/> into <paramref name="into"/>. Parts
        /// not seen keep the rest: the torso straight, an arm lowered by <paramref name="armDownDegrees"/>.
        /// </summary>
        /// <param name="hasUpperChest">Whether the model has an UpperChest; without it Chest takes the whole turn.</param>
        public static void Retarget(BodyPose body, bool mirror, bool hasUpperChest, float armDownDegrees, BodyTarget into)
        {
            bool usable = body != null && body.IsUsable;
            into.TorsoSeen = usable && Visible(body, BodyJoint.LeftShoulder) && Visible(body, BodyJoint.RightShoulder);
            Quaternion torso = into.TorsoSeen
                ? LimitTorso(Torso(Joint(body, BodyJoint.LeftShoulder, mirror), Joint(body, BodyJoint.RightShoulder, mirror), Joint(body, BodyJoint.Neck, mirror), Joint(body, BodyJoint.Hips, mirror)))
                : Quaternion.identity;
            into[UpperBodyBone.Spine] = Quaternion.Slerp(Quaternion.identity, torso, SpineShare);
            into[UpperBodyBone.Chest] = hasUpperChest ? Quaternion.Slerp(Quaternion.identity, torso, ChestShare) : torso;
            into[UpperBodyBone.UpperChest] = torso;

            (bool leftArm, bool leftForearm) = WriteArm(body, mirror, left: true, torso, into.TorsoSeen, armDownDegrees, into);
            (bool rightArm, bool rightForearm) = WriteArm(body, mirror, left: false, torso, into.TorsoSeen, armDownDegrees, into);
            into.LeftArmSeen = leftArm;
            into.LeftForearmSeen = leftForearm;
            into.RightArmSeen = rightArm;
            into.RightForearmSeen = rightForearm;
        }

        /// <summary>
        /// The torso's rotation from the shoulders, the neck, and the hips: its right is the shoulder line
        /// and its up the line from the hips to the neck, so it tilts, leans, and twists with them.
        /// </summary>
        public static Quaternion Torso(Vector3 leftShoulder, Vector3 rightShoulder, Vector3 neck, Vector3 hips)
        {
            Vector3 right = rightShoulder - leftShoulder;
            Vector3 up = neck - hips;
            Vector3 forward = Vector3.Cross(right, up);
            if (right.sqrMagnitude < 1e-8f || up.sqrMagnitude < 1e-8f || forward.sqrMagnitude < 1e-8f)
            {
                return Quaternion.identity;
            }

            // The up kept is the one at right angles to the shoulder line, so the shoulders stay level with the torso.
            return Quaternion.LookRotation(forward, Vector3.Cross(forward, right));
        }

        /// <summary>The torso within its limits: tilt (about Z), lean (about X), and twist (about Y).</summary>
        public static Quaternion LimitTorso(Quaternion torso)
        {
            Vector3 euler = torso.eulerAngles;
            return Quaternion.Euler(
                Mathf.Clamp(Signed(euler.x), -MaxLeanDegrees, MaxLeanDegrees),
                Mathf.Clamp(Signed(euler.y), -MaxTwistDegrees, MaxTwistDegrees),
                Mathf.Clamp(Signed(euler.z), -MaxTiltDegrees, MaxTiltDegrees));
        }

        /// <summary>
        /// The upper arm's and forearm's rotations from the shoulder, elbow, and wrist. The upper arm points
        /// at the elbow, twisted so the elbow bends in the plane it is seen bending in; a nearly straight arm
        /// keeps the twist of the shortest swing from the torso. The forearm points at the wrist and keeps
        /// the upper arm's twist (the palm's twist comes with the hands, step 6).
        /// </summary>
        public static (Quaternion Upper, Quaternion Lower) Arm(bool left, Vector3 shoulder, Vector3 elbow, Vector3 wrist, Quaternion torso)
        {
            Vector3 restDirection = RestDirection(left);
            Vector3 restNormal = RestBendNormal(left);
            Vector3 upper = elbow - shoulder;
            if (upper.sqrMagnitude < 1e-8f)
            {
                return (torso, torso);
            }

            upper.Normalize();
            Vector3 fore = wrist - elbow;
            fore = fore.sqrMagnitude < 1e-8f ? upper : fore.normalized;

            // The twist of the shortest swing from where the torso holds the arm: always at right angles
            // to the upper arm, so it serves when the bend says nothing.
            Vector3 swungNormal = Quaternion.FromToRotation(torso * restDirection, upper) * (torso * restNormal);
            Vector3 bend = Vector3.Cross(upper, fore);
            float bent = Mathf.Clamp01((bend.magnitude - StraightBelow) / (BentAbove - StraightBelow));
            Vector3 normal = bent > 0f ? Vector3.Slerp(swungNormal, bend.normalized, bent) : swungNormal;
            normal = Vector3.ProjectOnPlane(normal, upper);
            if (normal.sqrMagnitude < 1e-8f)
            {
                normal = swungNormal;
            }

            Quaternion upperRotation = Quaternion.LookRotation(upper, normal) * Quaternion.Inverse(Quaternion.LookRotation(restDirection, restNormal));
            Quaternion lowerRotation = Quaternion.FromToRotation(upperRotation * restDirection, fore) * upperRotation;
            return (upperRotation, lowerRotation);
        }

        /// <summary>
        /// How far the shoulder rises for an upper arm at <paramref name="upperArm"/> (its rotation in the
        /// root's space) on a torso at <paramref name="torso"/>: a share of how far the arm is raised above
        /// level, within the limit; 0 for an arm at or below level.
        /// </summary>
        public static float ShoulderRaise(bool left, Quaternion upperArm, Quaternion torso)
        {
            Vector3 direction = upperArm * RestDirection(left);
            float elevation = Mathf.Asin(Mathf.Clamp(Vector3.Dot(direction, torso * Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;
            return Mathf.Clamp(elevation * ShoulderShare, 0f, MaxShoulderRaiseDegrees);
        }

        /// <summary>The shoulder's rotation raised by <paramref name="degrees"/> on the torso.</summary>
        public static Quaternion Shoulder(bool left, float degrees, Quaternion torso)
        {
            // Turning about the torso's front lifts the right shoulder (+X) toward +Y with a positive angle,
            // and the left one (-X) with a negative angle.
            return Quaternion.AngleAxis(left ? -degrees : degrees, torso * Vector3.forward) * torso;
        }

        /// <summary>
        /// The arm while its wrist is hidden (behind the head, for example): the upper arm swung the
        /// shortest way from <paramref name="previousUpper"/> to where <paramref name="upper"/> points,
        /// keeping its twist, and the forearm keeping its last bend on it. Without the wrist the bend's
        /// plane is unknown, so a twist worked out afresh would jump.
        /// </summary>
        public static (Quaternion Upper, Quaternion Lower) WithoutWrist(bool left, Quaternion upper, Quaternion previousUpper, Quaternion previousLower)
        {
            Vector3 restDirection = RestDirection(left);
            Quaternion swung = Quaternion.FromToRotation(previousUpper * restDirection, upper * restDirection) * previousUpper;
            return (swung, swung * (Quaternion.Inverse(previousUpper) * previousLower));
        }

        /// <summary>Where an arm points in the reference pose: the avatar's left arm to -X.</summary>
        public static Vector3 RestDirection(bool left)
        {
            return left ? Vector3.left : Vector3.right;
        }

        /// <summary>
        /// The bend plane's normal of an arm in the reference pose with the elbow bent forward (the
        /// forearm toward +Z): the upper arm's direction crossed with the forearm's.
        /// </summary>
        public static Vector3 RestBendNormal(bool left)
        {
            return left ? Vector3.up : Vector3.down;
        }

        /// <summary>
        /// A joint in the avatar root's space: the same joint, or in a mirror the person's other side
        /// reflected through X = 0, so the person's right hand moves the avatar's left one.
        /// </summary>
        public static Vector3 Joint(BodyPose body, BodyJoint joint, bool mirror)
        {
            if (!mirror)
            {
                return body[joint];
            }

            Vector3 position = body[Opposite(joint)];
            return new Vector3(-position.x, position.y, position.z);
        }

        /// <summary>The same joint on the other side; a joint in the middle is itself.</summary>
        public static BodyJoint Opposite(BodyJoint joint)
        {
            switch (joint)
            {
                case BodyJoint.LeftEar: return BodyJoint.RightEar;
                case BodyJoint.RightEar: return BodyJoint.LeftEar;
                case BodyJoint.LeftShoulder: return BodyJoint.RightShoulder;
                case BodyJoint.RightShoulder: return BodyJoint.LeftShoulder;
                case BodyJoint.LeftElbow: return BodyJoint.RightElbow;
                case BodyJoint.RightElbow: return BodyJoint.LeftElbow;
                case BodyJoint.LeftWrist: return BodyJoint.RightWrist;
                case BodyJoint.RightWrist: return BodyJoint.LeftWrist;
                case BodyJoint.LeftHip: return BodyJoint.RightHip;
                case BodyJoint.RightHip: return BodyJoint.LeftHip;
                case BodyJoint.LeftKnee: return BodyJoint.RightKnee;
                case BodyJoint.RightKnee: return BodyJoint.LeftKnee;
                case BodyJoint.LeftAnkle: return BodyJoint.RightAnkle;
                case BodyJoint.RightAnkle: return BodyJoint.LeftAnkle;
                case BodyJoint.LeftFoot: return BodyJoint.RightFoot;
                case BodyJoint.RightFoot: return BodyJoint.LeftFoot;
                default: return joint;
            }
        }

        /// <summary>
        /// Writes one arm and its shoulder; returns whether the upper arm (shoulder and elbow) and the
        /// forearm (the wrist too) were seen. Without the wrist the forearm is left straight here; the
        /// motion keeps its last bend (<see cref="WithoutWrist"/>).
        /// </summary>
        private static (bool Arm, bool Forearm) WriteArm(BodyPose body, bool mirror, bool left, Quaternion torso, bool torsoSeen, float armDownDegrees, BodyTarget into)
        {
            BodyJoint shoulder = left ? BodyJoint.LeftShoulder : BodyJoint.RightShoulder;
            BodyJoint elbow = left ? BodyJoint.LeftElbow : BodyJoint.RightElbow;
            BodyJoint wrist = left ? BodyJoint.LeftWrist : BodyJoint.RightWrist;
            UpperBodyBone shoulderBone = left ? UpperBodyBone.LeftShoulder : UpperBodyBone.RightShoulder;
            UpperBodyBone upperBone = left ? UpperBodyBone.LeftUpperArm : UpperBodyBone.RightUpperArm;
            UpperBodyBone lowerBone = left ? UpperBodyBone.LeftLowerArm : UpperBodyBone.RightLowerArm;

            // Visibility is the person's side; in a mirror that is the avatar's other side.
            bool seen = torsoSeen
                && Visible(body, mirror ? Opposite(shoulder) : shoulder)
                && Visible(body, mirror ? Opposite(elbow) : elbow);
            if (!seen)
            {
                Quaternion lowered = torso * AvatarRestPose.UpperArm(left, armDownDegrees);
                into[shoulderBone] = torso;
                into[upperBone] = lowered;
                into[lowerBone] = lowered;
                return (false, false);
            }

            // A hand behind the head hides the wrist while the shoulder and the elbow stay in view (the
            // user's recording, 2026-10-22: the wrist seen 28% of the time, the elbow always).
            bool forearm = Visible(body, mirror ? Opposite(wrist) : wrist);
            Vector3 shoulderAt = Joint(body, shoulder, mirror);
            Vector3 elbowAt = Joint(body, elbow, mirror);
            Vector3 wristAt = forearm ? Joint(body, wrist, mirror) : elbowAt + (elbowAt - shoulderAt);
            (Quaternion upper, Quaternion lower) = Arm(left, shoulderAt, elbowAt, wristAt, torso);
            into[shoulderBone] = Shoulder(left, ShoulderRaise(left, upper, torso), torso);
            into[upperBone] = upper;
            into[lowerBone] = lower;
            return (true, forearm);
        }

        private static bool Visible(BodyPose body, BodyJoint joint)
        {
            return body.JointConfidence[(int)joint] >= MinVisibility;
        }

        private static float Signed(float degrees)
        {
            return degrees > 180f ? degrees - 360f : degrees;
        }
    }
}
