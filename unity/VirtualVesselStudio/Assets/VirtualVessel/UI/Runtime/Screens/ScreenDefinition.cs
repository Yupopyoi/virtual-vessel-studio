using System;
using UnityEngine.UIElements;
using VirtualVessel.UI.Icons;

namespace VirtualVessel.UI.Screens
{
    /// <summary>
    /// Optional for screens: told when the screen is shown and hidden, for work done only while
    /// visible, such as refreshing a table.
    /// </summary>
    public interface IScreen
    {
        void OnShown();

        void OnHidden();
    }

    /// <summary>One screen of the navigation (UI foundation detailed design 7.7).</summary>
    public sealed class ScreenDefinition
    {
        /// <param name="id">Stable id stored in <c>lastScreen</c>, such as "live".</param>
        /// <param name="create">Builds the screen the first time it opens.</param>
        /// <param name="developerOnly">Shown only while Developer Mode is on.</param>
        public ScreenDefinition(string id, IconName icon, string titleKey, string tooltipKey, Func<VisualElement> create, bool developerOnly = false)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("A screen needs an id.", nameof(id));
            }

            Id = id;
            Icon = icon;
            TitleKey = titleKey;
            TooltipKey = tooltipKey;
            Create = create ?? throw new ArgumentNullException(nameof(create));
            DeveloperOnly = developerOnly;
        }

        public string Id { get; }

        public IconName Icon { get; }

        public string TitleKey { get; }

        public string TooltipKey { get; }

        public Func<VisualElement> Create { get; }

        public bool DeveloperOnly { get; }
    }
}
