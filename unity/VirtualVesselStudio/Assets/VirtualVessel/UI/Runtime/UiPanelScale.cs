using UnityEngine;

namespace VirtualVessel.UI
{
    /// <summary>
    /// How large the UI is drawn (UI foundation detailed design 7.9): the Windows display scale, but no
    /// larger than keeps <see cref="MinWidth"/> × <see cref="MinHeight"/> of layout in the window, times the
    /// user's UI size. The screens are laid out for at least that much: on a 4K monitor at 225%, a Full HD
    /// window held only 853 × 480, and the setup screen's columns were squeezed until their text stood one
    /// character a line (the user's screenshot, 2026-10-24). The display scale is never taken below 100% for
    /// this, and the user's UI size, chosen to read more easily, is always applied in full.
    /// </summary>
    public static class UiPanelScale
    {
        /// <summary>The least layout, in UI pixels, the window holds before the UI is drawn smaller.</summary>
        public const float MinWidth = 1280f;
        public const float MinHeight = 720f;

        /// <summary>Windows lays out at 96 DPI for 100% display scale.</summary>
        public const float ReferenceDpi = 96f;

        /// <summary>
        /// The size the UI is drawn at, 1 for one UI pixel per screen pixel: the display scale of
        /// <paramref name="dpi"/> (96 when unknown), limited to what keeps <see cref="MinWidth"/> ×
        /// <see cref="MinHeight"/> in a window of <paramref name="width"/> × <paramref name="height"/> pixels
        /// but not below 1, times <paramref name="uiScale"/>. Pure.
        /// </summary>
        public static float Drawn(float uiScale, float dpi, int width, int height)
        {
            float display = DisplayScale(dpi);
            if (width > 0 && height > 0)
            {
                float fits = Mathf.Min(width / MinWidth, height / MinHeight);
                display = Mathf.Min(display, Mathf.Max(fits, 1f));
            }

            return display * uiScale;
        }

        /// <summary>
        /// The <see cref="UnityEngine.UIElements.PanelSettings.scale"/> that draws the UI at
        /// <see cref="Drawn"/> on a panel that already follows the display scale
        /// (<see cref="UnityEngine.UIElements.PanelScaleMode.ConstantPhysicalSize"/>). Pure.
        /// </summary>
        public static float PanelScale(float uiScale, float dpi, int width, int height)
        {
            return Drawn(uiScale, dpi, width, height) / DisplayScale(dpi);
        }

        /// <summary>The Windows display scale of <paramref name="dpi"/>: 1 at 96, and 1 when unknown (0 or less). Pure.</summary>
        public static float DisplayScale(float dpi)
        {
            return dpi > 0f ? dpi / ReferenceDpi : 1f;
        }
    }
}
