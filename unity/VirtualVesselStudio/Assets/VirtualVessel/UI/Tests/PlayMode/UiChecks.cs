using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.Tests
{
    /// <summary>
    /// Checks of what a screen shows, for the problems the user's reviews keep finding (2026-11-11): text that cannot be
    /// read on a background, and controls side by side with no room between them (ui-guidelines 3).
    /// </summary>
    internal static class UiChecks
    {
        /// <summary>design-system.md 9.2: large text, icons, and boundaries 3:1; any text shown is held to at least that.</summary>
        public const double MinimumContrast = 3.0;

        /// <summary>The S space (8px) between controls in a row.</summary>
        public const float MinimumGap = 8f;

        /// <summary>
        /// Shown, enabled text below the contrast against what is behind it, as "text: ratio". Text with no solid
        /// background of its own is checked against <paramref name="screen"/>, the background the screen is drawn on.
        /// </summary>
        public static List<string> Unreadable(VisualElement root, Color screen)
        {
            var found = new List<string>();
            root.Query<TextElement>().ForEach(text =>
            {
                if (string.IsNullOrWhiteSpace(text.text) || !Shown(text) || !text.enabledInHierarchy)
                {
                    return;
                }

                double ratio = Contrast(text.resolvedStyle.color, BackgroundOf(text) ?? screen);
                if (ratio < MinimumContrast)
                {
                    found.Add($"\"{text.text}\" ({string.Join(" ", text.GetClasses())}): {ratio:0.00}");
                }
            });
            return found;
        }

        /// <summary>Controls next to each other on one line with less than the S space between them.</summary>
        public static List<string> Cramped(VisualElement root)
        {
            var found = new List<string>();
            root.Query<VisualElement>().ForEach(parent =>
            {
                if (!Shown(parent) || parent.resolvedStyle.flexDirection != FlexDirection.Row)
                {
                    return;
                }

                VisualElement previous = null;
                foreach (VisualElement child in parent.Children())
                {
                    if (!IsControl(child) || !Shown(child))
                    {
                        continue;
                    }

                    if (previous != null && SameLine(previous.worldBound, child.worldBound))
                    {
                        float gap = child.worldBound.xMin - previous.worldBound.xMax;
                        if (gap < MinimumGap - 0.5f)
                        {
                            found.Add($"{Name(previous)} | {Name(child)}: {gap:0.#}px");
                        }
                    }

                    previous = child;
                }
            });
            return found;
        }

        /// <summary>
        /// Controls right under a divider (an element with a bottom border, such as a setting row) with less than the S
        /// space between the line and the control.
        /// </summary>
        public static List<string> UnderDivider(VisualElement root)
        {
            var found = new List<string>();
            root.Query<VisualElement>().ForEach(parent =>
            {
                if (!Shown(parent) || parent.resolvedStyle.flexDirection != FlexDirection.Column)
                {
                    return;
                }

                VisualElement previous = null;
                foreach (VisualElement child in parent.Children())
                {
                    if (!Shown(child))
                    {
                        continue;
                    }

                    if (previous != null && previous.resolvedStyle.borderBottomWidth > 0f)
                    {
                        VisualElement control = IsControl(child) ? child : child.Query<VisualElement>().Where(e => IsControl(e) && Shown(e)).First();
                        if (control != null && control.worldBound.yMin - previous.worldBound.yMax < MinimumGap - 0.5f)
                        {
                            found.Add($"{Name(previous)} over {Name(control)}: {control.worldBound.yMin - previous.worldBound.yMax:0.#}px");
                        }
                    }

                    previous = child;
                }
            });
            return found;
        }

        /// <summary>WCAG's contrast ratio        /// <summary>WCAG's contrast ratio, the foreground laid over the background by its alpha. Pure.</summary>
        public static double Contrast(Color foreground, Color background)
        {
            Color over = Color.Lerp(background, foreground, foreground.a);
            double a = Luminance(over);
            double b = Luminance(background);
            return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        }

        private static double Luminance(Color color)
        {
            return (0.2126 * Linear(color.r)) + (0.7152 * Linear(color.g)) + (0.0722 * Linear(color.b));
        }

        private static double Linear(float channel)
        {
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        private static bool IsControl(VisualElement element)
        {
            return element is Button || element is SwitchToggle || element is SelectField || element is ValueSlider || element is TextInput || element is Segmented;
        }

        private static bool SameLine(Rect a, Rect b)
        {
            return Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > 1f;
        }

        private static bool Shown(VisualElement element)
        {
            for (VisualElement at = element; at != null; at = at.parent)
            {
                if (at.resolvedStyle.display == DisplayStyle.None || at.resolvedStyle.visibility == Visibility.Hidden || at.resolvedStyle.opacity <= 0f)
                {
                    return false;
                }
            }

            return element.worldBound.width > 0f && element.worldBound.height > 0f;
        }

        // The nearest solid background behind the element; null when none is set.
        private static Color? BackgroundOf(VisualElement element)
        {
            for (VisualElement at = element; at != null; at = at.parent)
            {
                Color color = at.resolvedStyle.backgroundColor;
                if (color.a > 0.9f)
                {
                    return color;
                }
            }

            return null;
        }

        private static string Name(VisualElement element)
        {
            return element.GetType().Name + "(" + string.Join(" ", element.GetClasses()) + ")";
        }
    }
}
