using System.Collections.Generic;
using VirtualVessel.ProjectData.Config;

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
        /// <summary>2: the three smoothing presets became ten levels (smoothing, 0 to 2, became smoothingLevel, 1 to 10).</summary>
        public const int SchemaVersion = 2;
        public const string FileName = "tracking-settings.json";

        public string Provider { get; set; } = "mediapipe";

        public TrackingRegions Regions { get; set; } = TrackingRegions.All;

        public BodyRange BodyRange { get; set; } = BodyRange.UpperBody;

        public int SmoothingLevel { get; set; } = Tracking.SmoothingLevel.Default;

        /// <summary>Takes effect when tracking starts.</summary>
        public PoseModel PoseModel { get; set; } = PoseModel.Full;

        public bool Mirror { get; set; } = true;

        /// <summary>Whether tracking was started; it starts again at the next start.</summary>
        public bool Running { get; set; }

        /// <summary>The steps from older files to <see cref="SchemaVersion"/>, for the settings store.</summary>
        public static IReadOnlyList<ConfigMigration> Migrations { get; } = new[]
        {
            new ConfigMigration(1, document =>
            {
                if (document.Contains("smoothing"))
                {
                    document.Set("smoothingLevel", LevelOfPreset(document.Get("smoothing", 1)));
                    document.Remove("smoothing");
                }
            }),
        };

        /// <summary>
        /// The level of a version 1 preset: Smooth (0), Standard (1), and Responsive (2) keep their
        /// measured values as levels 3, 5, and 7; anything else is the default. Pure.
        /// </summary>
        public static int LevelOfPreset(int preset)
        {
            return preset switch
            {
                0 => Tracking.SmoothingLevel.Smooth,
                2 => Tracking.SmoothingLevel.Responsive,
                _ => Tracking.SmoothingLevel.Standard,
            };
        }

        public TrackingSettings Clone()
        {
            return (TrackingSettings)MemberwiseClone();
        }

        /// <summary>The user's choices in these settings. A method, not a property, so it is not written to the file. Pure.</summary>
        public TrackingOptions GetOptions()
        {
            return new TrackingOptions(Regions, BodyRange, SmoothingLevel, Mirror);
        }

        /// <summary>A copy with <paramref name="options"/> and <paramref name="running"/>; other values are kept. Pure.</summary>
        public TrackingSettings With(TrackingOptions options, bool running)
        {
            TrackingSettings next = Clone();
            next.Regions = options.Regions;
            next.BodyRange = options.BodyRange;
            next.SmoothingLevel = options.Smoothing;
            next.Mirror = options.Mirror;
            next.Running = running;
            return next;
        }
    }
}
