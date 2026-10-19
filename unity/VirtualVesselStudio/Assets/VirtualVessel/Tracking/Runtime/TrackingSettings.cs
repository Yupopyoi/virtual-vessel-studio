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
    /// The camera device is a capture setting (capture-settings.json). The user's choices are read and
    /// written as <see cref="TrackingOptions"/>.
    /// </summary>
    public sealed class TrackingSettings
    {
        public const int SchemaVersion = 1;
        public const string FileName = "tracking-settings.json";

        public string Provider { get; set; } = "mediapipe";

        public TrackingRegions Regions { get; set; } = TrackingRegions.All;

        public BodyRange BodyRange { get; set; } = BodyRange.UpperBody;

        public SmoothingPreset Smoothing { get; set; } = SmoothingPreset.Standard;

        /// <summary>Takes effect when tracking starts.</summary>
        public PoseModel PoseModel { get; set; } = PoseModel.Full;

        public bool Mirror { get; set; } = true;

        /// <summary>Whether tracking was started; it starts again at the next start.</summary>
        public bool Running { get; set; }

        public TrackingSettings Clone()
        {
            return (TrackingSettings)MemberwiseClone();
        }

        /// <summary>The user's choices in these settings. A method, not a property, so it is not written to the file. Pure.</summary>
        public TrackingOptions GetOptions()
        {
            return new TrackingOptions(Regions, BodyRange, Smoothing, Mirror);
        }

        /// <summary>A copy with <paramref name="options"/> and <paramref name="running"/>; other values are kept. Pure.</summary>
        public TrackingSettings With(TrackingOptions options, bool running)
        {
            TrackingSettings next = Clone();
            next.Regions = options.Regions;
            next.BodyRange = options.BodyRange;
            next.Smoothing = options.Smoothing;
            next.Mirror = options.Mirror;
            next.Running = running;
            return next;
        }
    }
}
