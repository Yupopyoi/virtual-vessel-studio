using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VirtualVessel.Core.State;

namespace VirtualVessel.UI.Lighting
{
    /// <summary>
    /// The stage's lights as the settings screen sees them (Stage detailed design 7.3,
    /// docs/&lt;lang&gt;/ui/screens/setup.md 15). Stage implements it: it owns the lights, applies a change
    /// at once, and saves it.
    /// </summary>
    public interface IStageLighting
    {
        IReadOnlyObservable<StageLighting> Lighting { get; }

        /// <summary>Applies <paramref name="lighting"/> at once, within the limits (<see cref="StageLighting.Normalized"/>), and saves it.</summary>
        void SetLighting(StageLighting lighting);
    }

    /// <summary>A light that lights the whole stage from one direction, or a spot aimed at the avatar.</summary>
    public enum StageLightKind
    {
        Directional = 0,
        Spot = 1,
    }

    /// <summary>
    /// One light, placed around the avatar: angles seen from the camera and, for a spot, its distance.
    /// Immutable; the With methods return a changed copy.
    /// </summary>
    public sealed class StageLight : IEquatable<StageLight>
    {
        public const float MinYaw = -180f;
        public const float MaxYaw = 180f;

        /// <summary>Slightly from below, for a rim or a fill; not under the floor.</summary>
        public const float MinPitch = -30f;

        public const float MaxPitch = 90f;
        public const float MinDistance = 0.5f;
        public const float MaxDistance = 5f;

        /// <summary>Off is a light removed, so the dimmest is still a little light.</summary>
        public const float MinIntensity = 0.1f;

        public const float MaxIntensity = 2f;
        public const float MinTemperature = 2700f;
        public const float MaxTemperature = 9000f;
        public const float MinSpotAngle = 10f;
        public const float MaxSpotAngle = 120f;

        public StageLight(StageLightKind kind, float yaw, float pitch, float distance, float intensity, string colorId, float temperature, float spotAngle)
        {
            Kind = kind;
            Yaw = yaw;
            Pitch = pitch;
            Distance = distance;
            Intensity = intensity;
            ColorId = colorId;
            Temperature = temperature;
            SpotAngle = spotAngle;
        }

        /// <summary>
        /// The light the stage started with: from above the camera and a little to its left (Stage
        /// detailed design 7.1), white at daylight.
        /// </summary>
        public static StageLight Default => new StageLight(StageLightKind.Directional, -15f, 35f, 2f, 1f, StageLightColors.White, 6500f, 50f);

        /// <summary>A spot added by the user: from the right, at the face's height, to light the other side.</summary>
        public static StageLight Added => new StageLight(StageLightKind.Spot, 45f, 15f, 2f, 1f, StageLightColors.White, 6500f, 50f);

        public StageLightKind Kind { get; }

        /// <summary>Degrees around the avatar seen from the camera: 0 in front, positive on the screen's right.</summary>
        public float Yaw { get; }

        /// <summary>Degrees above the avatar's face: 0 level, 90 straight above.</summary>
        public float Pitch { get; }

        /// <summary>Meters from the avatar's face; only a spot uses it.</summary>
        public float Distance { get; }

        /// <summary>1 is the light as the stage started with it.</summary>
        public float Intensity { get; }

        /// <summary>One of <see cref="StageLightColors"/>; white follows <see cref="Temperature"/>.</summary>
        public string ColorId { get; }

        /// <summary>Kelvin of the white light: lower is warmer.</summary>
        public float Temperature { get; }

        /// <summary>The spot's cone in degrees.</summary>
        public float SpotAngle { get; }

        public StageLight WithKind(StageLightKind kind) => new StageLight(kind, Yaw, Pitch, Distance, Intensity, ColorId, Temperature, SpotAngle);

        public StageLight WithYaw(float yaw) => new StageLight(Kind, yaw, Pitch, Distance, Intensity, ColorId, Temperature, SpotAngle);

        public StageLight WithPitch(float pitch) => new StageLight(Kind, Yaw, pitch, Distance, Intensity, ColorId, Temperature, SpotAngle);

        public StageLight WithDistance(float distance) => new StageLight(Kind, Yaw, Pitch, distance, Intensity, ColorId, Temperature, SpotAngle);

        public StageLight WithIntensity(float intensity) => new StageLight(Kind, Yaw, Pitch, Distance, intensity, ColorId, Temperature, SpotAngle);

        public StageLight WithColor(string colorId) => new StageLight(Kind, Yaw, Pitch, Distance, Intensity, colorId, Temperature, SpotAngle);

        public StageLight WithTemperature(float temperature) => new StageLight(Kind, Yaw, Pitch, Distance, Intensity, ColorId, temperature, SpotAngle);

        public StageLight WithSpotAngle(float spotAngle) => new StageLight(Kind, Yaw, Pitch, Distance, Intensity, ColorId, Temperature, spotAngle);

        /// <summary>
        /// This light within the limits: a value that is not a number, such as from a hand-edited file,
        /// becomes the default's, an unknown color white, and a value out of range the nearest end. Pure.
        /// </summary>
        public StageLight Normalized()
        {
            StageLight d = Default;
            StageLightKind kind = Enum.IsDefined(typeof(StageLightKind), Kind) ? Kind : d.Kind;
            return new StageLight(
                kind,
                Clamp(Yaw, MinYaw, MaxYaw, d.Yaw),
                Clamp(Pitch, MinPitch, MaxPitch, d.Pitch),
                Clamp(Distance, MinDistance, MaxDistance, d.Distance),
                Clamp(Intensity, MinIntensity, MaxIntensity, d.Intensity),
                StageLightColors.IsKnown(ColorId) ? ColorId : StageLightColors.White,
                Clamp(Temperature, MinTemperature, MaxTemperature, d.Temperature),
                Clamp(SpotAngle, MinSpotAngle, MaxSpotAngle, d.SpotAngle));
        }

        /// <summary><paramref name="value"/> within the range; <paramref name="fallback"/> for a value that is not a number. Pure.</summary>
        public static float Clamp(float value, float min, float max, float fallback)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Min(max, Math.Max(min, value));
        }

        public bool Equals(StageLight other)
        {
            return other != null && Kind == other.Kind && Yaw == other.Yaw && Pitch == other.Pitch && Distance == other.Distance
                && Intensity == other.Intensity && ColorId == other.ColorId && Temperature == other.Temperature && SpotAngle == other.SpotAngle;
        }

        public override bool Equals(object obj) => Equals(obj as StageLight);

        public override int GetHashCode() => HashCode.Combine(Kind, Yaw, Pitch, Distance, Intensity, ColorId, Temperature, SpotAngle);
    }

    /// <summary>All of the stage's lights and the ambient light. Immutable.</summary>
    public sealed class StageLighting : IEquatable<StageLighting>
    {
        /// <summary>
        /// The render pipeline lights an object with the main light and up to four more per object; four in
        /// all keeps every light on the avatar.
        /// </summary>
        public const int MaxLights = 4;

        public const float MinAmbient = 0f;
        public const float MaxAmbient = 2f;
        public const float DefaultAmbient = 1f;

        public StageLighting(IReadOnlyList<StageLight> lights, float ambient)
        {
            Lights = lights?.ToArray() ?? Array.Empty<StageLight>();
            Ambient = ambient;
        }

        /// <summary>The stage as it started: one light, and the scene's ambient light as it is.</summary>
        public static StageLighting Default => new StageLighting(new[] { StageLight.Default }, DefaultAmbient);

        /// <summary>One to <see cref="MaxLights"/> lights.</summary>
        public IReadOnlyList<StageLight> Lights { get; }

        /// <summary>A scale of the scene's ambient light, which lights the shade: 1 is as the scene has it.</summary>
        public float Ambient { get; }

        public bool CanAdd => Lights.Count < MaxLights;

        public bool CanRemove => Lights.Count > 1;

        public StageLighting WithLight(int index, StageLight light)
        {
            StageLight[] lights = Lights.ToArray();
            lights[index] = light;
            return new StageLighting(lights, Ambient);
        }

        /// <summary>With <paramref name="light"/> added at the end; unchanged when there are <see cref="MaxLights"/>. Pure.</summary>
        public StageLighting WithAdded(StageLight light)
        {
            return CanAdd ? new StageLighting(Lights.Append(light).ToArray(), Ambient) : this;
        }

        /// <summary>Without the light at <paramref name="index"/>; unchanged when it is the last one. Pure.</summary>
        public StageLighting WithRemoved(int index)
        {
            return CanRemove ? new StageLighting(Lights.Where((_, i) => i != index).ToArray(), Ambient) : this;
        }

        public StageLighting WithAmbient(float ambient) => new StageLighting(Lights, ambient);

        /// <summary>
        /// This lighting within the limits: each light normalized, at least one light (the default when
        /// none), at most <see cref="MaxLights"/>, and the ambient light in range. Pure.
        /// </summary>
        public StageLighting Normalized()
        {
            StageLight[] lights = Lights.Where(light => light != null).Take(MaxLights).Select(light => light.Normalized()).ToArray();
            if (lights.Length == 0)
            {
                lights = new[] { StageLight.Default };
            }

            return new StageLighting(lights, StageLight.Clamp(Ambient, MinAmbient, MaxAmbient, DefaultAmbient));
        }

        public bool Equals(StageLighting other)
        {
            return other != null && Ambient == other.Ambient && Lights.SequenceEqual(other.Lights);
        }

        public override bool Equals(object obj) => Equals(obj as StageLighting);

        public override int GetHashCode() => HashCode.Combine(Ambient, Lights.Count);
    }

    /// <summary>The colors a light can have: white at a chosen temperature, or one of a few stage colors.</summary>
    public static class StageLightColors
    {
        public const string White = "white";
        public const string Pink = "pink";
        public const string Orange = "orange";
        public const string Yellow = "yellow";
        public const string Green = "green";
        public const string Sky = "sky";
        public const string Blue = "blue";
        public const string Purple = "purple";

        /// <summary>In the order the settings screen lists them.</summary>
        public static readonly IReadOnlyList<string> Ids = new[] { White, Pink, Orange, Yellow, Green, Sky, Blue, Purple };

        // Light, not saturated: a colored light on a face reads as a tint, and a full color hides the face.
        private static readonly Dictionary<string, Color> s_colors = new Dictionary<string, Color>
        {
            [Pink] = new Color(1f, 0.62f, 0.8f),
            [Orange] = new Color(1f, 0.68f, 0.38f),
            [Yellow] = new Color(1f, 0.92f, 0.5f),
            [Green] = new Color(0.6f, 1f, 0.66f),
            [Sky] = new Color(0.6f, 0.86f, 1f),
            [Blue] = new Color(0.5f, 0.6f, 1f),
            [Purple] = new Color(0.76f, 0.58f, 1f),
        };

        public static bool IsKnown(string id) => id == White || (id != null && s_colors.ContainsKey(id));

        /// <summary>The light's color: white by its temperature, else the stage color. Pure.</summary>
        public static Color ColorOf(StageLight light)
        {
            if (light == null)
            {
                throw new ArgumentNullException(nameof(light));
            }

            return light.ColorId != null && s_colors.TryGetValue(light.ColorId, out Color color) ? color : FromTemperature(light.Temperature);
        }

        /// <summary>
        /// The color of white light at <paramref name="kelvin"/>, scaled so its brightest channel is 1 (Tanner
        /// Helland's fit to black-body colors, good from 1000 to 40000 K). Pure.
        /// </summary>
        public static Color FromTemperature(float kelvin)
        {
            double t = Math.Min(40000.0, Math.Max(1000.0, kelvin)) / 100.0;
            double r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
            double g = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
            double b = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
            return new Color(Channel(r), Channel(g), Channel(b));
        }

        private static float Channel(double value)
        {
            return (float)(Math.Min(255.0, Math.Max(0.0, value)) / 255.0);
        }
    }
}
