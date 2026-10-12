using System;
using UnityEngine.UIElements;
using VirtualVessel.Diagnostics.Logging;

namespace VirtualVessel.UI.Theme
{
    /// <summary>
    /// Switches every font in the UI at once by swapping the font stylesheet generated for a
    /// <see cref="FontSet"/> (UI foundation detailed design 7.2).
    /// </summary>
    /// <remarks>
    /// Text picks its font through typography classes (<c>vv-text-body</c>, <c>vv-text-heading</c>,
    /// <c>vv-text-mono</c>, ...), and only the font stylesheet maps those classes to font files.
    /// Call from the main thread.
    /// </remarks>
    public sealed class FontService
    {
        public const string DefaultFontSet = "NotoSans";

        private readonly ThemeAssets _assets;
        private readonly ILog _log;
        private VisualElement _root;
        private StyleSheet _applied;

        public FontService(ThemeAssets assets, ILog log)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _log = log;
            Current = DefaultFontSet;
        }

        public event Action<string> Changed;

        /// <summary>Id of the font set in use.</summary>
        public string Current { get; private set; }

        public void Attach(VisualElement root)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _applied = null;
            ApplyToRoot(Current);
        }

        public void Apply(string fontSetId)
        {
            if (_assets.GetFontSheet(fontSetId) == null)
            {
                _log?.Warning($"Unknown font set '{fontSetId}'; using '{DefaultFontSet}'.");
                fontSetId = DefaultFontSet;
            }

            if (string.Equals(fontSetId, Current, StringComparison.Ordinal) && (_root == null || _applied != null))
            {
                return;
            }

            Current = fontSetId;
            if (_root != null)
            {
                ApplyToRoot(fontSetId);
            }

            _log?.Information($"Font set changed: {fontSetId}.");
            Changed?.Invoke(fontSetId);
        }

        private void ApplyToRoot(string fontSetId)
        {
            StyleSheet sheet = _assets.GetFontSheet(fontSetId) ?? _assets.GetFontSheet(DefaultFontSet);
            if (_applied == sheet)
            {
                return;
            }

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
    }
}
