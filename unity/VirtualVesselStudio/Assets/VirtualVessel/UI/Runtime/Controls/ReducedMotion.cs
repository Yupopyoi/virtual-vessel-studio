using UnityEngine.UIElements;
using VirtualVessel.UI.Theme;

namespace VirtualVessel.UI.Controls
{
    /// <summary>Whether the "reduce motion" setting applies to an element (design-system.md 7).</summary>
    public static class ReducedMotion
    {
        /// <summary>True when the element or an ancestor carries the reduced-motion class.</summary>
        public static bool IsOn(VisualElement element)
        {
            for (VisualElement current = element; current != null; current = current.hierarchy.parent)
            {
                if (current.ClassListContains(ThemeService.ReducedMotionClass))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
