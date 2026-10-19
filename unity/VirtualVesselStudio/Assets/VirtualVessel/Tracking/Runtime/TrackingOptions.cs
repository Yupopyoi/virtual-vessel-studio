using System;

namespace VirtualVessel.Tracking
{
    /// <summary>
    /// What the user chose for tracking (tracking detailed design 10), as one value: the parts that are
    /// tracked, how much of the body moves, the smoothing level, and mirroring. Changes are made with
    /// the With methods and given back to <see cref="ITrackingSource.SetOptions"/> whole.
    /// </summary>
    public readonly struct TrackingOptions : IEquatable<TrackingOptions>
    {
        public TrackingOptions(TrackingRegions regions, BodyRange bodyRange, SmoothingPreset smoothing, bool mirror)
        {
            Regions = regions & TrackingRegions.All;
            BodyRange = Available(bodyRange);
            Smoothing = smoothing;
            Mirror = mirror;
        }

        /// <summary>Everything tracked, upper body, standard smoothing, mirrored.</summary>
        public static TrackingOptions Default => new TrackingOptions(TrackingRegions.All, BodyRange.UpperBody, SmoothingPreset.Standard, mirror: true);

        /// <summary>The parts tracked; inference for the others does not run.</summary>
        public TrackingRegions Regions { get; }

        public BodyRange BodyRange { get; }

        public SmoothingPreset Smoothing { get; }

        /// <summary>
        /// Shown and moved as in a mirror: the camera image on the setup screen is flipped, and the
        /// avatar raises its left hand for the person's right (Avatar detailed design 6.6). Observations
        /// keep the person's own sides either way.
        /// </summary>
        public bool Mirror { get; }

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
            return new TrackingOptions(regions, BodyRange, Smoothing, Mirror);
        }

        public TrackingOptions WithBodyRange(BodyRange bodyRange)
        {
            return new TrackingOptions(Regions, bodyRange, Smoothing, Mirror);
        }

        public TrackingOptions WithSmoothing(SmoothingPreset smoothing)
        {
            return new TrackingOptions(Regions, BodyRange, smoothing, Mirror);
        }

        public TrackingOptions WithMirror(bool mirror)
        {
            return new TrackingOptions(Regions, BodyRange, Smoothing, mirror);
        }

        public bool Equals(TrackingOptions other)
        {
            return Regions == other.Regions && BodyRange == other.BodyRange && Smoothing == other.Smoothing && Mirror == other.Mirror;
        }

        public override bool Equals(object obj)
        {
            return obj is TrackingOptions other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)Regions * 31) ^ ((int)BodyRange << 8) ^ ((int)Smoothing << 12) ^ (Mirror ? 1 << 15 : 0);
        }

        public override string ToString()
        {
            return $"{Regions}, {BodyRange}, {Smoothing}{(Mirror ? ", mirrored" : string.Empty)}";
        }
    }
}
