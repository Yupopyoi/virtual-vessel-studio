using UnityEngine.UIElements;
using VirtualVessel.UI.Theme;

namespace VirtualVessel.UI.Controls
{
    /// <summary>Where layers over the whole UI go, such as menus and full-screen views.</summary>
    internal static class ThemedRoot
    {
        /// <summary>The nearest themed root above <paramref name="element"/>, or the top of its tree.</summary>
        public static VisualElement Of(VisualElement element)
        {
            VisualElement top = element;
            for (VisualElement current = element; current != null; current = current.hierarchy.parent)
            {
                if (current.ClassListContains(ThemeService.RootClass))
                {
                    return current;
                }

                top = current;
            }

            return top;
        }
    }
}
