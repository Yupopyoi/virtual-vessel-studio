using System;
using UnityEngine;

namespace VirtualVessel.UI.Tooltips
{
    /// <summary>A component that can show a tooltip (components.md 3.12).</summary>
    public interface ITooltipSource
    {
        /// <summary>The string key to show now, such as the reason a button cannot be pressed; null for none.</summary>
        string CurrentTooltipKey { get; }

        /// <summary>A shortcut shown at the right of the tooltip, such as "Ctrl+M"; null for none.</summary>
        string Shortcut { get; }
    }

    /// <summary>
    /// Tooltip timing and spacing. These are defaults that may change; screens read them from here
    /// instead of repeating the numbers (UI foundation detailed design 7.6).
    /// </summary>
    public sealed class TooltipOptions
    {
        /// <summary>How long the pointer rests on a part before its tooltip appears.</summary>
        public TimeSpan ShowDelay { get; set; } = TimeSpan.FromSeconds(0.5);

        /// <summary>Space between the part and the tooltip, in pixels.</summary>
        public float Gap { get; set; } = 6f;

        /// <summary>Space kept from the screen edges, in pixels.</summary>
        public float EdgeMargin { get; set; } = 8f;
    }

    /// <summary>Where a tooltip goes. Pure.</summary>
    public static class TooltipPlacement
    {
        /// <summary>
        /// The top-left position of a tooltip of <paramref name="size"/> for a part at
        /// <paramref name="target"/>: centered above it, below it when there is no room above, and moved
        /// sideways so that it stays inside <paramref name="bounds"/>.
        /// </summary>
        public static Vector2 Place(Rect target, Vector2 size, Rect bounds, float gap, float edgeMargin)
        {
            float minX = bounds.xMin + edgeMargin;
            float maxX = bounds.xMax - edgeMargin - size.x;
            float x = target.center.x - (size.x / 2f);
            x = maxX < minX ? minX : Mathf.Clamp(x, minX, maxX);

            float above = target.yMin - gap - size.y;
            float y = above >= bounds.yMin + edgeMargin
                ? above
                : Mathf.Min(target.yMax + gap, bounds.yMax - edgeMargin - size.y);
            return new Vector2(x, Mathf.Max(bounds.yMin + edgeMargin, y));
        }
    }
}
