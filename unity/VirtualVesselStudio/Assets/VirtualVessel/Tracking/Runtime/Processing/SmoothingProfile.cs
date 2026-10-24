using System;

namespace VirtualVessel.Tracking.Processing
{
    /// <summary>
    /// One Euro parameters for each kind of value at a <see cref="SmoothingLevel"/> (tracking detailed
    /// design 8.2). Beta depends on the unit, so angles, positions, and face values each have their own.
    /// </summary>
    /// <remarks>
    /// The angle values come from synthetic motion with 2° noise at 30 samples per second, averaged
    /// over 20 noise seeds (OneEuroComparisonTests): the balanced set is as steady as the prototype's
    /// 30-frame average when still (0.088° against 0.097° per frame) with 66 ms lag instead of 483 ms.
    /// The steady set is steadier with 99 ms, the quick one follows closer with 56 ms. Positions scale
    /// beta by 1 cm ≈ 2° at arm length; face values are kept fast so blinks survive. Positions and face
    /// values are provisional until they are tuned on recorded camera data (tracking detailed design 17).
    /// The levels step from the balanced set past the steady one by the same ratio per level, half the
    /// ratio between the two, so the steady set would be level 9 and the balanced one 11; the user chose
    /// stability over lag, and the standard 5 lags about 260 ms on recorded arms (2026-10-24). The face
    /// stops at the steady set, so blinks and the mouth keep up at every level.
    /// </remarks>
    public readonly struct SmoothingProfile
    {
        private SmoothingProfile(OneEuroParameters angle, OneEuroParameters position, OneEuroParameters face)
        {
            Angle = angle;
            Position = position;
            Face = face;
        }

        /// <summary>For rotations, in degrees.</summary>
        public OneEuroParameters Angle { get; }

        /// <summary>For positions, in meters.</summary>
        public OneEuroParameters Position { get; }

        /// <summary>
        /// For the joints of the body and of each hand, smoothed together (<see cref="LinkedPositionFilter"/>):
        /// <see cref="Position"/> with <see cref="JointsBetaShare"/> of its beta.
        /// </summary>
        public OneEuroParameters Joints => new OneEuroParameters(Position.MinCutoff, Position.Beta * JointsBetaShare, Position.DerivativeCutoff);

        /// <summary>
        /// Joints smoothed together take the speed of the fastest one, in all three axes, which is faster
        /// than one joint's along one axis; half the beta keeps each level's lag on the user's recordings
        /// (383 ms at level 1 on "1024不自然解消用", as with a filter per joint and axis) and steadies the
        /// arms a little more (2026-10-24, tracking detailed design 8.2).
        /// </summary>
        public const float JointsBetaShare = 0.5f;

        /// <summary>For face values and gaze, 0 to 1.</summary>
        public OneEuroParameters Face { get; }

        private static readonly SmoothingProfile s_steady = new SmoothingProfile(
            new OneEuroParameters(0.05f, 0.003f),
            new OneEuroParameters(0.05f, 0.6f),
            new OneEuroParameters(0.5f, 0.5f));

        private static readonly SmoothingProfile s_balanced = new SmoothingProfile(
            new OneEuroParameters(0.1f, 0.005f),
            new OneEuroParameters(0.1f, 1f),
            new OneEuroParameters(1f, 1f));

        private static readonly SmoothingProfile s_quick = new SmoothingProfile(
            new OneEuroParameters(0.2f, 0.01f),
            new OneEuroParameters(0.2f, 2f),
            new OneEuroParameters(2f, 2f));

        // Where the measured sets would sit on the levels, past the quickest one.
        private const int SteadyLevel = 9;
        private const int BalancedLevel = 11;

        /// <summary>The measured sets, for tests of the filters themselves.</summary>
        public static SmoothingProfile Steady => s_steady;

        public static SmoothingProfile Balanced => s_balanced;

        public static SmoothingProfile Quick => s_quick;

        /// <summary>
        /// The parameters of <paramref name="level"/>, kept within the levels: geometric steps from the
        /// balanced set toward and past the steady one, two levels for the ratio between them; the face no
        /// further than the steady set. Pure.
        /// </summary>
        public static SmoothingProfile For(int level)
        {
            level = SmoothingLevel.Clamp(level);
            float steps = (BalancedLevel - level) / 2f;
            float faceSteps = Math.Min(steps, (BalancedLevel - SteadyLevel) / 2f);
            return new SmoothingProfile(
                Step(s_balanced.Angle, s_steady.Angle, steps),
                Step(s_balanced.Position, s_steady.Position, steps),
                Step(s_balanced.Face, s_steady.Face, faceSteps));
        }

        private static OneEuroParameters Step(OneEuroParameters from, OneEuroParameters to, float steps)
        {
            return new OneEuroParameters(
                from.MinCutoff * (float)Math.Pow(to.MinCutoff / from.MinCutoff, steps),
                from.Beta * (float)Math.Pow(to.Beta / from.Beta, steps),
                from.DerivativeCutoff);
        }
    }
}
