using UnityEngine.UIElements;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Tooltips;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// A help hint (components.md 3.28): an Info icon after a name, whose tooltip explains the item. It keeps the screen to
    /// the names and puts what only some need to read one step away (the user's request, 2026-11-11). It takes keyboard
    /// focus, so the tooltip shows from the keyboard too (components.md 3.12).
    /// </summary>
    public sealed class HelpHint : VisualElement, ITooltipSource
    {
        public const string UssClass = "vv-help-hint";

        public HelpHint(string tooltipKey)
        {
            AddToClassList(UssClass);
            focusable = true;
            tabIndex = 0;
            Add(new Icon(IconName.Info) { pickingMode = PickingMode.Ignore });
            FocusRing.Attach(this);
            TooltipKey = tooltipKey;
        }

        public string TooltipKey { get; set; }

        string ITooltipSource.CurrentTooltipKey => TooltipKey;

        string ITooltipSource.Shortcut => null;
    }
}
