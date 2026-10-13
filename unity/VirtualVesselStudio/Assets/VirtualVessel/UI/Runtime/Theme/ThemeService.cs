using System;
using UnityEngine.UIElements;
using VirtualVessel.Diagnostics.Logging;

namespace VirtualVessel.UI.Theme
{
    /// <summary>
    /// Applies the palette, background, and reduced-motion setting to a UI root by swapping the
    /// generated theme stylesheet (UI foundation detailed design 7.1).
    /// </summary>
    /// <remarks>
    /// Every part refers to colors through variables such as <c>var(--vv-color-primary)</c>, so
    /// swapping one stylesheet on the root changes the whole UI. Call from the main thread.
    /// </remarks>
    public sealed class ThemeService
    {
        public const string RootClass = "vv-root";

        public const string ReducedMotionClass = "vv-reduced-motion";

        private readonly ThemeAssets _assets;
        private readonly ILog _log;
        private VisualElement _root;
        private StyleSheet _applied;

        /// <param name="log">Optional; records theme changes and unknown palettes.</param>
        public ThemeService(ThemeAssets assets, ILog log)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _log = log;
            Current = ThemeSettings.Default;
        }

        public event Action<ThemeSettings> Changed;

        public ThemeSettings Current { get; private set; }

        /// <summary>Starts styling <paramref name="root"/> with the common and component sheets and the current theme.</summary>
        public void Attach(VisualElement root)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _root.AddToClassList(RootClass);
            foreach (StyleSheet sheet in new[] { _assets.Common, _assets.Components })
            {
                if (sheet != null && !_root.styleSheets.Contains(sheet))
                {
                    _root.styleSheets.Add(sheet);
                }
            }

            _applied = null;
            ApplyToRoot(Current);
        }

        public void Apply(ThemeSettings settings)
        {
            if (!_assets.HasPalette(settings.Palette))
            {
                // A palette removed in a later version, or a hand-edited settings file.
                _log?.Warning($"Unknown palette '{settings.Palette}'; using '{ThemeSettings.DefaultPalette}'.");
                settings = settings.WithPalette(ThemeSettings.DefaultPalette);
            }

            if (settings.Equals(Current) && (_root == null || _applied != null))
            {
                return;
            }

            Current = settings;
            if (_root != null)
            {
                ApplyToRoot(settings);
            }

            _log?.Information($"Theme changed: palette {settings.Palette}, background {settings.Background}, reduced motion {settings.ReducedMotion}.");
            Changed?.Invoke(settings);
        }

        private void ApplyToRoot(ThemeSettings settings)
        {
            StyleSheet sheet = _assets.GetThemeSheet(settings.Palette, settings.Background)
                ?? _assets.GetThemeSheet(ThemeSettings.DefaultPalette, settings.Background);
            if (_applied != sheet)
            {
                if (_applied != null)
                {
                    _root.styleSheets.Remove(_applied);
                }

                if (sheet != null)
                {
                    _root.styleSheets.Add(sheet);
                }

                _applied = sheet;
            }

            _root.EnableInClassList(ReducedMotionClass, settings.ReducedMotion);
        }
    }
}
