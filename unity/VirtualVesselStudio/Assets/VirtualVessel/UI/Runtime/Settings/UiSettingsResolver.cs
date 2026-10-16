using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using VirtualVessel.UI.Theme;

namespace VirtualVessel.UI.Settings
{
    /// <summary>How the reduce-motion setting is chosen (design-system.md 7).</summary>
    public enum ReducedMotionMode
    {
        /// <summary>Follow the Windows "Animation effects" setting.</summary>
        System,

        On,

        Off,
    }

    /// <summary>What the settings may choose from, and what the OS says, at the time of resolving.</summary>
    public sealed class UiSettingsChoices
    {
        public UiSettingsChoices(
            IEnumerable<string> palettes,
            IEnumerable<string> fontSets,
            IEnumerable<string> languages,
            string initialLanguage,
            bool systemPrefersReducedMotion)
        {
            Palettes = new HashSet<string>(palettes ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            FontSets = new HashSet<string>(fontSets ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            Languages = new HashSet<string>(languages ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            InitialLanguage = initialLanguage;
            SystemPrefersReducedMotion = systemPrefersReducedMotion;
        }

        public IReadOnlyCollection<string> Palettes { get; }

        public IReadOnlyCollection<string> FontSets { get; }

        public IReadOnlyCollection<string> Languages { get; }

        /// <summary>The language used while none is set, chosen from the OS language.</summary>
        public string InitialLanguage { get; }

        public bool SystemPrefersReducedMotion { get; }
    }

    /// <summary>The settings actually in use, after checking every value.</summary>
    public readonly struct ResolvedUiSettings : IEquatable<ResolvedUiSettings>
    {
        public ResolvedUiSettings(
            string language,
            ThemeSettings theme,
            ReducedMotionMode reducedMotionMode,
            string fontSet,
            float uiScale,
            bool developerMode,
            string lastScreen)
        {
            Language = language;
            Theme = theme;
            ReducedMotionMode = reducedMotionMode;
            FontSet = fontSet;
            UiScale = uiScale;
            DeveloperMode = developerMode;
            LastScreen = lastScreen;
        }

        public string Language { get; }

        /// <summary>Palette, background, and whether motion is reduced now (with "system" already resolved).</summary>
        public ThemeSettings Theme { get; }

        /// <summary>The choice itself, for showing it in the settings screen.</summary>
        public ReducedMotionMode ReducedMotionMode { get; }

        public string FontSet { get; }

        public float UiScale { get; }

        public bool DeveloperMode { get; }

        /// <summary>Kept as written; the screen host checks it against its screens.</summary>
        public string LastScreen { get; }

        public bool Equals(ResolvedUiSettings other)
        {
            return string.Equals(Language, other.Language, StringComparison.Ordinal)
                && Theme.Equals(other.Theme)
                && ReducedMotionMode == other.ReducedMotionMode
                && string.Equals(FontSet, other.FontSet, StringComparison.Ordinal)
                && UiScale.Equals(other.UiScale)
                && DeveloperMode == other.DeveloperMode
                && string.Equals(LastScreen, other.LastScreen, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is ResolvedUiSettings other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Language?.GetHashCode() ?? 0;
                hash = (hash * 397) ^ Theme.GetHashCode();
                hash = (hash * 397) ^ (FontSet?.GetHashCode() ?? 0);
                return (hash * 397) ^ UiScale.GetHashCode();
            }
        }
    }

    /// <summary>
    /// Decides the settings in use from the values in the file (UI foundation detailed design 8.1).
    /// </summary>
    /// <remarks>
    /// ui-settings.json can be edited by the user or by any program running as the user, so every value
    /// is checked against an allowed list, and anything else falls back to the default with a warning.
    /// The UI scale is limited to its four steps because an extreme value could make the UI unusable.
    /// </remarks>
    public static class UiSettingsResolver
    {
        public const string DefaultPalette = ThemeSettings.DefaultPalette;

        public const string DefaultFontSet = FontService.DefaultFontSet;

        public const string DefaultScreen = "live";

        public const string FallbackLanguage = "en";

        public const string Dark = "dark";

        public const string Light = "light";

        public const string MotionSystem = "system";

        public const string MotionOn = "on";

        public const string MotionOff = "off";

        // Long enough to recognize a value in a warning, short enough that a planted value cannot flood the log.
        private const int MaxQuotedLength = 40;

        private static readonly float[] s_uiScales = { 1f, 1.1f, 1.25f, 1.5f };

        public static IReadOnlyList<float> UiScales => s_uiScales;

        public static (ResolvedUiSettings Value, IReadOnlyList<string> Warnings) Resolve(UiSettings settings, UiSettingsChoices choices)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (choices == null)
            {
                throw new ArgumentNullException(nameof(choices));
            }

            var warnings = new List<string>();

            string language = string.IsNullOrEmpty(settings.Language)
                ? choices.InitialLanguage ?? FallbackLanguage
                : Pick(settings.Language, choices.Languages, FallbackLanguage, "language", warnings);
            string palette = Pick(settings.Palette, choices.Palettes, DefaultPalette, "palette", warnings);
            string fontSet = Pick(settings.FontSet, choices.FontSets, DefaultFontSet, "fontSet", warnings);
            ThemeBackground background = ParseBackground(settings.Background, warnings);
            ReducedMotionMode motion = ParseMotion(settings.ReducedMotion, warnings);
            float uiScale = PickScale(settings.UiScale, warnings);
            bool reduced = ReducesMotion(motion, choices.SystemPrefersReducedMotion);
            string lastScreen = string.IsNullOrEmpty(settings.LastScreen) ? DefaultScreen : settings.LastScreen;

            var resolved = new ResolvedUiSettings(
                language,
                new ThemeSettings(palette, background, reduced),
                motion,
                fontSet,
                uiScale,
                settings.DeveloperMode,
                lastScreen);
            return (resolved, warnings);
        }

        /// <summary>
        /// The language of the startup problem screen: the one shown until now, else the saved one,
        /// else <paramref name="initialLanguage"/> from the OS. Values not in <paramref name="available"/> are skipped.
        /// </summary>
        public static string ChooseProblemLanguage(string shownLanguage, string savedLanguage, string initialLanguage, IEnumerable<string> available)
        {
            var codes = new HashSet<string>(available ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            if (shownLanguage != null && codes.Contains(shownLanguage))
            {
                return shownLanguage;
            }

            if (savedLanguage != null && codes.Contains(savedLanguage))
            {
                return savedLanguage;
            }

            return initialLanguage;
        }

        public static bool ReducesMotion(ReducedMotionMode mode, bool systemPrefersReducedMotion)
        {
            return mode == ReducedMotionMode.On || (mode == ReducedMotionMode.System && systemPrefersReducedMotion);
        }

        public static string ToText(ThemeBackground background)
        {
            return background == ThemeBackground.Light ? Light : Dark;
        }

        public static string ToText(ReducedMotionMode mode)
        {
            return mode switch
            {
                ReducedMotionMode.On => MotionOn,
                ReducedMotionMode.Off => MotionOff,
                _ => MotionSystem,
            };
        }

        /// <summary>The allowed scale step for <paramref name="value"/>, or null if it is not one of them.</summary>
        public static float? FindScale(double value)
        {
            foreach (float step in s_uiScales)
            {
                // Hand-written JSON may say 1.1 while the float step is 1.10000002.
                if (Math.Abs(value - step) < 0.001)
                {
                    return step;
                }
            }

            return null;
        }

        /// <summary>A value as it may appear in a log: quoted, shortened, and without control characters.</summary>
        internal static string Quote(string value)
        {
            if (value == null)
            {
                return "null";
            }

            var builder = new StringBuilder("'");
            int length = Math.Min(value.Length, MaxQuotedLength);
            for (int i = 0; i < length; i++)
            {
                builder.Append(char.IsControl(value[i]) ? '?' : value[i]);
            }

            builder.Append(value.Length > MaxQuotedLength ? "…'" : "'");
            return builder.ToString();
        }

        private static string Pick(string value, IReadOnlyCollection<string> allowed, string fallback, string field, List<string> warnings)
        {
            if (value != null && allowed.Contains(value))
            {
                return value;
            }

            warnings.Add($"Unknown {field} {Quote(value)} in {UiSettings.FileName}; using '{fallback}'.");
            return fallback;
        }

        private static ThemeBackground ParseBackground(string value, List<string> warnings)
        {
            switch (value)
            {
                case Dark:
                    return ThemeBackground.Dark;
                case Light:
                    return ThemeBackground.Light;
                default:
                    warnings.Add($"Unknown background {Quote(value)} in {UiSettings.FileName}; using '{Dark}'.");
                    return ThemeBackground.Dark;
            }
        }

        private static ReducedMotionMode ParseMotion(string value, List<string> warnings)
        {
            switch (value)
            {
                case MotionSystem:
                    return ReducedMotionMode.System;
                case MotionOn:
                    return ReducedMotionMode.On;
                case MotionOff:
                    return ReducedMotionMode.Off;
                default:
                    warnings.Add($"Unknown reducedMotion {Quote(value)} in {UiSettings.FileName}; using '{MotionSystem}'.");
                    return ReducedMotionMode.System;
            }
        }

        private static float PickScale(double value, List<string> warnings)
        {
            float? step = FindScale(value);
            if (step.HasValue)
            {
                return step.Value;
            }

            warnings.Add($"Unsupported uiScale {value.ToString(CultureInfo.InvariantCulture)} in {UiSettings.FileName}; using 1.");
            return 1f;
        }
    }
}
