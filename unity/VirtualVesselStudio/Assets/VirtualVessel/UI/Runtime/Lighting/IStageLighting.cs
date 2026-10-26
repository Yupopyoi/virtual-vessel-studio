using System;
using VirtualVessel.Core.State;

namespace VirtualVessel.UI.Lighting
{
    /// <summary>
    /// How bright the stage's light is, as the settings screen sees it (Stage detailed design 7.1,
    /// docs/&lt;lang&gt;/ui/screens/setup.md 9). Stage implements it: it owns the light, applies a change
    /// at once, and saves it.
    /// </summary>
    public interface IStageLighting
    {
        /// <summary>A scale of the stage's light: 1 is the light as made, within <see cref="StageLightIntensity"/>'s range.</summary>
        IReadOnlyObservable<float> Intensity { get; }

        /// <summary>Applies <paramref name="intensity"/> to the light at once, clamped to the range, and saves it.</summary>
        void SetIntensity(float intensity);
    }

    /// <summary>The range of <see cref="IStageLighting.Intensity"/>, shared by Stage and the settings screen.</summary>
    public static class StageLightIntensity
    {
        /// <summary>Dim, but the lit side of the avatar still differs from its shade.</summary>
        public const float Min = 0.1f;

        public const float Max = 1.5f;

        public const float Default = 1f;

        /// <summary>
        /// <paramref name="intensity"/> within the range; the default for a value that is not a number,
        /// such as from a hand-edited file. Pure.
        /// </summary>
        public static float Clamp(float intensity)
        {
            return float.IsNaN(intensity) || float.IsInfinity(intensity) ? Default : Math.Min(Max, Math.Max(Min, intensity));
        }
    }
}
