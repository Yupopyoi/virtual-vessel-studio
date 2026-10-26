using UnityEngine;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// Keeping the arms out of the head and torso over time (avatar detailed design 7.6), as
    /// <see cref="BodyMotion"/> is for the upper body: the side each limb is turned to is remembered, and
    /// the turn <see cref="ArmClearance"/> adds is eased in and out, so a change of side or a limb sliding
    /// off a part glides instead of jumping. The tracked motion itself is followed without delay.
    /// </summary>
    /// <remarks>
    /// The state lives here; the turns are the pure <see cref="ArmClearance"/>, the easing the pure
    /// <see cref="Ease"/>. Buffers are reused, so a frame allocates nothing (CLAUDE.md 23).
    /// </remarks>
    public sealed class ClearanceMotion
    {
        /// <summary>How fast the added turn follows: after this long, it has come about two thirds of the way.</summary>
        public const float EaseSeconds = 0.08f;

        /// <summary>Closer than this, the added turn is where it is going.</summary>
        public const float SettledDegrees = 0.01f;

        private readonly AvatarBodyShape _shape;
        private readonly BodyTarget _cleared = new BodyTarget();
        private readonly Quaternion[] _added = new Quaternion[ArmSide.LimbBones.Length];
        private readonly FrameClock _clock = new FrameClock();
        private ArmClearance.ClearanceSides _sides;

        /// <param name="shape">The model's body; null leaves the arms as tracked.</param>
        public ClearanceMotion(AvatarBodyShape shape)
        {
            _shape = shape;
            for (int i = 0; i < _added.Length; i++)
            {
                _added[i] = Quaternion.identity;
            }
        }

        /// <summary>What the upper body shows this frame.</summary>
        public BodyTarget Current { get; } = new BodyTarget();

        /// <param name="frontOnly">Keeps the arms in front of the body, for a body tracked without depth (2D).</param>
        /// <param name="now">Seconds on the clock of the tracking timestamps.</param>
        public void Update(BodyTarget tracked, Quaternion neck, Quaternion head, bool frontOnly, double now)
        {
            float elapsed = _clock.Tick(now, out bool first);
            if (first)
            {
                elapsed = float.PositiveInfinity;
            }

            _sides = ArmClearance.Apply(_shape, tracked, neck, head, _sides, frontOnly, _cleared);
            Current.CopyFrom(tracked);
            for (int i = 0; i < ArmSide.LimbBones.Length; i++)
            {
                UpperBodyBone bone = ArmSide.LimbBones[i];
                _added[i] = Ease(_added[i], _cleared[bone] * Quaternion.Inverse(tracked[bone]), elapsed);
                Current[bone] = _added[i] * tracked[bone];
            }
        }

        /// <summary>
        /// The added turn after <paramref name="elapsed"/> seconds, moving from <paramref name="from"/> toward
        /// <paramref name="to"/> with <see cref="EaseSeconds"/>; at once on the first frame, and exactly once
        /// within <see cref="SettledDegrees"/>, so a still arm ends just clear rather than a hair inside. Pure.
        /// </summary>
        public static Quaternion Ease(Quaternion from, Quaternion to, float elapsed)
        {
            float share = float.IsPositiveInfinity(elapsed) ? 1f : 1f - Mathf.Exp(-elapsed / EaseSeconds);
            Quaternion eased = Quaternion.Slerp(from, to, share);
            return Quaternion.Angle(eased, to) < SettledDegrees ? to : eased;
        }
    }
}
