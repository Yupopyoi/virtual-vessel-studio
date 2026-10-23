using UnityEngine;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// Keeps the avatar's arms out of its head (avatar detailed design 7.6). Only directions are retargeted,
    /// so on an avatar whose head is bigger for its arms than the person's, an arm the person holds beside
    /// or over the head passes through the avatar's. An arm that would is turned the least that clears it:
    /// the whole arm about the shoulder when the upper arm reaches the head, then the forearm about the
    /// elbow when the forearm or the hand does. It turns away from the head's center, so an arm in front of
    /// the face goes further forward and one behind the head further back. Pure: only the caller's buffer
    /// is written, and nothing is allocated (CLAUDE.md 23).
    /// </summary>
    public static class ArmClearance
    {
        /// <summary>The most an arm is turned; past it the arm stays where that turn puts it, so it never jumps.</summary>
        public const float MaxTurnDegrees = 60f;

        // Halvings of the turn: 60° / 2^12 is about 0.015°.
        private const int Halvings = 12;

        /// <summary>
        /// Writes <paramref name="pose"/> into <paramref name="into"/> with the arms turned out of the head;
        /// <paramref name="into"/> may be <paramref name="pose"/>. Without a shape the pose is copied as it is.
        /// </summary>
        /// <param name="neck">The neck's rotation in the root's space; unused when the model has no neck.</param>
        /// <param name="head">The head's rotation in the root's space.</param>
        public static void Apply(AvatarBodyShape shape, BodyTarget pose, Quaternion neck, Quaternion head, BodyTarget into)
        {
            into.CopyFrom(pose);
            if (shape == null)
            {
                return;
            }

            PlacedHead placed = Place(shape, into, neck, head, out Chain top);
            ClearArm(shape, placed, top, left: true, into);
            ClearArm(shape, placed, top, left: false, into);
        }

        /// <summary>Whether either arm of <paramref name="pose"/> reaches into the head; false without a shape. Pure.</summary>
        public static bool Touches(AvatarBodyShape shape, BodyTarget pose, Quaternion neck, Quaternion head)
        {
            if (shape == null)
            {
                return false;
            }

            PlacedHead placed = Place(shape, pose, neck, head, out Chain top);
            return Touches(shape, placed, top, left: true, pose) || Touches(shape, placed, top, left: false, pose);
        }

        /// <summary>
        /// Whether a capsule from <paramref name="from"/> to <paramref name="to"/> of <paramref name="radius"/>
        /// reaches into the head. The head grown by the radius stands in for the capsule's rounded sides. Pure.
        /// </summary>
        public static bool Hits(PlacedHead head, Vector3 from, Vector3 to, float radius)
        {
            Vector3 a = head.ToUnit(from, radius);
            Vector3 b = head.ToUnit(to, radius);
            Vector3 along = b - a;
            float lengthSquared = along.sqrMagnitude;
            float t = lengthSquared > 1e-12f ? Mathf.Clamp01(-Vector3.Dot(a, along) / lengthSquared) : 0f;
            return (a + (along * t)).sqrMagnitude < 1f;
        }

        /// <summary>
        /// The head where <paramref name="pose"/> puts it, and the top of the torso the shoulders hang from:
        /// forward along the chain from the hips, which do not move, each bone where its parent's rotation
        /// carries the rest offset between them.
        /// </summary>
        private static PlacedHead Place(AvatarBodyShape shape, BodyTarget pose, Quaternion neck, Quaternion head, out Chain top)
        {
            var chain = new Chain(shape.Rest(AvatarBone.Hips));
            chain = chain.Then(shape, AvatarBone.Spine, pose[UpperBodyBone.Spine]);
            chain = chain.Then(shape, AvatarBone.Chest, pose[UpperBodyBone.Chest]);
            chain = chain.Then(shape, AvatarBone.UpperChest, pose[UpperBodyBone.UpperChest]);
            top = chain;
            chain = chain.Then(shape, AvatarBone.Neck, neck);
            chain = chain.Then(shape, AvatarBone.Head, head);
            return new PlacedHead(chain.At + (head * shape.Head.Center), head, shape.Head.Radii);
        }

        private static void ClearArm(AvatarBodyShape shape, PlacedHead head, Chain top, bool left, BodyTarget into)
        {
            UpperBodyBone upper = left ? UpperBodyBone.LeftUpperArm : UpperBodyBone.RightUpperArm;
            UpperBodyBone lower = left ? UpperBodyBone.LeftLowerArm : UpperBodyBone.RightLowerArm;
            ArmShape thickness = shape.Arm(left);
            Limb arm = Reach(shape, top, left, into);

            Quaternion turn = Turn(head, arm.Shoulder, arm.UpperArm, thickness.UpperRadius, arm.UpperArm, thickness.UpperRadius);
            into[upper] = turn * into[upper];
            into[lower] = turn * into[lower];
            Vector3 elbow = arm.Shoulder + (turn * arm.UpperArm);
            Vector3 forearm = turn * arm.Forearm;
            into[lower] = Turn(head, elbow, forearm, thickness.LowerRadius, forearm + (turn * arm.Hand), thickness.HandRadius) * into[lower];
        }

        private static bool Touches(AvatarBodyShape shape, PlacedHead head, Chain top, bool left, BodyTarget pose)
        {
            ArmShape thickness = shape.Arm(left);
            Limb arm = Reach(shape, top, left, pose);
            Vector3 elbow = arm.Shoulder + arm.UpperArm;
            Vector3 wrist = elbow + arm.Forearm;
            return Hits(head, arm.Shoulder, elbow, thickness.UpperRadius)
                || Hits(head, elbow, wrist, thickness.LowerRadius)
                || Hits(head, wrist, wrist + arm.Hand, thickness.HandRadius);
        }

        /// <summary>Where an arm starts and the vectors along its upper arm, forearm, and hand.</summary>
        private static Limb Reach(AvatarBodyShape shape, Chain top, bool left, BodyTarget pose)
        {
            AvatarBone upperBone = left ? AvatarBone.LeftUpperArm : AvatarBone.RightUpperArm;
            AvatarBone lowerBone = left ? AvatarBone.LeftLowerArm : AvatarBone.RightLowerArm;
            AvatarBone handBone = left ? AvatarBone.LeftHand : AvatarBone.RightHand;
            Quaternion upper = pose[left ? UpperBodyBone.LeftUpperArm : UpperBodyBone.RightUpperArm];
            Quaternion lower = pose[left ? UpperBodyBone.LeftLowerArm : UpperBodyBone.RightLowerArm];
            Chain shoulder = top.Then(shape, left ? AvatarBone.LeftShoulder : AvatarBone.RightShoulder, pose[left ? UpperBodyBone.LeftShoulder : UpperBodyBone.RightShoulder]);
            Vector3 forearmRest = shape.Rest(handBone) - shape.Rest(lowerBone);

            // The hand follows the forearm until it has its own rotation (step 6).
            return new Limb(
                shoulder.Then(shape, upperBone, upper).At,
                upper * (shape.Rest(lowerBone) - shape.Rest(upperBone)),
                lower * forearmRest,
                lower * (forearmRest.normalized * shape.Arm(left).HandLength));
        }

        /// <summary>
        /// The least turn about <paramref name="pivot"/> that takes two capsules joined end to end out of
        /// the head: the first from the pivot to <paramref name="first"/>, the second on to
        /// <paramref name="second"/> (both from the pivot). None when they are clear or the pivot itself is
        /// in the head, which no turn about it would help.
        /// </summary>
        private static Quaternion Turn(PlacedHead head, Vector3 pivot, Vector3 first, float firstRadius, Vector3 second, float secondRadius)
        {
            if (Hits(head, pivot, pivot, firstRadius) || !HitsTurned(head, pivot, first, firstRadius, second, secondRadius, Quaternion.identity))
            {
                return Quaternion.identity;
            }

            // Away from the head's center, in the plane of the center and the limb.
            Vector3 toCenter = head.Center - pivot;
            Vector3 axis = Vector3.Cross(toCenter, first);
            if (axis.sqrMagnitude < 1e-10f)
            {
                // Pointing straight at the center: any side clears; go up, or sideways when that is the same.
                axis = Vector3.Cross(toCenter, Vector3.up);
                if (axis.sqrMagnitude < 1e-10f)
                {
                    axis = Vector3.right;
                }
            }

            axis.Normalize();
            Quaternion most = Quaternion.AngleAxis(MaxTurnDegrees, axis);
            if (HitsTurned(head, pivot, first, firstRadius, second, secondRadius, most))
            {
                return most;
            }

            float hitting = 0f;
            float clear = MaxTurnDegrees;
            for (int i = 0; i < Halvings; i++)
            {
                float middle = (hitting + clear) * 0.5f;
                if (HitsTurned(head, pivot, first, firstRadius, second, secondRadius, Quaternion.AngleAxis(middle, axis)))
                {
                    hitting = middle;
                }
                else
                {
                    clear = middle;
                }
            }

            return Quaternion.AngleAxis(clear, axis);
        }

        private static bool HitsTurned(PlacedHead head, Vector3 pivot, Vector3 first, float firstRadius, Vector3 second, float secondRadius, Quaternion turn)
        {
            Vector3 joint = pivot + (turn * first);
            return Hits(head, pivot, joint, firstRadius) || Hits(head, joint, pivot + (turn * second), secondRadius);
        }

        /// <summary>The head where it is this frame: its center and rotation in the root's space, and its half sizes.</summary>
        public readonly struct PlacedHead
        {
            private readonly Quaternion _inverse;

            public PlacedHead(Vector3 center, Quaternion rotation, Vector3 radii)
            {
                Center = center;
                _inverse = Quaternion.Inverse(rotation);
                Radii = radii;
            }

            public Vector3 Center { get; }

            public Vector3 Radii { get; }

            /// <summary>A point in the space where the head grown by <paramref name="grow"/> is the unit sphere.</summary>
            public Vector3 ToUnit(Vector3 point, float grow)
            {
                Vector3 local = _inverse * (point - Center);
                return new Vector3(local.x / (Radii.x + grow), local.y / (Radii.y + grow), local.z / (Radii.z + grow));
            }
        }

        private readonly struct Limb
        {
            public Limb(Vector3 shoulder, Vector3 upperArm, Vector3 forearm, Vector3 hand)
            {
                Shoulder = shoulder;
                UpperArm = upperArm;
                Forearm = forearm;
                Hand = hand;
            }

            public Vector3 Shoulder { get; }

            public Vector3 UpperArm { get; }

            public Vector3 Forearm { get; }

            public Vector3 Hand { get; }
        }

        /// <summary>Where the chain has reached: a bone's position, its rest position, and its rotation.</summary>
        private readonly struct Chain
        {
            private readonly Vector3 _rest;
            private readonly Quaternion _rotation;

            public Chain(Vector3 hips)
                : this(hips, hips, Quaternion.identity)
            {
            }

            private Chain(Vector3 at, Vector3 rest, Quaternion rotation)
            {
                At = at;
                _rest = rest;
                _rotation = rotation;
            }

            public Vector3 At { get; }

            /// <summary>The next bone on <paramref name="rotation"/>; the same place when the model lacks it.</summary>
            public Chain Then(AvatarBodyShape shape, AvatarBone bone, Quaternion rotation)
            {
                if (!shape.Has(bone))
                {
                    return this;
                }

                Vector3 rest = shape.Rest(bone);
                return new Chain(At + (_rotation * (rest - _rest)), rest, rotation);
            }
        }
    }
}
