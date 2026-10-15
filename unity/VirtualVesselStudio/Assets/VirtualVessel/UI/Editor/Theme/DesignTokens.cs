using System;
using UnityEngine;

namespace VirtualVessel.UI.Editor.Theme
{
    /// <summary>
    /// The contents of design-tokens.json, the single source of the design system values on the
    /// Unity side (docs/en/ui/design-system.md). Field names follow the JSON keys.
    /// </summary>
    [Serializable]
    internal sealed class DesignTokens
    {
        // JsonUtility requires public lower-case fields that match the JSON keys.
        public int schemaVersion;
        public string defaultPalette;
        public PaletteToken[] palettes;
        public NeutralToken[] neutrals;
        public AccentShiftToken accentShift;
        public int[] accentLineLight;
        public StatusToken[] status;
        public float statusLineAlpha;
        public float glowAlpha;
        public ModeAlphaToken washAlpha;
        public float tintAmount;
        public float errorSurfaceAmount;
        public ShadowToken shadow;

        /// <summary>The shadow color's opacity behind a dialog; dark pages need more to show the change.</summary>
        public ModeAlphaToken scrimAlpha;

        /// <summary>On light backgrounds, how much of the vivid fill color tints a chip surface.</summary>
        public float chipFillAlphaLight;
        public SizeToken[] spacing;
        public SizeToken[] radius;
        public TypographyToken[] typography;

        public static DesignTokens Parse(string json)
        {
            DesignTokens tokens = JsonUtility.FromJson<DesignTokens>(json);
            if (tokens == null || tokens.palettes == null || tokens.palettes.Length == 0)
            {
                throw new FormatException("design-tokens.json has no palettes.");
            }

            return tokens;
        }

        [Serializable]
        internal sealed class PaletteToken
        {
            public string id;
            public string name;
            public float hue;
            public string primary;
            public string secondary;
        }

        /// <summary>Saturation and lightness (percent) for dark and light backgrounds; hue comes from the palette.</summary>
        [Serializable]
        internal sealed class NeutralToken
        {
            public string token;
            public int[] dark;
            public int[] light;
        }

        /// <summary>Saturation and lightness deltas (percent) applied to Primary and Secondary per background.</summary>
        [Serializable]
        internal sealed class AccentShiftToken
        {
            public int[] dark;
            public int[] light;
        }

        [Serializable]
        internal sealed class ModeAlphaToken
        {
            public float dark;
            public float light;
        }

        [Serializable]
        internal sealed class StatusToken
        {
            public string token;
            public string dark;
            public string light;
            public float softAlpha;

            /// <summary>Optional graphic color (level meter) on dark backgrounds; the status color when empty.</summary>
            public string fillDark;

            /// <summary>Optional graphic color (level meter) on light backgrounds; the status color when empty.</summary>
            public string fillLight;
        }

        [Serializable]
        internal sealed class ShadowToken
        {
            public float saturation;
            public float lightness;
            public float alpha;
        }

        [Serializable]
        internal sealed class SizeToken
        {
            public string token;
            public int px;
        }

        [Serializable]
        internal sealed class TypographyToken
        {
            public string token;
            public int size;

            /// <summary>"regular", "bold", or "mono".</summary>
            public string font;
        }
    }
}
