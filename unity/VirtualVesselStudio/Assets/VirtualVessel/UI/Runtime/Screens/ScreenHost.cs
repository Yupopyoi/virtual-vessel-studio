using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.Screens
{
    /// <summary>
    /// Shows one screen at a time (UI foundation detailed design 7.7). A screen is created the first
    /// time it opens and kept afterwards, hidden with <c>display: none</c>.
    /// </summary>
    public sealed class ScreenHost : VisualElement
    {
        public const string UssClass = "vv-screens";
        public const string ScreenClass = "vv-screen";
        public const string ColumnClass = ScreenClass + "__column";
        public const string TitleClass = ScreenClass + "__title";
        public const string FailedClass = ScreenClass + "__failed";

        private readonly Dictionary<string, ScreenDefinition> _definitions = new Dictionary<string, ScreenDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, Page> _pages = new Dictionary<string, Page>(StringComparer.Ordinal);
        private readonly ILog _log;

        /// <param name="log">Optional; records screens that fail to build.</param>
        public ScreenHost(IEnumerable<ScreenDefinition> screens, ILog log)
        {
            if (screens == null)
            {
                throw new ArgumentNullException(nameof(screens));
            }

            _log = log;
            AddToClassList(UssClass);
            foreach (ScreenDefinition screen in screens)
            {
                _definitions.Add(screen.Id, screen);
            }
        }

        /// <summary>The id of the screen shown, or null before the first <see cref="Show"/>.</summary>
        public string Current { get; private set; }

        public bool IsCreated(string id)
        {
            return _pages.ContainsKey(id);
        }

        /// <summary>The screen element as created, or null if it has not opened yet or failed to build.</summary>
        public VisualElement GetScreen(string id)
        {
            return _pages.TryGetValue(id, out Page page) ? page.Screen : null;
        }

        /// <summary>The page around the screen (heading and scroll view), or null before it opens.</summary>
        public VisualElement GetPage(string id)
        {
            return _pages.TryGetValue(id, out Page page) ? page.Root : null;
        }

        public void Show(string id)
        {
            if (!_definitions.TryGetValue(id ?? string.Empty, out ScreenDefinition definition))
            {
                throw new ArgumentException($"Unknown screen '{id}'.", nameof(id));
            }

            if (string.Equals(Current, id, StringComparison.Ordinal))
            {
                return;
            }

            HideCurrent();
            if (!_pages.TryGetValue(id, out Page page))
            {
                page = CreatePage(definition);
                _pages.Add(id, page);
                Add(page.Root);
            }

            page.Root.style.display = DisplayStyle.Flex;
            Current = id;
            (page.Screen as IScreen)?.OnShown();
        }

        /// <summary>Hides the shown screen and tells it so; nothing is shown afterwards. For teardown.</summary>
        public void HideCurrent()
        {
            if (Current == null)
            {
                return;
            }

            Page page = _pages[Current];
            page.Root.style.display = DisplayStyle.None;
            Current = null;
            (page.Screen as IScreen)?.OnHidden();
        }

        private Page CreatePage(ScreenDefinition definition)
        {
            // Screens scroll up and down only; anything wider than its column is a layout bug, which
            // the layout tests catch, not something to scroll sideways.
            var root = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            root.AddToClassList(ScreenClass);
            root.AddToClassList(ScreenClass + "--" + definition.Id);

            // A column with a maximum width, so rows stay readable on a wide window (design-system.md 5).
            var column = new VisualElement();
            column.AddToClassList(ColumnClass);
            root.Add(column);
            column.Add(Ui.Text(definition.TitleKey, TitleClass, "vv-text-heading"));

            VisualElement screen;
            try
            {
                screen = definition.Create();
                column.Add(screen);
            }
            catch (Exception exception)
            {
                // One broken screen must not take the others down (UI foundation detailed design 13).
                screen = null;
                _log?.Error($"Screen '{definition.Id}' failed to build.", exception);
                column.Add(Ui.Text(ScreenStrings.Failed, FailedClass, "vv-text-body"));
            }

            return new Page(root, screen);
        }

        private readonly struct Page
        {
            public Page(VisualElement root, VisualElement screen)
            {
                Root = root;
                Screen = screen;
            }

            public VisualElement Root { get; }

            public VisualElement Screen { get; }
        }
    }
}
