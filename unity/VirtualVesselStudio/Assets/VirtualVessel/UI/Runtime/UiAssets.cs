using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Theme;

namespace VirtualVessel.UI
{
    /// <summary>
    /// The assets the UI is built from (UI foundation detailed design 5, 7.9). The string tables are
    /// collected from Strings/*.json by the UiAssetsUpdater editor tool, so adding a language file is enough.
    /// </summary>
    public sealed class UiAssets : ScriptableObject
    {
        [SerializeField]
        private PanelSettings _panelSettings;

        [SerializeField]
        private ThemeAssets _theme;

        [SerializeField]
        private List<TextAsset> _stringTables = new List<TextAsset>();

        /// <summary>The template; the UI root uses a copy so the asset never changes at run time.</summary>
        public PanelSettings PanelSettings => _panelSettings;

        public ThemeAssets Theme => _theme;

        public IReadOnlyList<TextAsset> StringTables => _stringTables;

        /// <summary>Parses every string table. Throws if one is malformed, which is a build mistake.</summary>
        public IReadOnlyList<StringTable> LoadStringTables()
        {
            var tables = new List<StringTable>(_stringTables.Count);
            foreach (TextAsset asset in _stringTables)
            {
                if (asset == null)
                {
                    throw new InvalidOperationException($"{name} has an empty string table entry.");
                }

                tables.Add(StringTable.Parse(asset.text));
            }

            return tables;
        }

        /// <summary>The font set with <paramref name="id"/>, or null.</summary>
        public FontSet FindFontSet(string id)
        {
            if (_theme == null)
            {
                return null;
            }

            foreach (FontSet fontSet in _theme.FontSets)
            {
                if (fontSet != null && string.Equals(fontSet.Id, id, StringComparison.Ordinal))
                {
                    return fontSet;
                }
            }

            return null;
        }

        /// <summary>Used by the editor tool that keeps the asset in sync.</summary>
        internal void Set(PanelSettings panelSettings, ThemeAssets theme, IEnumerable<TextAsset> stringTables)
        {
            _panelSettings = panelSettings;
            _theme = theme;
            _stringTables = new List<TextAsset>(stringTables);
        }
    }
}
