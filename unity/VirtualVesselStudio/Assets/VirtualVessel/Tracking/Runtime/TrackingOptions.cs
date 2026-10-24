using System;

namespace VirtualVessel.Tracking
{
    /// <summary>
    /// What the user chose for tracking (tracking detailed design 10), as one value: the parts that are
    /// tracked, how much of the body moves, the smoothing level, mirroring, and whether the body moves in
    /// depth. Changes are made with the With methods and given back to <see cref="ITrackingSource.SetOptions"/> whole.
    /// </summary>
    public readonly struct TrackingOptions : IEquatable<TrackingOptions>
    {
        public TrackingOptions(TrackingRegions regions, BodyRange bodyRange, int smoothing, bool mirror, BodyDepth bodyDepth = BodyDepth.Tracked)
        {
            Regions = regions & TrackingRegions.All;
            BodyRange = Available(bodyRange);
            Smoothing = SmoothingLevel.Clamp(smoothing);
            Mirror = mirror;
            BodyDepth = bodyDepth == BodyDepth.Flat ? BodyDepth.Flat : BodyDepth.Tracked;
        }

        /// <summary>Everything tracked, upper body, the default smoothing level, mirrored, in depth (3D).</summary>
        public static TrackingOptions Default => new TrackingOptions(TrackingRegions.All, BodyRange.UpperBody, SmoothingLevel.Default, mirror: true);

        /// <summary>The parts tracked; inference for the others does not run.</summary>
        public TrackingRegions Regions { get; }

        public BodyRange BodyRange { get; }

        /// <summary>The smoothing level, <see cref="SmoothingLevel.Min"/> (steadiest) to <see cref="SmoothingLevel.Max"/> (fastest).</summary>
        public int Smoothing { get; }

        /// <summary>
        /// Shown and moved as in a mirror: the camera image on the setup screen is flipped, and the
        /// avatar raises its left hand for the person's right (Avatar detailed design 6.6). Observations
        /// keep the person's own sides either way.
        /// </summary>
        public bool Mirror { get; }

        /// <summary>
        /// Whether the body moves toward and away from the camera. The camera sees depth poorly, so a user
        /// may keep only what it sees well (2D). The face and hands are not affected.
        /// </summary>
        public BodyDepth BodyDepth { get; }

        /// <summary>
        /// The range that can be used: full body is shown as coming soon and cannot be chosen yet
        /// (tracking detailed design 6.5), so a value saved by a later version falls back to the upper body. Pure.
        /// </summary>
        public static BodyRange Available(BodyRange range)
        {
            return range == BodyRange.UpperBody ? range : BodyRange.UpperBody;
        }

        public TrackingOptions WithRegions(TrackingRegions regions)
        {
            return new TrackingOptions(regions, BodyRange, Smoothing, Mirror, BodyDepth);
        }

        public TrackingOptions WithBodyRange(BodyRange bodyRange)
        {
            return new TrackingOptions(Regions, bodyRange, Smoothing, Mirror, BodyDepth);
        }

        public TrackingOptions WithSmoothing(int smoothing)
        {
            return new TrackingOptions(Regions, BodyRange, smoothing, Mirror, BodyDepth);
        }

        public TrackingOptions WithMirror(bool mirror)
        {
            return new TrackingOptions(Regions, BodyRange, Smoothing, mirror, BodyDepth);
        }

        public TrackingOptions WithBodyDepth(BodyDepth bodyDepth)
        {
            return new TrackingOptions(Regions, BodyRange, Smoothing, Mirror, bodyDepth);
        }

        public bool Equals(TrackingOptions other)
        {
            return Regions == other.Regions && BodyRange == other.BodyRange && Smoothing == other.Smoothing && Mirror == other.Mirror && BodyDepth == other.BodyDepth;
        }

        public override bool Equals(object obj)
        {
            return obj is TrackingOptions other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)Regions * 31) ^ ((int)BodyRange << 8) ^ (Smoothing << 12) ^ (Mirror ? 1 << 17 : 0) ^ ((int)BodyDepth << 18);
        }

        public override string ToString()
        {
            return $"{Regions}, {BodyRange}, smoothing {Smoothing}{(Mirror ? ", mirrored" : string.Empty)}{(BodyDepth == BodyDepth.Flat ? ", flat (2D)" : string.Empty)}";
        }
    }
}
