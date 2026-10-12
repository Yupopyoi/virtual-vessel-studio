using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Theme;

namespace VirtualVessel.UI.Tests
{
    public sealed class ThemeServiceTests
    {
        private ThemeAssets _assets;
        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _assets = Create<ThemeAssets>();
            var fontSet = Create<FontSet>();
            _assets.Set(
                Create<StyleSheet>(),
                new[]
                {
                    new ThemeAssets.PaletteSheets("lavender", "Lavender", Create<StyleSheet>(), Create<StyleSheet>()),
                    new ThemeAssets.PaletteSheets("mint", "Mint", Create<StyleSheet>(), Create<StyleSheet>()),
                },
                new[] { new ThemeAssets.FontSheet(fontSet, Create<StyleSheet>()) });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
            {
                Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        [Test]
        public void Attach_AddsRootClassCommonAndDefaultTheme()
        {
            var root = new VisualElement();

            new ThemeService(_assets, null).Attach(root);

            Assert.That(root.ClassListContains(ThemeService.RootClass), Is.True);
            Assert.That(root.styleSheets.Contains(_assets.Common), Is.True);
            Assert.That(root.styleSheets.Contains(_assets.GetThemeSheet("lavender", ThemeBackground.Dark)), Is.True);
        }

        [Test]
        public void Apply_SwapsOnlyTheThemeSheet()
        {
            var root = new VisualElement();
            var service = new ThemeService(_assets, null);
            service.Attach(root);
            ThemeSettings? changed = null;
            service.Changed += s => changed = s;

            service.Apply(new ThemeSettings("mint", ThemeBackground.Light, reducedMotion: true));

            Assert.That(root.styleSheets.Contains(_assets.GetThemeSheet("lavender", ThemeBackground.Dark)), Is.False);
            Assert.That(root.styleSheets.Contains(_assets.GetThemeSheet("mint", ThemeBackground.Light)), Is.True);
            Assert.That(root.styleSheets.Contains(_assets.Common), Is.True);
            Assert.That(root.styleSheets.count, Is.EqualTo(2));
            Assert.That(root.ClassListContains(ThemeService.ReducedMotionClass), Is.True);
            Assert.That(changed?.Palette, Is.EqualTo("mint"));
        }

        [Test]
        public void Apply_UnknownPalette_FallsBackToDefault()
        {
            var root = new VisualElement();
            var service = new ThemeService(_assets, null);
            service.Attach(root);
            service.Apply(new ThemeSettings("mint", ThemeBackground.Dark, false));

            service.Apply(new ThemeSettings("removed-palette", ThemeBackground.Dark, false));

            Assert.That(service.Current.Palette, Is.EqualTo(ThemeSettings.DefaultPalette));
            Assert.That(root.styleSheets.Contains(_assets.GetThemeSheet("lavender", ThemeBackground.Dark)), Is.True);
        }

        [Test]
        public void ApplyBeforeAttach_IsUsedWhenAttached()
        {
            var root = new VisualElement();
            var service = new ThemeService(_assets, null);

            service.Apply(new ThemeSettings("mint", ThemeBackground.Dark, false));
            service.Attach(root);

            Assert.That(root.styleSheets.Contains(_assets.GetThemeSheet("mint", ThemeBackground.Dark)), Is.True);
        }

        [Test]
        public void FontService_SwapsTheFontSheetAndFallsBack()
        {
            var root = new VisualElement();
            var service = new FontService(_assets, null);
            FontSet set = _assets.FontSets.Single();
            service.Attach(root);
            StyleSheet sheet = _assets.GetFontSheet(set.Id);

            service.Apply("UnknownFonts");

            Assert.That(root.styleSheets.Contains(sheet), Is.True);
            Assert.That(service.Current, Is.EqualTo(FontService.DefaultFontSet));
        }

        private T Create<T>()
            where T : ScriptableObject
        {
            T instance = ScriptableObject.CreateInstance<T>();
            _created.Add(instance);
            return instance;
        }
    }
}
