using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VirtualVessel.UI.Editor;
using VirtualVessel.UI.Editor.Theme;
using VirtualVessel.UI.Theme;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The asset the UI root builds from (UI foundation detailed design 7.4, 7.9).</summary>
    public sealed class UiAssetsTests
    {
        private UiAssets _assets;

        [SetUp]
        public void SetUp()
        {
            _assets = AssetDatabase.LoadAssetAtPath<UiAssets>(UiAssetsUpdater.UiAssetsPath);
            Assert.That(_assets, Is.Not.Null, $"{UiAssetsUpdater.UiAssetsPath} is missing; run Virtual Vessel/UI/Update UI Assets.");
        }

        [Test]
        public void StringTables_AreEveryStringsJson()
        {
            TextAsset[] files = UiAssetsUpdater.FindStringTables();

            Assert.That(files.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(_assets.StringTables, Is.EqualTo(files), "Run Virtual Vessel/UI/Update UI Assets after adding a language file.");
            Assert.That(_assets.LoadStringTables().Select(table => table.Locale.Code), Is.EquivalentTo(new[] { "en", "ja" }));
        }

        [Test]
        public void References_ThePanelSettingsThemeAndDefaultFontSet()
        {
            Assert.That(AssetDatabase.GetAssetPath(_assets.PanelSettings), Is.EqualTo(UiAssetsUpdater.PanelSettingsPath));
            Assert.That(_assets.PanelSettings.themeStyleSheet, Is.Not.Null, "Without a theme, Unity warns and draws nothing.");
            Assert.That(AssetDatabase.GetAssetPath(_assets.Theme), Is.EqualTo(ThemeGenerator.ThemeAssetsPath));
            Assert.That(_assets.FindFontSet(FontService.DefaultFontSet)?.Regular, Is.Not.Null, "The default font set is the glyph fallback.");
        }
    }
}
