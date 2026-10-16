using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.Screens
{
    /// <summary>
    /// The application frame: the navigation on the left and the screens on the right (UI foundation
    /// detailed design 7.7, the screen mockup in styleboard.html).
    /// </summary>
    public sealed class AppShell : VisualElement
    {
        public const string UssClass = "vv-shell";
        public const string NavClass = UssClass + "__nav";
        public const string VersionClass = UssClass + "__version";

        private readonly IReadOnlyList<ScreenDefinition> _screens;
        private readonly List<NavItem> _items = new List<NavItem>();
        private bool _developerMode;

        /// <param name="version">Shown at the bottom of the navigation, such as "0.1.0"; optional.</param>
        /// <param name="log">Optional; records screens that fail to build.</param>
        public AppShell(IReadOnlyList<ScreenDefinition> screens, string version, ILog log)
        {
            _screens = screens ?? throw new ArgumentNullException(nameof(screens));
            AddToClassList(UssClass);
            style.flexDirection = FlexDirection.Row;
            style.flexGrow = 1;

            Navigation = new VisualElement();
            Navigation.AddToClassList(NavClass);
            Add(Navigation);
            foreach (ScreenDefinition screen in screens)
            {
                NavItem item = Ui.NavItem(screen.Icon, screen.TitleKey);
                item.TooltipKey = screen.TooltipKey;
                string id = screen.Id;
                item.clicked += () => Open(id);
                item.RegisterCallback<NavigationMoveEvent>(OnItemNavigationMove);
                _items.Add(item);
                Navigation.Add(item);
            }

            if (!string.IsNullOrEmpty(version))
            {
                // A version number reads the same in every language.
                Version = new Label("v" + version);
                Version.AddToClassList(VersionClass);
                Version.AddToClassList("vv-text-mono");
                Navigation.Add(Version);
            }

            Host = new ScreenHost(screens, log);
            Host.style.flexGrow = 1;
            Add(Host);
            ApplyDeveloperMode();
        }

        /// <summary>Raised after a screen opens, by the user or by code.</summary>
        public event Action<string> Opened;

        public VisualElement Navigation { get; }

        public ScreenHost Host { get; }

        /// <summary>The navigation items, in the order of the screens.</summary>
        public IReadOnlyList<NavItem> Items => _items;

        /// <summary>Null when no version was given.</summary>
        public Label Version { get; }

        /// <summary>Developer-only screens show while this is on; turning it off leaves such a screen for Live.</summary>
        public bool DeveloperMode
        {
            get => _developerMode;
            set
            {
                if (_developerMode == value)
                {
                    return;
                }

                _developerMode = value;
                ApplyDeveloperMode();
                if (Host.Current != null)
                {
                    Open(Host.Current);
                }
            }
        }

        /// <summary>Opens <paramref name="id"/>, or the first screen (Live) when it does not show now.</summary>
        public void Open(string id)
        {
            string chosen = ScreenNavigation.Choose(_screens, id, _developerMode);
            if (chosen == null || string.Equals(chosen, Host.Current, StringComparison.Ordinal))
            {
                return;
            }

            Host.Show(chosen);
            for (int i = 0; i < _screens.Count; i++)
            {
                _items[i].Selected = string.Equals(_screens[i].Id, chosen, StringComparison.Ordinal);
            }

            Opened?.Invoke(chosen);
        }

        private void ApplyDeveloperMode()
        {
            for (int i = 0; i < _screens.Count; i++)
            {
                _items[i].style.display = ScreenNavigation.IsVisible(_screens[i], _developerMode) ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void OnItemNavigationMove(NavigationMoveEvent evt)
        {
            int step = evt.direction == NavigationMoveEvent.Direction.Up ? -1 : evt.direction == NavigationMoveEvent.Direction.Down ? 1 : 0;
            if (step == 0)
            {
                return;
            }

            var visible = new List<NavItem>();
            for (int i = 0; i < _screens.Count; i++)
            {
                if (ScreenNavigation.IsVisible(_screens[i], _developerMode))
                {
                    visible.Add(_items[i]);
                }
            }

            int index = ScreenNavigation.Step(visible.IndexOf(evt.currentTarget as NavItem), step, visible.Count);
            if (index >= 0)
            {
                visible[index].Focus();
            }

            // Unity's own move would pick a spatial neighbour, possibly in the screen area.
            evt.StopPropagation();
            focusController?.IgnoreEvent(evt);
        }
    }
}
