using System;
using System.Collections.Generic;

namespace VirtualVessel.UI.Screens
{
    /// <summary>Which screens show and which one opens (UI foundation detailed design 7.7). Pure.</summary>
    public static class ScreenNavigation
    {
        public static bool IsVisible(ScreenDefinition screen, bool developerMode)
        {
            return !screen.DeveloperOnly || developerMode;
        }

        /// <summary>
        /// The screen to open: <paramref name="wanted"/> when it exists and shows, otherwise the first
        /// screen that shows (Live). Used at startup with lastScreen and when Developer Mode turns off.
        /// </summary>
        public static string Choose(IReadOnlyList<ScreenDefinition> screens, string wanted, bool developerMode)
        {
            string first = null;
            foreach (ScreenDefinition screen in screens)
            {
                if (!IsVisible(screen, developerMode))
                {
                    continue;
                }

                if (string.Equals(screen.Id, wanted, StringComparison.Ordinal))
                {
                    return screen.Id;
                }

                first ??= screen.Id;
            }

            return first;
        }

        /// <summary>The item index after an Up (-1) or Down (+1) key, staying within the list.</summary>
        public static int Step(int current, int step, int count)
        {
            if (count <= 0)
            {
                return -1;
            }

            return Math.Max(0, Math.Min(count - 1, current + step));
        }
    }
}
