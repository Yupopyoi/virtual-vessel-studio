using UnityEngine;
using VirtualVessel.Tracking;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// The fingers from a tracked hand (avatar detailed design 7.5; tracking detailed design 8.4): the
    /// angles between neighboring finger bones, which depend on neither the hand's size nor its distance and
    /// suffer less from misread depth than positions, then the avatar's finger bones turned by those angles
    /// about the model's own axes. Pure: everything comes from the arguments, and only the caller's buffers
    /// are written.
    /// </summary>
    public static class FingerRetargeting
    {
        /// <summary>How far a finger bends at its base: from straight up to a right angle.</summary>
        public const float MaxBaseBend = 90f;

        /// <summary>How far a finger bends at its middle and end joints.</summary>
        public const float MaxMiddleBend = 100f;
        public const float MaxEndBend = 80f;

        /// <summary>
        /// The tracked hand reads its bends small: MediaPipe flattens depth, and fingers curled toward the
        /// camera lose most of their bend. On the user's recordings the most curled hands read 60–84° at the
        /// base and 54–78° at the middle joint (medians per finger), where a fist bends about 90° and 100°,
        /// and the most open hands still read 15–36° and 8–36° (2026-10-24). So the bend shown is the bend
        /// read past <see cref="OpenBend"/>, scaled by <see cref="BaseGain"/> or <see cref="MiddleGain"/>.
        /// </summary>
        public const float OpenBend = 10f;
        public const float BaseGain = 1.5f;
        public const float MiddleGain = 1.8f;

        /// <summary>
        /// The end joint's share of the middle one's bend. A finger's end joint bends with its middle one,
        /// and the end joint read on its own was noise: an open index finger read a median of 42° there.
        /// </summary>
        public const float EndShare = 2f / 3f;

        /// <summary>How far a finger spreads either way at its base.</summary>
        public const float MaxSpread = 15f;

        /// <summary>
        /// The angle between the thumb's and the index finger's metacarpals of an open hand; the thumb's
        /// base folds across the palm by as much as it is closer than this, up to <see cref="MaxThumbFold"/>.
        /// On the user's recordings the angle was 11–52° (5–95%), with a median of 27°.
        /// </summary>
        public const float OpenThumbDegrees = 30f;
        public const float MaxThumbFold = 25f;

        /// <summary>How far the thumb bends at its middle and end joints.</summary>
        public const float MaxThumbBend = 60f;
        public const float MaxThumbEndBend = 80f;

        /// <summary>The thumb's middle and end bends shown, as for the other fingers.</summary>
        public const float ThumbGain = 1.5f;

        /// <summary>The model's bone of <paramref name="finger"/>'s <paramref name="joint"/>th joint (0 nearest the hand) on <paramref name="side"/>.</summary>
        public static AvatarBone Bone(ArmSide side, Finger finger, int joint)
        {
            AvatarBone first = side.IsLeft ? AvatarBone.LeftThumbProximal : AvatarBone.RightThumbProximal;
            return first + ((int)finger * FingerPose.JointCount) + joint;
        }

        /// <summary>
        /// Writes the finger angles of <paramref name="hand"/> into <paramref name="into"/>, within the limits;
        /// false, writing nothing, when the hand is not seen. <paramref name="left"/> is whether it is the
        /// person's left hand, whose thumb is on the other side.
        /// </summary>
        public static bool TryMeasure(HandPose hand, bool left, FingerPose into)
        {
            if (hand == null || !hand.IsUsable)
            {
                return false;
            }

            Vector3 wrist = hand[HandJoint.Wrist];
            Vector3 indexBase = hand[HandJoint.IndexMcp];
            Vector3 littleBase = hand[HandJoint.LittleMcp];

            // The back of the hand as the palm's rotation takes it (tracking detailed design 6.4).
            Vector3 back = Vector3.Cross(indexBase - wrist, littleBase - wrist);
            if (left)
            {
                back = -back;
            }

            Vector3 thumbward = indexBase - littleBase;
            if (back.sqrMagnitude < 1e-12f || thumbward.sqrMagnitude < 1e-12f)
            {
                return false;
            }

            for (Finger finger = Finger.Index; finger <= Finger.Little; finger++)
            {
                int first = (int)HandJoint.IndexMcp + (((int)finger - 1) * 4);
                Vector3 knuckle = hand.Positions[first];
                Vector3 proximal = hand.Positions[first + 1] - knuckle;
                Vector3 middle = hand.Positions[first + 2] - hand.Positions[first + 1];
                (float bend, float spread) = Base(knuckle - wrist, proximal, back, thumbward);
                float middleBend = Shown(Vector3.Angle(proximal, middle), MiddleGain, MaxMiddleBend);
                into.SetBend(finger, 0, Shown(bend, BaseGain, MaxBaseBend));
                into.SetBend(finger, 1, middleBend);
                into.SetBend(finger, 2, Mathf.Min(middleBend * EndShare, MaxEndBend));
                into.SetSpread(finger, Mathf.Clamp(spread, -MaxSpread, MaxSpread));
            }

            Vector3 thumbBase = hand[HandJoint.ThumbMcp] - hand[HandJoint.ThumbCmc];
            Vector3 thumbMiddle = hand[HandJoint.ThumbIp] - hand[HandJoint.ThumbMcp];
            Vector3 thumbEnd = hand[HandJoint.ThumbTip] - hand[HandJoint.ThumbIp];
            into.SetBend(Finger.Thumb, 0, Mathf.Clamp(OpenThumbDegrees - Vector3.Angle(thumbBase, indexBase - wrist), 0f, MaxThumbFold));
            into.SetBend(Finger.Thumb, 1, Shown(Vector3.Angle(thumbBase, thumbMiddle), ThumbGain, MaxThumbBend));
            into.SetBend(Finger.Thumb, 2, Shown(Vector3.Angle(thumbMiddle, thumbEnd), ThumbGain, MaxThumbEndBend));
            into.SetSpread(Finger.Thumb, 0f);
            return true;
        }

        /// <summary>The bend shown for a bend read: past <see cref="OpenBend"/>, scaled by <paramref name="gain"/>, from 0 to <paramref name="max"/>. Pure.</summary>
        public static float Shown(float read, float gain, float max)
        {
            return Mathf.Clamp((read - OpenBend) * gain, 0f, max);
        }

        /// <summary>
        /// How far a finger's first bone (<paramref name="proximal"/>) bends from its metacarpal toward the
        /// palm, and how far it spreads toward the thumb, in degrees. The spread fades as the finger bends, as
        /// a finger pointing into the palm shows no side to spread to. Pure.
        /// </summary>
        public static (float Bend, float Spread) Base(Vector3 metacarpal, Vector3 proximal, Vector3 back, Vector3 thumbward)
        {
            Vector3 along = metacarpal.normalized;
            Vector3 up = Vector3.ProjectOnPlane(back, along).normalized;
            Vector3 side = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(thumbward, along), up).normalized;
            float forward = Vector3.Dot(proximal, along);
            float bend = Mathf.Atan2(-Vector3.Dot(proximal, up), forward) * Mathf.Rad2Deg;
            float spread = Mathf.Atan2(Vector3.Dot(proximal, side), Mathf.Abs(forward)) * Mathf.Rad2Deg;
            return (bend, spread * Mathf.Clamp01(Mathf.Cos(bend * Mathf.Deg2Rad)));
        }

        /// <summary>
        /// Writes the root-space rotations of one hand's 15 finger bones into <paramref name="into"/>, in
        /// the order of <see cref="Bone"/> (finger, then joint): each joint turned by <paramref name="pose"/>'s
        /// angles about <paramref name="rest"/>'s axes, on <paramref name="hand"/>, the hand's root-space rotation.
        /// </summary>
        public static void Rotations(in FingerRest rest, Quaternion hand, FingerPose pose, Quaternion[] into)
        {
            for (Finger finger = Finger.Thumb; finger <= Finger.Little; finger++)
            {
                // A thumb folds across the palm, toward the little finger; the others into the palm.
                Vector3 toward = finger == Finger.Thumb ? (-rest.Back - rest.Thumbward).normalized : -rest.Back;
                Quaternion chain = hand;
                for (int joint = 0; joint < FingerPose.JointCount; joint++)
                {
                    Vector3 direction = rest.Direction(finger, joint);
                    Quaternion turn = Quaternion.AngleAxis(pose.Bend(finger, joint), Axis(direction, toward));
                    if (joint == 0 && finger != Finger.Thumb)
                    {
                        turn = Quaternion.AngleAxis(pose.Spread(finger), Axis(direction, rest.Thumbward)) * turn;
                    }

                    // In the reference pose every bone's rotation is identity, so a joint's turn about an axis
                    // of the reference pose follows its parent's.
                    chain *= turn;
                    into[((int)finger * FingerPose.JointCount) + joint] = chain;
                }
            }
        }

        /// <summary>The axis about which a positive angle turns <paramref name="direction"/> toward <paramref name="toward"/>.</summary>
        private static Vector3 Axis(Vector3 direction, Vector3 toward)
        {
            Vector3 axis = Vector3.Cross(direction, toward);
            return axis.sqrMagnitude < 1e-12f ? Vector3.up : axis.normalized;
        }
    }

    /// <summary>
    /// One hand of the model in the reference pose (avatar detailed design 7.5): where each finger bone
    /// points, which way the back of the hand faces, and which way is toward the thumb, in the avatar root's
    /// space. From the bone positions measured at load; a typical hand for what the model lacks.
    /// </summary>
    public readonly struct FingerRest
    {
        private readonly Vector3[] _directions;

        private FingerRest(Vector3[] directions, Vector3 back, Vector3 thumbward)
        {
            _directions = directions;
            Back = back;
            Thumbward = thumbward;
        }

        /// <summary>Out of the back of the hand.</summary>
        public Vector3 Back { get; }

        /// <summary>Across the hand toward the thumb, at right angles to <see cref="Back"/>.</summary>
        public Vector3 Thumbward { get; }

        /// <summary>Where the bone of <paramref name="finger"/>'s <paramref name="joint"/>th joint points.</summary>
        public Vector3 Direction(Finger finger, int joint)
        {
            return _directions[((int)finger * FingerPose.JointCount) + joint];
        }

        /// <summary>
        /// <paramref name="side"/>'s hand of the model measured in <paramref name="shape"/>; a typical hand
        /// (palm down, fingers out to the side, the thumb forward) where it is null or lacks the bones. Pure.
        /// </summary>
        public static FingerRest Of(AvatarBodyShape shape, ArmSide side)
        {
            bool left = side.IsLeft;
            Vector3 along = left ? Vector3.left : Vector3.right;
            Vector3 thumbward = Vector3.forward;
            if (Has(shape, side.HandBone) && Has(shape, Bone(side, Finger.Middle)) && Has(shape, Bone(side, Finger.Index)) && Has(shape, Bone(side, Finger.Little)))
            {
                along = (shape.Rest(Bone(side, Finger.Middle)) - shape.Rest(side.HandBone)).normalized;
                thumbward = shape.Rest(Bone(side, Finger.Index)) - shape.Rest(Bone(side, Finger.Little));
            }

            // The thumb is in front on both hands, so their backs are on opposite sides of along × thumbward.
            Vector3 back = (left ? Vector3.Cross(along, thumbward) : Vector3.Cross(thumbward, along)).normalized;
            thumbward = Vector3.ProjectOnPlane(thumbward, back).normalized;
            Vector3 thumb = (along + thumbward - (back * 0.3f)).normalized;

            var directions = new Vector3[FingerPose.FingerCount * FingerPose.JointCount];
            for (Finger finger = Finger.Thumb; finger <= Finger.Little; finger++)
            {
                Vector3 typical = finger == Finger.Thumb ? thumb : along;
                Vector3 previous = typical;
                for (int joint = 0; joint < FingerPose.JointCount; joint++)
                {
                    // A bone points to the next joint; the last, without one, keeps the one before's direction.
                    AvatarBone bone = FingerRetargeting.Bone(side, finger, joint);
                    Vector3 direction = previous;
                    if (joint < FingerPose.JointCount - 1 && Has(shape, bone) && Has(shape, bone + 1))
                    {
                        Vector3 toNext = shape.Rest(bone + 1) - shape.Rest(bone);
                        direction = toNext.sqrMagnitude > 1e-10f ? toNext.normalized : previous;
                    }

                    directions[((int)finger * FingerPose.JointCount) + joint] = direction;
                    previous = direction;
                }
            }

            return new FingerRest(directions, back, thumbward);
        }

        private static AvatarBone Bone(ArmSide side, Finger finger)
        {
            return FingerRetargeting.Bone(side, finger, 0);
        }

        private static bool Has(AvatarBodyShape shape, AvatarBone bone)
        {
            return shape != null && shape.Has(bone);
        }
    }
}
