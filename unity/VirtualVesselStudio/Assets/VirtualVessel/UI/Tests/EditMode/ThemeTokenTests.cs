using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Editor.Theme;
using VirtualVessel.UI.Theme;

namespace VirtualVessel.UI.Tests
{
    /// <summary>
    /// Keeps design-tokens.json, the generated stylesheets, the styleboard, and the accessibility
    /// rules of the design system in agreement (UI foundation detailed design 7.1).
    /// </summary>
    public sealed class ThemeTokenTests
    {
        private static DesignTokens Tokens => ThemeGenerator.LoadTokens();

        private static IEnumerable<TestCaseData> Themes()
        {
            foreach (DesignTokens.PaletteToken palette in ThemeGenerator.LoadTokens().palettes)
            {
                yield return new TestCaseData(palette.id, false).SetName($"Contrast {palette.id} dark");
                yield return new TestCaseData(palette.id, true).SetName($"Contrast {palette.id} light");
            }
        }

        [Test]
        public void GeneratedStyleSheets_MatchTokens()
        {
            IReadOnlyList<KeyValuePair<string, string>> expected = ThemeStyleSheetGenerator.Generate(
                Tokens, ThemeGenerator.FindFontSets().Select(ThemeGenerator.ToPaths));

            foreach (KeyValuePair<string, string> file in expected)
            {
                string path = Path.Combine(ThemeGenerator.GeneratedFolder, file.Key);
                Assert.That(File.Exists(path), Is.True, $"{file.Key} is missing. Run Virtual Vessel > UI > Generate Theme.");
                Assert.That(File.ReadAllText(path), Is.EqualTo(file.Value), $"{file.Key} is out of date. Run Virtual Vessel > UI > Generate Theme.");
            }

            string[] onDisk = Directory.GetFiles(ThemeGenerator.GeneratedFolder, "*.uss").Select(Path.GetFileName).ToArray();
            Assert.That(onDisk, Is.EquivalentTo(expected.Select(f => f.Key)), "Generated folder has stale files.");
        }

        [Test]
        public void ThemeAssets_ReferenceEveryPaletteAndFontSet()
        {
            ThemeAssets assets = AssetDatabase.LoadAssetAtPath<ThemeAssets>(ThemeGenerator.ThemeAssetsPath);

            Assert.That(assets, Is.Not.Null);
            Assert.That(assets.Common, Is.Not.Null);
            foreach (DesignTokens.PaletteToken palette in Tokens.palettes)
            {
                Assert.That(assets.GetThemeSheet(palette.id, ThemeBackground.Dark), Is.Not.Null, palette.id);
                Assert.That(assets.GetThemeSheet(palette.id, ThemeBackground.Light), Is.Not.Null, palette.id);
            }

            Assert.That(assets.GetFontSheet(FontService.DefaultFontSet), Is.Not.Null);
            FontSet noto = assets.FontSets.Single(f => f.Id == FontService.DefaultFontSet);
            Assert.That(noto.Regular != null && noto.Bold != null && noto.Mono != null, Is.True, "Every font of the default set is assigned.");
        }

        [TestCaseSource(nameof(Themes))]
        public void Theme_MeetsContrastRules(string paletteId, bool light)
        {
            // design-system.md 9.2: body text 4.5:1, large text, icons, and boundaries 3:1.
            DesignTokens tokens = Tokens;
            Dictionary<string, ThemeColor> c = ThemeStyleSheetGenerator.Colors(tokens, tokens.palettes.Single(p => p.id == paletteId), light)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            var failures = new List<string>();
            void Check(string foreground, string background, double minimum)
            {
                double ratio = ThemeColor.Contrast(c[foreground], c[background]);
                if (ratio < minimum)
                {
                    failures.Add($"{foreground} on {background}: {ratio:0.00} < {minimum}");
                }
            }

            foreach (string surface in new[] { "bg", "surface", "surface-2", "surface-3" })
            {
                Check("text", surface, 4.5);
                Check("text-2", surface, 4.5);
            }

            Check("text-3", "bg", 4.5);
            Check("text-3", "surface", 4.5);
            Check("text-3", "surface-2", 4.5);
            Check("on-accent", "primary", 4.5);
            Check("on-accent", "secondary", 4.5);
            Check("accent-line", "bg", 3);
            Check("accent-line", "surface", 3);
            Check("accent-line", "surface-2", 3);
            foreach (string status in new[] { "success", "warning", "error", "live" })
            {
                // Status text sits on its soft tint over a card.
                ThemeColor chip = c[status + "-soft"].Over(c["surface-2"]);
                double ratio = ThemeColor.Contrast(c[status], chip);
                if (ratio < 4.5)
                {
                    failures.Add($"{status} text on its chip: {ratio:0.00} < 4.5");
                }

                Check(status, "surface", 3);
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [Test]
        public void Styleboard_UsesTokenPaletteColors()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "docs", "assets", "ui", "styleboard.html"));
            string html = File.ReadAllText(path);
            foreach (DesignTokens.PaletteToken palette in Tokens.palettes)
            {
                string selector = palette.id == Tokens.defaultPalette ? ":root" : $":root\\[data-palette=\"{palette.id}\"\\]";
                Match match = Regex.Match(html, selector + @"\s*\{[^}]*?--h:\s*(\d+);\s*--primary:\s*(#[0-9a-f]{6});\s*--secondary:\s*(#[0-9a-f]{6});");
                Assert.That(match.Success, Is.True, $"Palette {palette.id} not found in styleboard.html.");
                Assert.That(float.Parse(match.Groups[1].Value), Is.EqualTo(palette.hue), palette.id);
                Assert.That(match.Groups[2].Value, Is.EqualTo(palette.primary), palette.id);
                Assert.That(match.Groups[3].Value, Is.EqualTo(palette.secondary), palette.id);
            }
        }

        [Test]
        public void ThemeColor_MatchesKnownValues()
        {
            Assert.That(ThemeColor.FromHsl(0, 100, 50).ToUss(), Is.EqualTo("#ff0000"));
            Assert.That(ThemeColor.FromHsl(268, 26, 8).ToUss(), Is.EqualTo("#140f1a"), "Lavender dark background, as the browser computes it.");
            Assert.That(ThemeColor.Contrast(ThemeColor.FromHex("#000000"), ThemeColor.FromHex("#ffffff")), Is.EqualTo(21).Within(0.01));
            Assert.That(ThemeColor.FromHex("#c3a6ff").WithAlpha(0.35f).ToUss(), Is.EqualTo("rgba(195, 166, 255, 0.35)"));
            Assert.That(ThemeColor.FromHex("#ffffff").Mix(ThemeColor.FromHex("#000000"), 0.5).ToUss(), Is.EqualTo("#808080"));
        }
    }
}
