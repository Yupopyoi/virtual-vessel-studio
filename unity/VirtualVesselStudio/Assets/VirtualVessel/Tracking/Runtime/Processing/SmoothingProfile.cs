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
    /// The three sets are levels 5 (the standard), 7, and 9; the levels between and beyond take the
    /// same ratio per level, so each step changes the cutoffs by the same factor, and levels 1 to 4 are
    /// steadier than any set measured. Until 2026-10-24 the sets were levels 3, 5, and 7.
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

        /// <summary>
        /// The parameters of <paramref name="level"/>, kept within the levels: the measured sets at 5, 7,
        /// and 9, and geometric steps from the balanced set toward and past the nearer of the other two. Pure.
        /// </summary>
        public static SmoothingProfile For(int level)
        {
            level = SmoothingLevel.Clamp(level);
            switch (level)
            {
                case SmoothingLevel.Standard:
                    return s_steady;
                case SmoothingLevel.Balanced:
                    return s_balanced;
                case SmoothingLevel.Responsive:
                    return s_quick;
            }

            // Two levels lead from the balanced set to the next one, so each level is half of that ratio.
            SmoothingProfile toward = level < SmoothingLevel.Balanced ? s_steady : s_quick;
            float steps = Math.Abs(level - SmoothingLevel.Balanced) / 2f;
            return new SmoothingProfile(
                Step(s_balanced.Angle, toward.Angle, steps),
                Step(s_balanced.Position, toward.Position, steps),
                Step(s_balanced.Face, toward.Face, steps));
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
