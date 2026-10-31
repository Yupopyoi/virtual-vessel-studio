using System;

namespace VirtualVessel.UI.Settings
{
    /// <summary>
    /// The UI settings as written in ui-settings.json (UI foundation detailed design 8.1).
    /// </summary>
    /// <remarks>
    /// Values are kept exactly as written, even ones this version does not know, so that such a value
    /// survives until the user changes that setting. <see cref="UiSettingsResolver"/> decides what is
    /// actually used. Property names become the camelCase JSON field names.
    /// </remarks>
    public sealed class UiSettings
    {
        public const int SchemaVersion = 1;

        public const string FileName = "ui-settings.json";

        public string Language { get; set; }

        public string Palette { get; set; } = UiSettingsResolver.DefaultPalette;

        public string Background { get; set; } = UiSettingsResolver.Dark;

        public double UiScale { get; set; } = 1.0;

        public string ReducedMotion { get; set; } = UiSettingsResolver.MotionSystem;

        public string FontSet { get; set; } = UiSettingsResolver.DefaultFontSet;

        public bool DeveloperMode { get; set; }

        public string LastScreen { get; set; } = UiSettingsResolver.DefaultScreen;

        /// <summary>The page last open on the settings screen, or null for the first.</summary>
        public string LastSetupPage { get; set; }

        /// <summary>The settings for a first start, in <paramref name="language"/> chosen from the OS.</summary>
        public static UiSettings CreateDefault(string language)
        {
            return new UiSettings { Language = language };
        }

        public UiSettings Clone()
        {
            return (UiSettings)MemberwiseClone();
        }

        public static bool SameValues(UiSettings a, UiSettings b)
        {
            return string.Equals(a.Language, b.Language, StringComparison.Ordinal)
                && string.Equals(a.Palette, b.Palette, StringComparison.Ordinal)
                && string.Equals(a.Background, b.Background, StringComparison.Ordinal)
                && a.UiScale.Equals(b.UiScale)
                && string.Equals(a.ReducedMotion, b.ReducedMotion, StringComparison.Ordinal)
                && string.Equals(a.FontSet, b.FontSet, StringComparison.Ordinal)
                && a.DeveloperMode == b.DeveloperMode
                && string.Equals(a.LastScreen, b.LastScreen, StringComparison.Ordinal)
                && string.Equals(a.LastSetupPage, b.LastSetupPage, StringComparison.Ordinal);
        }
    }
}
