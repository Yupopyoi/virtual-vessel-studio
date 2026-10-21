using System;

namespace VirtualVessel.Tracking.Processing
{
    /// <summary>
    /// One Euro parameters for each kind of value at a <see cref="SmoothingLevel"/> (tracking detailed
    /// design 8.2). Beta depends on the unit, so angles, positions, and face values each have their own.
    /// </summary>
    /// <remarks>
    /// The angle values come from synthetic motion with 2° noise at 30 samples per second, averaged
    /// over 20 noise seeds (OneEuroComparisonTests): Standard is as steady as the prototype's 30-frame
    /// average when still (0.088° against 0.097° per frame) with 66 ms lag instead of 483 ms. Smooth
    /// is steadier with 99 ms, Responsive follows closer with 56 ms. Positions scale beta by 1 cm ≈ 2°
    /// at arm length; face values are kept fast so blinks survive. Positions and face values are
    /// provisional until they are tuned on recorded camera data (tracking detailed design 17).
    /// Those three presets are levels 3, 5, and 7; the levels between and beyond take the same ratio
    /// per level, so each step changes the cutoffs by the same factor.
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

        private static readonly SmoothingProfile s_smooth = new SmoothingProfile(
            new OneEuroParameters(0.05f, 0.003f),
            new OneEuroParameters(0.05f, 0.6f),
            new OneEuroParameters(0.5f, 0.5f));

        private static readonly SmoothingProfile s_standard = new SmoothingProfile(
            new OneEuroParameters(0.1f, 0.005f),
            new OneEuroParameters(0.1f, 1f),
            new OneEuroParameters(1f, 1f));

        private static readonly SmoothingProfile s_responsive = new SmoothingProfile(
            new OneEuroParameters(0.2f, 0.01f),
            new OneEuroParameters(0.2f, 2f),
            new OneEuroParameters(2f, 2f));

        /// <summary>
        /// The parameters of <paramref name="level"/>, kept within the levels: the measured presets at 3,
        /// 5, and 7, and geometric steps from the standard toward and past the nearer of the other two. Pure.
        /// </summary>
        public static SmoothingProfile For(int level)
        {
            level = SmoothingLevel.Clamp(level);
            switch (level)
            {
                case SmoothingLevel.Smooth:
                    return s_smooth;
                case SmoothingLevel.Standard:
                    return s_standard;
                case SmoothingLevel.Responsive:
                    return s_responsive;
            }

            // Two levels lead from the standard to the next preset, so each level is half of that ratio.
            SmoothingProfile toward = level < SmoothingLevel.Standard ? s_smooth : s_responsive;
            float steps = Math.Abs(level - SmoothingLevel.Standard) / 2f;
            return new SmoothingProfile(
                Step(s_standard.Angle, toward.Angle, steps),
                Step(s_standard.Position, toward.Position, steps),
                Step(s_standard.Face, toward.Face, steps));
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
