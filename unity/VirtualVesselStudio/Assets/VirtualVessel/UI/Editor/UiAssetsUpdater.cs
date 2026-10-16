using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Editor.Theme;
using VirtualVessel.UI.Theme;

namespace VirtualVessel.UI.Editor
{
    /// <summary>
    /// Creates UiAssets.asset and its panel settings, and keeps its string tables equal to
    /// Strings/*.json, so adding a language file is enough (UI foundation detailed design 7.4, 7.9).
    /// </summary>
    /// <remarks>Batch mode: <c>-executeMethod VirtualVessel.UI.Editor.UiAssetsUpdater.UpdateAssets</c>.</remarks>
    public static class UiAssetsUpdater
    {
        public const string UiFolder = "Assets/VirtualVessel/UI";
        public const string UiAssetsPath = UiFolder + "/UiAssets.asset";
        public const string PanelSettingsPath = UiFolder + "/PanelSettings.asset";
        public const string RuntimeThemePath = UiFolder + "/RuntimeTheme.tss";
        public const string StringsFolder = UiFolder + "/Strings";

        [MenuItem("Virtual Vessel/UI/Update UI Assets")]
        public static void UpdateAssets()
        {
            ThemeStyleSheet runtimeTheme = EnsureRuntimeTheme();
            PanelSettings panel = EnsurePanelSettings(runtimeTheme);
            ThemeAssets theme = AssetDatabase.LoadAssetAtPath<ThemeAssets>(ThemeGenerator.ThemeAssetsPath);
            if (theme == null)
            {
                throw new InvalidOperationException($"{ThemeGenerator.ThemeAssetsPath} is missing; run the theme generator first.");
            }

            UiAssets assets = AssetDatabase.LoadAssetAtPath<UiAssets>(UiAssetsPath);
            if (assets == null)
            {
                assets = ScriptableObject.CreateInstance<UiAssets>();
                AssetDatabase.CreateAsset(assets, UiAssetsPath);
            }

            TextAsset[] tables = FindStringTables();
            bool unchanged = assets.PanelSettings == panel && assets.Theme == theme && assets.StringTables.SequenceEqual(tables);
            if (unchanged)
            {
                return;
            }

            assets.Set(panel, theme, tables);
            EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssets();
            Debug.Log($"[UiAssetsUpdater] {UiAssetsPath} updated: {tables.Length} string tables.");
        }

        /// <summary>Every Strings/*.json, ordered by path so the asset does not change between runs.</summary>
        internal static TextAsset[] FindStringTables()
        {
            return AssetDatabase.FindAssets("t:" + nameof(TextAsset), new[] { StringsFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<TextAsset>)
                .ToArray();
        }

        private static ThemeStyleSheet EnsureRuntimeTheme()
        {
            if (!File.Exists(RuntimeThemePath))
            {
                // Unity's runtime defaults only; every project look comes from the token stylesheets.
                File.WriteAllText(RuntimeThemePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(RuntimeThemePath);
            }

            return AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(RuntimeThemePath);
        }

        private static PanelSettings EnsurePanelSettings(ThemeStyleSheet runtimeTheme)
        {
            PanelSettings panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panel != null)
            {
                return panel;
            }

            // The UI root sets the scale mode and scale on its run-time copy (UI foundation detailed design 7.9).
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.themeStyleSheet = runtimeTheme;
            panel.scaleMode = PanelScaleMode.ConstantPhysicalSize;
            panel.referenceDpi = 96f;
            panel.fallbackDpi = 96f;
            AssetDatabase.CreateAsset(panel, PanelSettingsPath);
            return panel;
        }

        /// <summary>Refreshes the string tables when a language file is added, removed, or moved.</summary>
        private sealed class StringTablePostprocessor : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                bool touchesStrings = imported.Concat(deleted).Concat(moved).Concat(movedFrom)
                    .Any(path => path.StartsWith(StringsFolder + "/", StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
                if (touchesStrings && File.Exists(UiAssetsPath))
                {
                    // Changing assets during an import is not allowed; update right after it.
                    EditorApplication.delayCall += UpdateAssets;
                }
            }
        }
    }
}
