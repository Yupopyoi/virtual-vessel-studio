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
        /// <summary>
        /// 2: the three smoothing presets became ten levels (smoothing, 0 to 2, became smoothingLevel, 1 to 10).
        /// 3: the levels moved two steps, so the former 3 is the standard 5 (2026-10-24).
        /// 4: the levels moved four more steps, so version 3's 1 is the standard 5 (2026-10-24).
        /// </summary>
        public const int SchemaVersion = 4;

        // The levels of version 2, which the steps from older files work in.
        private const int Version2Smooth = 3;
        private const int Version2Standard = 5;
        private const int Version2Responsive = 7;
        private const int Version3Standard = 5;
        public const string FileName = "tracking-settings.json";

        public string Provider { get; set; } = "mediapipe";

        public TrackingRegions Regions { get; set; } = TrackingRegions.All;

        public BodyRange BodyRange { get; set; } = BodyRange.UpperBody;

        public int SmoothingLevel { get; set; } = Tracking.SmoothingLevel.Default;

        /// <summary>Loaded when tracking starts; a change while running restarts it.</summary>
        public PoseModel PoseModel { get; set; } = PoseModel.Full;

        public bool Mirror { get; set; } = true;

        /// <summary>Added in version 3 without a step: a file without it reads as tracked in depth (3D).</summary>
        public BodyDepth BodyDepth { get; set; } = BodyDepth.Tracked;

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
            new ConfigMigration(2, document =>
            {
                if (document.Contains("smoothingLevel"))
                {
                    document.Set("smoothingLevel", LevelOfVersion2(document.Get("smoothingLevel", Version2Standard)));
                }
            }),
            new ConfigMigration(3, document =>
            {
                if (document.Contains("smoothingLevel"))
                {
                    document.Set("smoothingLevel", LevelOfVersion3(document.Get("smoothingLevel", Version3Standard)));
                }
            }),
        };

        /// <summary>
        /// The version 2 level of a version 1 preset. Version 1 had no screen to choose one, so Standard (1)
        /// was never the user's choice and becomes the default; Smooth (0) and Responsive (2), set only by
        /// editing the file, keep their measured values as levels 3 and 7. Pure.
        /// </summary>
        public static int LevelOfPreset(int preset)
        {
            return preset switch
            {
                0 => Version2Smooth,
                2 => Version2Responsive,
                _ => Version2Standard,
            };
        }

        /// <summary>
        /// The version 3 level of a version 2 level: two steps up, so each keeps its motion. The standard 5
        /// was the default nearly everyone had, and the user asked for the steadier former 3 as the
        /// standard, so it becomes version 3's standard 5. The two quickest, 9 and 10, both become 10. Pure.
        /// </summary>
        public static int LevelOfVersion2(int level)
        {
            return level == Version2Standard ? Version3Standard : Tracking.SmoothingLevel.Clamp(level + 2);
        }

        /// <summary>
        /// The level of a version 3 level: four steps up, so each keeps its arms' motion. Version 3's
        /// standard 5, the untouched default, becomes the new standard 5, as the user asked for the
        /// steadier one. 6 to 10 all become 10. Pure.
        /// </summary>
        public static int LevelOfVersion3(int level)
        {
            return level == Version3Standard ? Tracking.SmoothingLevel.Standard : Tracking.SmoothingLevel.Clamp(level + 4);
        }

        public TrackingSettings Clone()
        {
            return (TrackingSettings)MemberwiseClone();
        }

        /// <summary>The user's choices in these settings. A method, not a property, so it is not written to the file. Pure.</summary>
        public TrackingOptions GetOptions()
        {
            return new TrackingOptions(Regions, BodyRange, SmoothingLevel, Mirror, BodyDepth, PoseModel);
        }

        /// <summary>A copy with <paramref name="options"/> and <paramref name="running"/>; other values are kept. Pure.</summary>
        public TrackingSettings With(TrackingOptions options, bool running)
        {
            TrackingSettings next = Clone();
            next.Regions = options.Regions;
            next.BodyRange = options.BodyRange;
            next.SmoothingLevel = options.Smoothing;
            next.Mirror = options.Mirror;
            next.BodyDepth = options.BodyDepth;
            next.PoseModel = options.PoseModel;
            next.Running = running;
            return next;
        }
    }
}
