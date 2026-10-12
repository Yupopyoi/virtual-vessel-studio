using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Theme;

namespace VirtualVessel.UI.Editor.Theme
{
    /// <summary>
    /// Regenerates the theme, common, and font stylesheets from design-tokens.json and refreshes
    /// ThemeAssets.asset. Run after changing tokens or font sets; the results are committed.
    /// </summary>
    /// <remarks>Batch mode: <c>-executeMethod VirtualVessel.UI.Editor.Theme.ThemeGenerator.Generate</c>.</remarks>
    public static class ThemeGenerator
    {
        public const string ThemeFolder = "Assets/VirtualVessel/UI/Theme";
        public const string GeneratedFolder = ThemeFolder + "/Generated";
        public const string TokensPath = ThemeFolder + "/design-tokens.json";
        public const string ThemeAssetsPath = ThemeFolder + "/ThemeAssets.asset";
        public const string FontSetFolder = "Assets/VirtualVessel/UI/Fonts";
        public const string DefaultFontSetPath = FontSetFolder + "/NotoSans.asset";

        private static readonly UTF8Encoding s_encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        [MenuItem("Virtual Vessel/UI/Generate Theme")]
        public static void Generate()
        {
            EnsureDefaultFontSet();
            DesignTokens tokens = LoadTokens();
            List<FontSet> fontSets = FindFontSets();
            IReadOnlyList<KeyValuePair<string, string>> files = ThemeStyleSheetGenerator.Generate(tokens, fontSets.Select(ToPaths));

            Directory.CreateDirectory(GeneratedFolder);
            int written = 0;
            foreach (KeyValuePair<string, string> file in files)
            {
                string path = Path.Combine(GeneratedFolder, file.Key);
                if (!File.Exists(path) || File.ReadAllText(path, s_encoding) != file.Value)
                {
                    File.WriteAllText(path, file.Value, s_encoding);
                    written++;
                }
            }

            AssetDatabase.Refresh();
            UpdateThemeAssets(tokens, fontSets);
            Debug.Log($"[ThemeGenerator] {files.Count} stylesheets checked, {written} written.");
        }

        internal static DesignTokens LoadTokens()
        {
            return DesignTokens.Parse(File.ReadAllText(TokensPath, s_encoding));
        }

        internal static List<FontSet> FindFontSets()
        {
            return AssetDatabase.FindAssets("t:" + nameof(FontSet), new[] { FontSetFolder })
                .Select(guid => AssetDatabase.LoadAssetAtPath<FontSet>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(set => set != null)
                .OrderBy(set => set.Id, System.StringComparer.Ordinal)
                .ToList();
        }

        internal static FontSetPaths ToPaths(FontSet set)
        {
            return new FontSetPaths(set.Id, AssetDatabase.GetAssetPath(set.Regular), AssetDatabase.GetAssetPath(set.Bold), AssetDatabase.GetAssetPath(set.Mono));
        }

        private static void EnsureDefaultFontSet()
        {
            if (AssetDatabase.LoadAssetAtPath<FontSet>(DefaultFontSetPath) != null)
            {
                return;
            }

            Directory.CreateDirectory(FontSetFolder);
            FontSet set = ScriptableObject.CreateInstance<FontSet>();
            var serialized = new SerializedObject(set);
            serialized.FindProperty("_id").stringValue = FontService.DefaultFontSet;
            serialized.FindProperty("_displayName").stringValue = "Noto Sans";
            serialized.FindProperty("_regular").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>("Assets/ThirdParty/NotoSansJP/NotoSansJP-Regular.ttf");
            serialized.FindProperty("_bold").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>("Assets/ThirdParty/NotoSansJP/NotoSansJP-Bold.ttf");
            serialized.FindProperty("_mono").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>("Assets/ThirdParty/NotoSansMono/NotoSansMono-Regular.ttf");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(set, DefaultFontSetPath);
            AssetDatabase.SaveAssets();
        }

        private static void UpdateThemeAssets(DesignTokens tokens, List<FontSet> fontSets)
        {
            ThemeAssets assets = AssetDatabase.LoadAssetAtPath<ThemeAssets>(ThemeAssetsPath);
            if (assets == null)
            {
                assets = ScriptableObject.CreateInstance<ThemeAssets>();
                AssetDatabase.CreateAsset(assets, ThemeAssetsPath);
            }

            StyleSheet Load(string fileName)
            {
                return AssetDatabase.LoadAssetAtPath<StyleSheet>(GeneratedFolder + "/" + fileName);
            }

            assets.Set(
                Load(ThemeStyleSheetGenerator.CommonFileName),
                tokens.palettes.Select(p => new ThemeAssets.PaletteSheets(
                    p.id,
                    p.name,
                    Load(ThemeStyleSheetGenerator.ThemeFileName(p.id, light: false)),
                    Load(ThemeStyleSheetGenerator.ThemeFileName(p.id, light: true)))),
                fontSets.Select(f => new ThemeAssets.FontSheet(f, Load(ThemeStyleSheetGenerator.FontFileName(f.Id)))));
            EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssets();
        }
    }
}
