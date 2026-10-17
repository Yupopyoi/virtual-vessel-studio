using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVessel.UI.Icons
{
    /// <summary>The project's own icons (UI foundation detailed design 6.6).</summary>
    public enum IconName
    {
        None,
        Live,
        Setup,
        Lab,
        Diagnostics,
        Mic,
        MicOff,
        Fullscreen,
        ExitFullscreen,
        Check,
        Warning,
        Close,
        Info,
        ChevronDown,
        Plus,
        Minus,

        /// <summary>Filled dot: the "live" status shape.</summary>
        Dot,

        /// <summary>Outline circle: the "idle" status shape.</summary>
        Ring,

        /// <summary>Three bars: something that can be dragged, such as a layer list row.</summary>
        Grip,

        /// <summary>A 2×2 grid of squares: the component gallery.</summary>
        Gallery,
    }

    /// <summary>One part of an icon: a line drawn with the text color, or a filled area.</summary>
    public sealed class IconPart
    {
        public IconPart(IReadOnlyList<IconPolyline> lines, bool filled)
        {
            Lines = lines ?? throw new ArgumentNullException(nameof(lines));
            Filled = filled;
        }

        public IReadOnlyList<IconPolyline> Lines { get; }

        public bool Filled { get; }
    }

    /// <summary>
    /// Icon shapes on a 24×24 grid with 2-unit round strokes, the same rules as the styleboard icons
    /// (docs/assets/ui/styleboard.html). Shapes are written as SVG path data so that they can be drawn
    /// and checked in any SVG viewer.
    /// </summary>
    /// <remarks>Parsed once when first used and never changed afterwards.</remarks>
    public static class IconLibrary
    {
        public const float GridSize = 24f;

        public const float StrokeWidth = 2f;

        /// <summary>The SVG path data of each icon, for review and tests. Filled parts are prefixed with "fill:".</summary>
        internal static IReadOnlyDictionary<IconName, string[]> Sources { get; } = new Dictionary<IconName, string[]>
        {
            [IconName.Live] = new[] { "fill:M9 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0Z", "M5.6 5.6a9 9 0 0 0 0 12.8M18.4 5.6a9 9 0 0 1 0 12.8" },
            [IconName.Setup] = new[] { "M4 7h10M18 7h2M4 17h4M12 17h8", "M14 7a2 2 0 1 0 4 0a2 2 0 1 0 -4 0Z", "M8 17a2 2 0 1 0 4 0a2 2 0 1 0 -4 0Z" },
            [IconName.Lab] = new[] { "M9 3v6l-5 9a2 2 0 0 0 1.8 3h12.4a2 2 0 0 0 1.8-3l-5-9V3M8 3h8" },
            [IconName.Diagnostics] = new[] { "M3 12h4l3-7 4 14 3-7h4" },
            [IconName.Mic] = new[] { "M9 6a3 3 0 0 1 6 0v5a3 3 0 0 1 -6 0Z", "M5 11a7 7 0 0 0 14 0M12 18v3" },
            [IconName.MicOff] = new[] { "M9 6a3 3 0 0 1 6 0v5a3 3 0 0 1 -6 0Z", "M5 11a7 7 0 0 0 14 0M12 18v3", "M3 3l18 18" },
            [IconName.Fullscreen] = new[] { "M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5" },
            [IconName.ExitFullscreen] = new[] { "M9 4v5H4M15 4v5h5M9 20v-5H4M15 20v-5h5" },
            [IconName.Check] = new[] { "M5 12.5l4.5 4.5L19 7.5" },
            [IconName.Warning] = new[] { "M12 3.5L21.5 20H2.5Z", "M12 9.5v4.5", "fill:M10.8 17a1.2 1.2 0 1 0 2.4 0a1.2 1.2 0 1 0 -2.4 0Z" },
            [IconName.Close] = new[] { "M6 6l12 12M18 6L6 18" },
            [IconName.Info] = new[] { "M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0Z", "M12 11v5", "fill:M10.8 7.8a1.2 1.2 0 1 0 2.4 0a1.2 1.2 0 1 0 -2.4 0Z" },
            [IconName.ChevronDown] = new[] { "M6 9l6 6l6 -6" },
            [IconName.Plus] = new[] { "M12 5v14M5 12h14" },
            [IconName.Minus] = new[] { "M5 12h14" },
            [IconName.Dot] = new[] { "fill:M7 12a5 5 0 1 0 10 0a5 5 0 1 0 -10 0Z" },
            [IconName.Ring] = new[] { "M6.5 12a5.5 5.5 0 1 0 11 0a5.5 5.5 0 1 0 -11 0Z" },
            [IconName.Grip] = new[] { "M6 8h12M6 12h12M6 16h12" },
            [IconName.Gallery] = new[] { "M4 4h6v6H4Z", "M14 4h6v6h-6Z", "M4 14h6v6H4Z", "M14 14h6v6h-6Z" },
        };

        // Declared after Sources: static initializers run in source order.
        private static readonly Dictionary<IconName, IReadOnlyList<IconPart>> s_icons = Build();

        public static IReadOnlyList<IconPart> Get(IconName name)
        {
            return s_icons.TryGetValue(name, out IReadOnlyList<IconPart> parts) ? parts : Array.Empty<IconPart>();
        }

        private static Dictionary<IconName, IReadOnlyList<IconPart>> Build()
        {
            var icons = new Dictionary<IconName, IReadOnlyList<IconPart>>();
            foreach (KeyValuePair<IconName, string[]> icon in Sources)
            {
                var parts = new List<IconPart>();
                foreach (string source in icon.Value)
                {
                    bool filled = source.StartsWith("fill:", StringComparison.Ordinal);
                    parts.Add(new IconPart(IconGeometry.ParsePath(filled ? source.Substring(5) : source), filled));
                }

                icons.Add(icon.Key, parts);
            }

            return icons;
        }
    }
}
