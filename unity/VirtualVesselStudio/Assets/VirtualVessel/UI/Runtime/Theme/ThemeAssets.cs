using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace VirtualVessel.UI.Theme
{
    /// <summary>
    /// The stylesheets generated from design-tokens.json, kept together so that the runtime can
    /// switch them. Filled by the ThemeGenerator editor tool; do not edit by hand.
    /// </summary>
    public sealed class ThemeAssets : ScriptableObject
    {
        [SerializeField]
        private StyleSheet _common;

        [SerializeField]
        private StyleSheet _components;

        [SerializeField]
        private List<PaletteSheets> _palettes = new List<PaletteSheets>();

        [SerializeField]
        private List<FontSheet> _fontSets = new List<FontSheet>();

        /// <summary>Spacing, radius, and text size variables shared by every theme.</summary>
        public StyleSheet Common => _common;

        /// <summary>The styles of the common components (Styles/components.uss), which use only token variables.</summary>
        public StyleSheet Components => _components;

        public IEnumerable<string> PaletteIds
        {
            get
            {
                foreach (PaletteSheets palette in _palettes)
                {
                    yield return palette.Id;
                }
            }
        }

        public IEnumerable<FontSet> FontSets
        {
            get
            {
                foreach (FontSheet entry in _fontSets)
                {
                    yield return entry.FontSet;
                }
            }
        }

        public bool HasPalette(string id)
        {
            return FindPalette(id) != null;
        }

        public string GetPaletteName(string id)
        {
            return FindPalette(id)?.DisplayName;
        }

        public StyleSheet GetThemeSheet(string paletteId, ThemeBackground background)
        {
            PaletteSheets palette = FindPalette(paletteId);
            return palette == null ? null : background == ThemeBackground.Light ? palette.Light : palette.Dark;
        }

        public StyleSheet GetFontSheet(string fontSetId)
        {
            foreach (FontSheet entry in _fontSets)
            {
                if (entry.FontSet != null && string.Equals(entry.FontSet.Id, fontSetId, StringComparison.Ordinal))
                {
                    return entry.Sheet;
                }
            }

            return null;
        }

        /// <summary>Used by the generator to fill the asset.</summary>
        internal void Set(StyleSheet common, StyleSheet components, IEnumerable<PaletteSheets> palettes, IEnumerable<FontSheet> fontSets)
        {
            _common = common;
            _components = components;
            _palettes = new List<PaletteSheets>(palettes);
            _fontSets = new List<FontSheet>(fontSets);
        }

        private PaletteSheets FindPalette(string id)
        {
            foreach (PaletteSheets palette in _palettes)
            {
                if (string.Equals(palette.Id, id, StringComparison.Ordinal))
                {
                    return palette;
                }
            }

            return null;
        }

        [Serializable]
        internal sealed class PaletteSheets
        {
            [SerializeField]
            private string _id;

            [SerializeField]
            private string _displayName;

            [SerializeField]
            private StyleSheet _dark;

            [SerializeField]
            private StyleSheet _light;

            // For the Unity serializer.
            private PaletteSheets()
            {
            }

            public PaletteSheets(string id, string displayName, StyleSheet dark, StyleSheet light)
            {
                _id = id;
                _displayName = displayName;
                _dark = dark;
                _light = light;
            }

            public string Id => _id;

            public string DisplayName => _displayName;

            public StyleSheet Dark => _dark;

            public StyleSheet Light => _light;
        }

        [Serializable]
        internal sealed class FontSheet
        {
            [SerializeField]
            private FontSet _fontSet;

            [SerializeField]
            private StyleSheet _sheet;

            // For the Unity serializer.
            private FontSheet()
            {
            }

            public FontSheet(FontSet fontSet, StyleSheet sheet)
            {
                _fontSet = fontSet;
                _sheet = sheet;
            }

            public FontSet FontSet => _fontSet;

            public StyleSheet Sheet => _sheet;
        }
    }
}
