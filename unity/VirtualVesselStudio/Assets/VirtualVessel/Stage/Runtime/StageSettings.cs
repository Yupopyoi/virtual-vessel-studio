using System;
using System.Collections.Generic;
using System.Linq;
using VirtualVessel.Core.Geometry;
using VirtualVessel.UI.Lighting;

namespace VirtualVessel.Stage
{
    /// <summary>
    /// The placements and the light as written in stage-settings.json (Stage detailed design 6.3).
    /// Property names become the camelCase JSON field names; placements of ids this version does not
    /// know are kept.
    /// </summary>
    public sealed class StageSettings
    {
        public const int SchemaVersion = 1;
        public const string FileName = "stage-settings.json";

        public Dictionary<string, ScreenSettings> Screens { get; set; } = new Dictionary<string, ScreenSettings>();

        /// <summary>
        /// The scale of the one light of version 0.3 (Stage detailed design 7.1). Read while
        /// <see cref="Lights"/> is null, as the first light's brightness; written as null once the lights are saved.
        /// </summary>
        public float? LightIntensity { get; set; }

        /// <summary>The lights the user set (7.3); null until changed, so the default reaches users who never changed it.</summary>
        public List<LightSettings> Lights { get; set; }

        /// <summary>The scale of the scene's ambient light (7.3); null until changed.</summary>
        public float? AmbientLight { get; set; }

        public StageSettings Clone()
        {
            var copy = new StageSettings
            {
                LightIntensity = LightIntensity,
                Lights = Lights?.Select(light => light?.Clone()).ToList(),
                AmbientLight = AmbientLight,
            };
            foreach (KeyValuePair<string, ScreenSettings> pair in Screens ?? new Dictionary<string, ScreenSettings>())
            {
                copy.Screens[pair.Key] = pair.Value?.Clone();
            }

            return copy;
        }
    }

    /// <summary>One video's placement in the file: rectangles as [x, y, width, height] on the 0 to 1 stream screen.</summary>
    public sealed class ScreenSettings
    {
        public double[] Placement { get; set; }

        public double[] Crop { get; set; }

        public bool FlipHorizontal { get; set; }

        public bool Locked { get; set; }

        public bool AllowOutside { get; set; }

        public int Order { get; set; }

        public static ScreenSettings From(FrameLayout layout)
        {
            return new ScreenSettings
            {
                Placement = ToArray(layout.Placement),
                Crop = ToArray(layout.Crop),
                FlipHorizontal = layout.FlipHorizontal,
                Locked = layout.Locked,
                AllowOutside = layout.AllowOutside,
                Order = layout.Order,
            };
        }

        /// <summary>The layout this entry describes, or null when it cannot be used (a hand-edited or damaged file).</summary>
        public FrameLayout? ToLayout()
        {
            NormalizedRect? placement = ToRect(Placement);
            NormalizedRect? crop = ToRect(Crop);
            if (placement == null || crop == null)
            {
                return null;
            }

            var layout = new FrameLayout(placement.Value, crop.Value, FlipHorizontal, Locked, AllowOutside, Order);
            return ScreenPlacementMath.IsUsable(layout) ? layout : (FrameLayout?)null;
        }

        public ScreenSettings Clone()
        {
            var copy = (ScreenSettings)MemberwiseClone();
            copy.Placement = (double[])Placement?.Clone();
            copy.Crop = (double[])Crop?.Clone();
            return copy;
        }

        private static double[] ToArray(NormalizedRect rect)
        {
            return new[] { rect.X, rect.Y, rect.Width, rect.Height };
        }

        private static NormalizedRect? ToRect(double[] values)
        {
            if (values == null || values.Length != 4 || Array.Exists(values, v => double.IsNaN(v) || double.IsInfinity(v)))
            {
                return null;
            }

            return new NormalizedRect(values[0], values[1], values[2], values[3]);
        }
    }

    /// <summary>
    /// One light in the file (Stage detailed design 6.3). A missing or damaged field takes the default
    /// light's value, so a hand-edited file still loads.
    /// </summary>
    public sealed class LightSettings
    {
        public const string DirectionalKind = "directional";
        public const string SpotKind = "spot";

        public string Kind { get; set; }

        public float? Yaw { get; set; }

        public float? Pitch { get; set; }

        public float? Distance { get; set; }

        public float? Intensity { get; set; }

        public string Color { get; set; }

        public float? Temperature { get; set; }

        public float? SpotAngle { get; set; }

        public static LightSettings From(StageLight light)
        {
            return new LightSettings
            {
                Kind = light.Kind == StageLightKind.Spot ? SpotKind : DirectionalKind,
                Yaw = light.Yaw,
                Pitch = light.Pitch,
                Distance = light.Distance,
                Intensity = light.Intensity,
                Color = light.ColorId,
                Temperature = light.Temperature,
                SpotAngle = light.SpotAngle,
            };
        }

        /// <summary>The light this entry describes, within the limits. Pure.</summary>
        public StageLight ToLight()
        {
            StageLight d = StageLight.Default;
            StageLightKind kind = Kind == SpotKind ? StageLightKind.Spot : StageLightKind.Directional;
            return new StageLight(kind, Yaw ?? d.Yaw, Pitch ?? d.Pitch, Distance ?? d.Distance, Intensity ?? d.Intensity, Color ?? d.ColorId, Temperature ?? d.Temperature, SpotAngle ?? d.SpotAngle).Normalized();
        }

        public LightSettings Clone()
        {
            return (LightSettings)MemberwiseClone();
        }
    }
}
