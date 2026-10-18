namespace VirtualVessel.Tracking.Processing
{
    /// <summary>
    /// One Euro parameters for each kind of value at a <see cref="SmoothingPreset"/> (tracking detailed
    /// design 8.2). Beta depends on the unit, so angles, positions, and face values each have their own.
    /// </summary>
    /// <remarks>
    /// The angle values come from synthetic motion with 2° noise at 30 samples per second, averaged
    /// over 20 noise seeds (OneEuroComparisonTests): Standard is as steady as the prototype's 30-frame
    /// average when still (0.088° against 0.097° per frame) with 66 ms lag instead of 483 ms. Smooth
    /// is steadier with 99 ms, Responsive follows closer with 56 ms. Positions scale beta by 1 cm ≈ 2°
    /// at arm length; face values are kept fast so blinks survive. Positions and face values are
    /// provisional until they are tuned on recorded camera data (tracking detailed design 17).
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

        public static SmoothingProfile For(SmoothingPreset preset)
        {
            switch (preset)
            {
                case SmoothingPreset.Smooth:
                    return new SmoothingProfile(
                        new OneEuroParameters(0.05f, 0.003f),
                        new OneEuroParameters(0.05f, 0.6f),
                        new OneEuroParameters(0.5f, 0.5f));
                case SmoothingPreset.Responsive:
                    return new SmoothingProfile(
                        new OneEuroParameters(0.2f, 0.01f),
                        new OneEuroParameters(0.2f, 2f),
                        new OneEuroParameters(2f, 2f));
                default:
                    return new SmoothingProfile(
                        new OneEuroParameters(0.1f, 0.005f),
                        new OneEuroParameters(0.1f, 1f),
                        new OneEuroParameters(1f, 1f));
            }
        }
    }
}
