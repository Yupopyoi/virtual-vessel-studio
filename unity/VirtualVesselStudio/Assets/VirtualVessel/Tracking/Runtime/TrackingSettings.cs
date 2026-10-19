using System;

namespace VirtualVessel.Tracking
{
    /// <summary>The Pose model: lighter runs faster, heavier is more accurate (tracking detailed design 15).</summary>
    public enum PoseModel
    {
        Lite,
        Full,
        Heavy,
    }

    /// <summary>
    /// What tracking starts with, as written in tracking-settings.json (tracking detailed design 10).
    /// The camera device is a capture setting (capture-settings.json).
    /// </summary>
    public sealed class TrackingSettings
    {
        public const int SchemaVersion = 1;
        public const string FileName = "tracking-settings.json";

        public string Provider { get; set; } = "mediapipe";

        /// <summary>Enabled parts; inference for the others does not run.</summary>
        public TrackingRegions Regions { get; set; } = TrackingRegions.All;

        public BodyRange BodyRange { get; set; } = BodyRange.UpperBody;

        public SmoothingPreset Smoothing { get; set; } = SmoothingPreset.Standard;

        public PoseModel PoseModel { get; set; } = PoseModel.Full;

        /// <summary>Shown and moved as in a mirror (Avatar detailed design 6.6).</summary>
        public bool Mirror { get; set; } = true;

        /// <summary>Whether tracking was started; it starts again at the next start.</summary>
        public bool Running { get; set; }

        public TrackingSettings Clone()
        {
            return (TrackingSettings)MemberwiseClone();
        }
    }

    /// <summary>The user's choices compared every frame to find changes to save. A value type, so comparing allocates nothing.</summary>
    internal readonly struct TrackingChoices : IEquatable<TrackingChoices>
    {
        public TrackingChoices(TrackingRegions regions, BodyRange bodyRange, SmoothingPreset smoothing, bool mirror, bool running)
        {
            Mirror = mirror;
            Regions = regions;
            BodyRange = bodyRange;
            Smoothing = smoothing;
            Running = running;
        }

        public TrackingRegions Regions { get; }

        public BodyRange BodyRange { get; }

        public SmoothingPreset Smoothing { get; }

        public bool Mirror { get; }

        public bool Running { get; }

        public static TrackingChoices From(TrackingSettings settings)
        {
            return new TrackingChoices(settings.Regions, settings.BodyRange, settings.Smoothing, settings.Mirror, settings.Running);
        }

        /// <summary>A copy of <paramref name="settings"/> with these choices; other values are kept. Pure.</summary>
        public TrackingSettings ApplyTo(TrackingSettings settings)
        {
            TrackingSettings next = settings.Clone();
            next.Regions = Regions;
            next.BodyRange = BodyRange;
            next.Smoothing = Smoothing;
            next.Mirror = Mirror;
            next.Running = Running;
            return next;
        }

        public bool Equals(TrackingChoices other)
        {
            return Regions == other.Regions && BodyRange == other.BodyRange && Smoothing == other.Smoothing && Mirror == other.Mirror && Running == other.Running;
        }

        public override bool Equals(object obj)
        {
            return obj is TrackingChoices other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)Regions * 31) ^ ((int)BodyRange << 8) ^ ((int)Smoothing << 12) ^ (Mirror ? 1 << 15 : 0) ^ (Running ? 1 << 16 : 0);
        }
    }
}
