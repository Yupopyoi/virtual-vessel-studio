using UnityEngine.UIElements;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// Shows the focus outline only when focus came from the keyboard (components.md 2), because USS has
    /// no <c>:focus-visible</c>.
    /// </summary>
    public static class FocusRing
    {
        public const string RingClass = "vv-focus-ring";

        public const string VisibleClass = "vv-focus-visible";

        /// <summary>Adds the outline element to <paramref name="element"/> and makes it focusable.</summary>
        public static void Attach(VisualElement element)
        {
            element.focusable = true;
            var ring = new VisualElement { pickingMode = PickingMode.Ignore };
            ring.AddToClassList(RingClass);
            element.hierarchy.Add(ring);
            element.RegisterCallback<FocusEvent>(evt => element.EnableInClassList(VisibleClass, IsFromKeyboard(evt.direction)));
            element.RegisterCallback<BlurEvent>(_ => element.RemoveFromClassList(VisibleClass));
        }

        /// <summary>Tab and arrow navigation carry a direction; pointer clicks and code do not.</summary>
        public static bool IsFromKeyboard(FocusChangeDirection direction)
        {
            return direction != null && direction != FocusChangeDirection.unspecified && direction != FocusChangeDirection.none;
        }
    }
}
