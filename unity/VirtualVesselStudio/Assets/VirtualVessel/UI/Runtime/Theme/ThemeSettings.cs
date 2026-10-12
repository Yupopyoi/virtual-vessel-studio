using System;

namespace VirtualVessel.UI.Theme
{
    public enum ThemeBackground
    {
        Dark,
        Light,
    }

    /// <summary>The appearance a user chooses (design-system.md chapter 8).</summary>
    public readonly struct ThemeSettings : IEquatable<ThemeSettings>
    {
        public const string DefaultPalette = "lavender";

        public ThemeSettings(string palette, ThemeBackground background, bool reducedMotion)
        {
            Palette = string.IsNullOrEmpty(palette) ? DefaultPalette : palette;
            Background = background;
            ReducedMotion = reducedMotion;
        }

        public static ThemeSettings Default => new ThemeSettings(DefaultPalette, ThemeBackground.Dark, reducedMotion: false);

        /// <summary>Palette id from design-tokens.json, such as "lavender".</summary>
        public string Palette { get; }

        public ThemeBackground Background { get; }

        public bool ReducedMotion { get; }

        public ThemeSettings WithPalette(string palette)
        {
            return new ThemeSettings(palette, Background, ReducedMotion);
        }

        public ThemeSettings WithBackground(ThemeBackground background)
        {
            return new ThemeSettings(Palette, background, ReducedMotion);
        }

        public ThemeSettings WithReducedMotion(bool reducedMotion)
        {
            return new ThemeSettings(Palette, Background, reducedMotion);
        }

        public bool Equals(ThemeSettings other)
        {
            return string.Equals(Palette, other.Palette, StringComparison.Ordinal) && Background == other.Background && ReducedMotion == other.ReducedMotion;
        }

        public override bool Equals(object obj)
        {
            return obj is ThemeSettings other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Palette?.GetHashCode() ?? 0) * 397) ^ ((int)Background * 31) ^ (ReducedMotion ? 1 : 0);
            }
        }
    }
}
