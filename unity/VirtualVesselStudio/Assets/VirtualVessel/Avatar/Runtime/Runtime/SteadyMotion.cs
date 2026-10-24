using UnityEngine;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// The last step of the upper body (avatar detailed design 7.8): each arm bone turns no faster than a
    /// limit, where it points and its twist about itself apart. Steadiness and a natural look matter more
    /// than following every turn (the user's decision, 2026-10-24). On the user's recording "1024不自然解消用"
    /// at the steadiest smoothing, the arms still turned up to 52° and the hands 129° in a frame, mostly
    /// in twist: the twist the elbow's bend sets turns fast on a nearly straight arm.
    /// </summary>
    /// <remarks>
    /// The state lives here; the turn is the pure <see cref="Limit"/>. Nothing is allocated per frame.
    /// </remarks>
    public sealed class SteadyMotion
    {
        /// <summary>How fast an arm bone may turn where it points, in degrees a second (12° a frame at 60).</summary>
        public const float MaxSwingDegreesPerSecond = 720f;

        /// <summary>How fast an arm bone may twist about itself, in degrees a second (6° a frame at 60).</summary>
        public const float MaxTwistDegreesPerSecond = 360f;

        private readonly Quaternion[] _previous = new Quaternion[ArmSide.LimbBones.Length];
        private readonly FrameClock _clock = new FrameClock();

        /// <summary>What the upper body shows this frame.</summary>
        public BodyTarget Current { get; } = new BodyTarget();

        /// <summary>Moves toward <paramref name="target"/>: the torso at once, each arm bone within its limits.</summary>
        /// <param name="now">Seconds on the clock of the tracking timestamps.</param>
        public void Update(BodyTarget target, double now)
        {
            float elapsed = _clock.Tick(now, out bool first);
            Current.CopyFrom(target);
            for (int i = 0; i < ArmSide.LimbBones.Length; i++)
            {
                UpperBodyBone bone = ArmSide.LimbBones[i];
                if (!first)
                {
                    Current[bone] = Limit(_previous[i], target[bone], PoseRetargeting.RestDirection(ArmSide.IsLeftLimb(i)), MaxSwingDegreesPerSecond * elapsed, MaxTwistDegreesPerSecond * elapsed);
                }

                _previous[i] = Current[bone];
            }
        }

        /// <summary>
        /// <paramref name="previous"/> turned toward <paramref name="target"/>: where the bone points
        /// (<paramref name="rest"/> turned) by at most <paramref name="maxSwing"/>, and its twist about itself
        /// by at most <paramref name="maxTwist"/>, in degrees. Pure.
        /// </summary>
        public static Quaternion Limit(Quaternion previous, Quaternion target, Vector3 rest, float maxSwing, float maxTwist)
        {
            Vector3 from = previous * rest;
            Vector3 to = target * rest;
            float swing = Vector3.Angle(from, to);
            Quaternion swung = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(from, to), swing > maxSwing ? maxSwing / swing : 1f) * previous;

            // The twist left: from the target's own direction, how far the previous turned there is rolled.
            Quaternion fullySwung = Quaternion.FromToRotation(from, to) * previous;
            (target * Quaternion.Inverse(fullySwung)).ToAngleAxis(out float twist, out Vector3 axis);
            twist = PoseRetargeting.Signed(twist) * Mathf.Sign(Vector3.Dot(axis, to));
            return Quaternion.AngleAxis(Mathf.Clamp(twist, -maxTwist, maxTwist), swung * rest) * swung;
        }
    }
}
