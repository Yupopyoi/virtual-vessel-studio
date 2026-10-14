using System.Collections.Generic;
using UnityEngine;

namespace VirtualVessel.UI.Frames
{
    /// <summary>
    /// Sets the mouse cursor while editing placement. The USS <c>cursor</c> property only works in Editor
    /// UI, so runtime panels draw their own cursor images (UI foundation detailed design 9.3).
    /// </summary>
    /// <remarks>
    /// Static because the hardware cursor is a single, application-wide resource. Main thread only. The
    /// few cursor textures are made once and kept for the application's lifetime.
    /// </remarks>
    public static class CursorService
    {
        private static readonly Dictionary<CursorKind, Texture2D> s_textures = new Dictionary<CursorKind, Texture2D>();
        private static CursorKind s_current;

        public static CursorKind Current => s_current;

        public static void Set(CursorKind kind)
        {
            if (kind == s_current)
            {
                return;
            }

            s_current = kind;
            if (kind == CursorKind.Default)
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                return;
            }

            if (!s_textures.TryGetValue(kind, out Texture2D texture))
            {
                texture = new Texture2D(CursorShapes.Size, CursorShapes.Size, TextureFormat.RGBA32, false)
                {
                    name = "VvCursor" + kind,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave,
                };
                texture.SetPixels32(CursorShapes.Draw(kind));
                texture.Apply();
                s_textures[kind] = texture;
            }

            Cursor.SetCursor(texture, new Vector2(CursorShapes.Size / 2, CursorShapes.Size / 2), CursorMode.Auto);
        }

        public static void Reset()
        {
            Set(CursorKind.Default);
        }
    }

    /// <summary>Cursor images: a white shape with a dark outline, readable on any background. Pure.</summary>
    public static class CursorShapes
    {
        public const int Size = 32;

        private const int Samples = 4;

        // A double-headed arrow along +X, centered on the origin, in cursor pixels.
        private static readonly Vector2[] s_arrow =
        {
            new Vector2(-13, 0), new Vector2(-7, -6), new Vector2(-7, -1.7f), new Vector2(7, -1.7f), new Vector2(7, -6),
            new Vector2(13, 0), new Vector2(7, 6), new Vector2(7, 1.7f), new Vector2(-7, 1.7f), new Vector2(-7, 6),
        };

        // Axis-aligned arrows look heavier than diagonal ones at the same size, so their shaft and heads
        // are slimmer (checked in the UI sandbox).
        private static readonly Vector2[] s_slim =
        {
            new Vector2(-12, 0), new Vector2(-7, -5), new Vector2(-7, -1.1f), new Vector2(7, -1.1f), new Vector2(7, -5),
            new Vector2(12, 0), new Vector2(7, 5), new Vector2(7, 1.1f), new Vector2(-7, 1.1f), new Vector2(-7, 5),
        };

        /// <summary>The pixels of <paramref name="kind"/>, bottom row first as <c>Texture2D</c> expects.</summary>
        public static Color32[] Draw(CursorKind kind)
        {
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float fill = 0;
                    float edge = 0;
                    for (int sy = 0; sy < Samples; sy++)
                    {
                        for (int sx = 0; sx < Samples; sx++)
                        {
                            // Texture rows are bottom-up; the shapes are defined with Y down.
                            var p = new Vector2(x + ((sx + 0.5f) / Samples) - (Size / 2f), (Size - 1 - y) + ((sy + 0.5f) / Samples) - (Size / 2f));
                            if (Inside(kind, p))
                            {
                                fill++;
                            }
                            else if (Near(kind, p))
                            {
                                edge++;
                            }
                        }
                    }

                    float alpha = Mathf.Clamp01((fill + edge) / (Samples * Samples));
                    byte white = (byte)(fill + edge > 0 ? fill / (fill + edge) * 255 : 0);
                    pixels[(y * Size) + x] = new Color32(white, white, white, (byte)(alpha * 255));
                }
            }

            return pixels;
        }

        /// <summary>Whether a point, relative to the hotspot, is inside the white shape.</summary>
        public static bool Inside(CursorKind kind, Vector2 p)
        {
            switch (kind)
            {
                case CursorKind.ResizeHorizontal:
                    return InPolygon(s_slim, p);
                case CursorKind.ResizeVertical:
                    return InPolygon(s_slim, Rotate(p, -90));
                case CursorKind.ResizeDiagonalDown:
                    return InPolygon(s_arrow, Rotate(p, -45));
                case CursorKind.ResizeDiagonalUp:
                    return InPolygon(s_arrow, Rotate(p, 45));
                case CursorKind.Move:
                    return InPolygon(s_slim, p) || InPolygon(s_slim, Rotate(p, -90));
                case CursorKind.NotAllowed:
                    float r = p.magnitude;
                    Vector2 q = Rotate(p, 45);
                    return (r <= 10 && r >= 6.5f) || (Mathf.Abs(q.y) <= 1.8f && Mathf.Abs(q.x) <= 8);
                default:
                    return false;
            }
        }

        // The dark outline: points just outside the white shape.
        private static bool Near(CursorKind kind, Vector2 p)
        {
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                if (Inside(kind, p + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 1.6f)))
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector2 Rotate(Vector2 p, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            return new Vector2((p.x * cos) - (p.y * sin), (p.x * sin) + (p.y * cos));
        }

        private static bool InPolygon(Vector2[] polygon, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                if ((polygon[i].y > p.y) != (polygon[j].y > p.y)
                    && p.x < ((polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y)) + polygon[i].x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}
