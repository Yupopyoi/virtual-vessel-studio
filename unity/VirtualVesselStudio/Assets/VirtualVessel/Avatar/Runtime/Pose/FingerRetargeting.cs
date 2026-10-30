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
        /// read past <see cref="OpenBend"/>, scaled by <see cref="BaseGain"/> at the base, and mapped from an
        /// open hand's reading to a fist's at the middle joint (<see cref="ShownMiddle"/>).
        /// </summary>
        public const float OpenBend = 10f;
        public const float BaseGain = 1.5f;

        // The middle joint's reading of an open hand and of a fist, per finger from the index: a straight
        // middle finger reads about 36°, and read past 10° and scaled, it stayed bent in an open hand (the
        // user's report). Medians on the user's recording made for the hands (2026-10-25): open 17-18, 34-37,
        // 23-26, and 22-25; a fist 81-88, 80-94, 73-92, and 68-76. The older recordings agree on the middle
        // finger (36° in the most open hands).
        private static readonly float[] s_middleOpen = { 18f, 36f, 25f, 23f };
        private static readonly float[] s_middleFist = { 85f, 90f, 84f, 72f };

        /// <summary>
        /// The end joint's share of the middle one's bend. A finger's end joint bends with its middle one,
        /// and the end joint read on its own was noise: an open index finger read a median of 42° there.
        /// </summary>
        public const float EndShare = 2f / 3f;

        /// <summary>The share of the middle joint's extra bend given to the base (<see cref="Arc"/>).</summary>
        public const float ArcShare = 0.35f;

        /// <summary>How far a finger spreads either way at its base.</summary>
        public const float MaxSpread = 15f;

        /// <summary>
        /// How far across the palm, as a share of its width from the index finger's base toward the little
        /// finger's, the thumb's tip reaches as it tucks in: from <see cref="TuckFrom"/> (beside the index
        /// finger) to <see cref="TuckFull"/>, where the thumb folds across the palm at its base by
        /// <see cref="MaxThumbFold"/> and bends at least <see cref="ThumbTuckBend"/> at its middle joint. The
        /// tip tells a tucked thumb from one standing out of a fist, where the angle between the thumb's and
        /// the index finger's metacarpals barely differed (25° and 30°) and folded a raised thumb. On the
        /// user's recordings (2026-10-24, 25), the tip was -0.87 with the thumb raised out of a fist, -0.75 in
        /// an open hand, -0.38 laid along the index finger, 0.03–0.23 wrapped over the curled fingers (a fist in
        /// rock-paper-scissors), and 0.36 tucked inside them. Reaching full only at 0.4, a wrapped thumb had
        /// folded halfway (the user's report); from -0.2 to 0.15, it went between half and fully folded as the
        /// tip moved in a fist (-0.42 to 0.31 over one), which looked unsteady (the user's report).
        /// </summary>
        public const float TuckFrom = -0.3f;
        public const float TuckFull = 0f;

        /// <summary>
        /// The thumb tucked in: folded across the palm at its base, and bent at least this far at its middle
        /// and end joints, so it lies on the palm under the curled fingers, which wrap it. In a fist in
        /// rock-paper-scissors the user wants the thumb hidden like that (2026-10-25). Every joint turns it
        /// across, along the palm: bending every joint into the palm, the thumb folded back toward the wrist,
        /// which a thumb cannot do, and bringing the base in front of the palm (40°, with 60° and 50° past it)
        /// laid the thumb over the curled fingers, 5.6 cm in front of the palm's knuckles where the fingers'
        /// middle joints were 3 cm (the user's videos). With these, on the sample model replaying the user's
        /// recording made for the hands, the thumb's tip lay 1.7 cm in front of the knuckles, under the middle
        /// and ring fingers, behind their middle joints.
        /// </summary>
        public const float MaxThumbFold = 30f;
        public const float ThumbTuckBend = 85f;
        public const float ThumbTuckEndBend = 60f;

        /// <summary>How far the thumb bends at its middle and end joints.</summary>
        public const float MaxThumbBend = 60f;
        public const float MaxThumbEndBend = 80f;

        /// <summary>
        /// What a straight thumb reads at its middle and end joints, and the gain past it, as for the other
        /// fingers. On the user's recordings a thumb out of a fist read medians of 26° and 21°, and in an open
        /// hand 22° and 18°; shown past 10°, a raised thumb stayed bent (the user's screen recording).
        /// </summary>
        public const float ThumbOpenBend = 20f;
        public const float ThumbGain = 1.5f;

        /// <summary>
        /// The thumb's reading toward its side and the scale past it, as for the other fingers: on the user's
        /// recordings the whole thumb read a median of 33° from the fingers in an open hand and 53° in half of
        /// a heart, where the metacarpal alone barely differed (41° and 44°), and shown as read it still
        /// pointed at the face (2026-10-30).
        /// </summary>
        public const float OpenThumbAzimuth = 33f;
        public const float ThumbAzimuthGain = 1.75f;

        /// <summary>How far the thumb can point (<see cref="ThumbAim"/>).</summary>
        public const float MinThumbAzimuth = -20f;
        public const float MaxThumbAzimuth = 90f;
        public const float MinThumbElevation = -20f;
        public const float MaxThumbElevation = 70f;

        /// <summary>The model's bone of <paramref name="finger"/>'s <paramref name="joint"/>th joint (0 nearest the hand) on <paramref name="side"/>.</summary>
        public static AvatarBone Bone(ArmSide side, Finger finger, int joint)
        {
            AvatarBone first = side.IsLeft ? AvatarBone.LeftThumbProximal : AvatarBone.RightThumbProximal;
            return first + ((int)finger * FingerPose.JointCount) + joint;
        }

        /// <summary>
        /// Writes the finger angles of <paramref name="hand"/> into <paramref name="into"/>, within the limits;
        /// false, writing nothing, when the hand is not seen. <paramref name="left"/> is whether it is the
        /// person's left hand, whose thumb is on the other side. <paramref name="tuning"/> is the user's
        /// strength; null is the default.
        /// </summary>
        public static bool TryMeasure(HandPose hand, bool left, FingerPose into, FingerTuning tuning = null)
        {
            tuning ??= FingerTuning.Default;
            float baseGain = BaseGain * tuning.Bend;
            float thumbGain = ThumbGain * tuning.Thumb;

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
                (float baseBend, float middleBend) = Arc(Shown(bend, OpenBend, baseGain, MaxBaseBend), Mathf.Min(ShownMiddle(finger, Vector3.Angle(proximal, middle)) * tuning.Bend, MaxMiddleBend));
                into.SetBend(finger, 0, baseBend);
                into.SetBend(finger, 1, middleBend);
                into.SetBend(finger, 2, Mathf.Min(middleBend * EndShare, MaxEndBend));
                into.SetSpread(finger, Mathf.Clamp(spread, -MaxSpread, MaxSpread));
            }

            Vector3 thumbBase = hand[HandJoint.ThumbMcp] - hand[HandJoint.ThumbCmc];
            Vector3 thumbMiddle = hand[HandJoint.ThumbIp] - hand[HandJoint.ThumbMcp];
            Vector3 thumbEnd = hand[HandJoint.ThumbTip] - hand[HandJoint.ThumbIp];
            float tuck = Tuck(hand[HandJoint.ThumbTip] - indexBase, thumbward);
            into.SetBend(Finger.Thumb, 0, MaxThumbFold * tuck);
            into.SetBend(Finger.Thumb, 1, Mathf.Max(Shown(Vector3.Angle(thumbBase, thumbMiddle), ThumbOpenBend, thumbGain, MaxThumbBend), ThumbTuckBend * tuck));
            into.SetBend(Finger.Thumb, 2, Mathf.Max(Shown(Vector3.Angle(thumbMiddle, thumbEnd), ThumbOpenBend, thumbGain, MaxThumbEndBend), ThumbTuckEndBend * tuck));
            into.SetSpread(Finger.Thumb, 0f);
            (into.ThumbAzimuth, into.ThumbElevation) = ThumbAim(hand[HandJoint.ThumbTip] - hand[HandJoint.ThumbCmc], hand[HandJoint.MiddleMcp] - wrist, back, thumbward);
            into.ThumbAim = (1f - tuck) * 100f;
            return true;
        }

        /// <summary>
        /// The middle joint's bend shown for <paramref name="read"/> on <paramref name="finger"/> (not the
        /// thumb): straight at an open hand's reading, <see cref="MaxMiddleBend"/> at a fist's, and in
        /// proportion between. Pure.
        /// </summary>
        public static float ShownMiddle(Finger finger, float read)
        {
            int i = (int)finger - 1;
            return Mathf.InverseLerp(s_middleOpen[i], s_middleFist[i], read) * MaxMiddleBend;
        }

        /// <summary>
        /// The base and middle joints with a share of what the middle bends past the base moved to the base
        /// (<see cref="ArcShare"/>), so a finger curves as a whole. In a rounded hand, such as half of a heart,
        /// the base reads small and the middle large: on the user's recordings (2026-10-30), 5-20° and
        /// 60-88°, which showed as a straight finger hooked at the end. Pure.
        /// </summary>
        public static (float Base, float Middle) Arc(float baseBend, float middleBend)
        {
            float moved = Mathf.Min(Mathf.Max(0f, middleBend - baseBend) * ArcShare, MaxBaseBend - baseBend);
            return (baseBend + moved, middleBend - moved);
        }

        /// <summary>
        /// Where the thumb (<paramref name="thumb"/>, from its base to its tip) points in the palm, in degrees: from the fingers
        /// (<paramref name="along"/>) toward the thumb's side, and out of the palm's side of the hand, within
        /// the limits. A model's thumb rests in its own direction, often nearly along the fingers, so bending
        /// its joints alone pointed it at the face in half of a heart, where the user's points down (the
        /// user's report, 2026-10-30). Pure.
        /// </summary>
        public static (float Azimuth, float Elevation) ThumbAim(Vector3 thumb, Vector3 along, Vector3 back, Vector3 thumbward)
        {
            Vector3 forward = along.normalized;
            Vector3 up = Vector3.ProjectOnPlane(back, forward).normalized;
            Vector3 side = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(thumbward, forward), up).normalized;
            float azimuth = Mathf.Atan2(Vector3.Dot(thumb, side), Vector3.Dot(thumb, forward)) * Mathf.Rad2Deg;
            float elevation = Mathf.Asin(Mathf.Clamp(Vector3.Dot(thumb.normalized, -up), -1f, 1f)) * Mathf.Rad2Deg;
            return (Mathf.Clamp(OpenThumbAzimuth + ((azimuth - OpenThumbAzimuth) * ThumbAzimuthGain), MinThumbAzimuth, MaxThumbAzimuth), Mathf.Clamp(elevation, MinThumbElevation, MaxThumbElevation));
        }

        /// <summary>The bend shown for a bend read: past <paramref name="open"/>, scaled by <paramref name="gain"/>, from 0 to <paramref name="max"/>. Pure.</summary>
        public static float Shown(float read, float open, float gain, float max)
        {
            return Mathf.Clamp((read - open) * gain, 0f, max);
        }

        /// <summary>
        /// How tucked in the thumb is, 0 to 1: how far its tip (<paramref name="tipFromIndexBase"/>) reaches
        /// across the palm from the index finger's base, from <see cref="TuckFrom"/> to <see cref="TuckFull"/>
        /// of the palm's width (<paramref name="thumbward"/>, from the little finger's base to the index
        /// finger's). Pure.
        /// </summary>
        public static float Tuck(Vector3 tipFromIndexBase, Vector3 thumbward)
        {
            float width = thumbward.magnitude;
            return width < 1e-6f ? 0f : Mathf.InverseLerp(TuckFrom, TuckFull, -Vector3.Dot(tipFromIndexBase, thumbward) / (width * width));
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
                Quaternion chain = hand;
                for (int joint = 0; joint < FingerPose.JointCount; joint++)
                {
                    Vector3 direction = rest.Direction(finger, joint);
                    // The fingers bend into the palm; the thumb bends across it, toward the little finger.
                    Vector3 toward = finger == Finger.Thumb ? -rest.Thumbward : -rest.Back;
                    Quaternion turn = Quaternion.AngleAxis(pose.Bend(finger, joint), Axis(direction, toward));
                    if (joint == 0 && finger == Finger.Thumb)
                    {
                        Quaternion aim = Quaternion.FromToRotation(direction, rest.InPalm(pose.ThumbAzimuth, pose.ThumbElevation));
                        turn = Quaternion.Slerp(Quaternion.identity, aim, pose.ThumbAim / 100f) * turn;
                    }

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

        private FingerRest(Vector3[] directions, Vector3 along, Vector3 back, Vector3 thumbward)
        {
            _directions = directions;
            Along = along;
            Back = back;
            Thumbward = thumbward;
        }

        /// <summary>From the wrist toward the fingers, in the palm's plane.</summary>
        public Vector3 Along { get; }

        /// <summary>Out of the back of the hand.</summary>
        public Vector3 Back { get; }

        /// <summary>Across the hand toward the thumb, at right angles to <see cref="Back"/>.</summary>
        public Vector3 Thumbward { get; }

        /// <summary>The direction <paramref name="azimuth"/> degrees from the fingers toward the thumb's side and <paramref name="elevation"/> degrees out of the palm's side. Pure.</summary>
        public Vector3 InPalm(float azimuth, float elevation)
        {
            float a = azimuth * Mathf.Deg2Rad;
            float e = elevation * Mathf.Deg2Rad;
            return (((Along * Mathf.Cos(a)) + (Thumbward * Mathf.Sin(a))) * Mathf.Cos(e)) - (Back * Mathf.Sin(e));
        }

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

            return new FingerRest(directions, Vector3.Cross(left ? thumbward : back, left ? back : thumbward).normalized, back, thumbward);
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
