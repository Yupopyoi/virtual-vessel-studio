using UnityEngine;

namespace VirtualVessel.Stage
{
    /// <summary>
    /// The picture of the provisional backdrop behind the avatar (Stage detailed design 7.1), until a Stage
    /// Scene brings its own: a pastel gradient from pink through lavender to sky blue, a soft glow behind
    /// the avatar, white polka dots, and a few stars and hearts in the side bands, away from the avatar.
    /// Drawn in code, so no image file is bundled. Pure: the same size always gives the same picture.
    /// </summary>
    public static class PastelBackdrop
    {
        public const int Width = 1600;
        public const int Height = 900;

        private static readonly Color s_top = new Color(1.00f, 0.85f, 0.92f);
        private static readonly Color s_middle = new Color(0.91f, 0.87f, 1.00f);
        private static readonly Color s_bottom = new Color(0.84f, 0.94f, 1.00f);

        private static readonly Color[] s_shapeColors =
        {
            new Color(1.00f, 0.95f, 0.66f),
            new Color(1.00f, 0.73f, 0.86f),
            new Color(0.74f, 0.95f, 0.85f),
            new Color(0.80f, 0.78f, 1.00f),
        };

        /// <summary>The picture, rows from the bottom as Unity's textures take them.</summary>
        public static Color32[] Render(int width, int height)
        {
            var pixels = new Color[width * height];
            float dotSpacing = height * 0.06f;
            float dotRadius = dotSpacing * 0.13f;
            for (int y = 0; y < height; y++)
            {
                float v = (float)y / (height - 1);
                Color row = Gradient(v);
                for (int x = 0; x < width; x++)
                {
                    float u = (float)x / (width - 1);

                    // Brighter and calmer behind the avatar, so it stands out.
                    float glow = Mathf.Exp(-((u - 0.5f) * (u - 0.5f) * 9f) - ((v - 0.5f) * (v - 0.5f) * 5f));
                    Color color = Color.Lerp(row, Color.white, 0.4f * glow);
                    float dot = Coverage(DotDistance(x, y, dotSpacing) - dotRadius);
                    pixels[(y * width) + x] = Color.Lerp(color, Color.white, dot * 0.5f * (1f - (0.7f * glow)));
                }
            }

            DrawShapes(pixels, width, height);
            var result = new Color32[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                result[i] = pixels[i];
            }

            return result;
        }

        /// <summary>The background color at <paramref name="v"/>, 0 at the bottom. Pure.</summary>
        public static Color Gradient(float v)
        {
            return v > 0.5f
                ? Color.Lerp(s_middle, s_top, Mathf.SmoothStep(0f, 1f, (v - 0.5f) * 2f))
                : Color.Lerp(s_bottom, s_middle, Mathf.SmoothStep(0f, 1f, v * 2f));
        }

        /// <summary>How far pixel (<paramref name="x"/>, <paramref name="y"/>) is from the nearest dot center; every other row is offset by half.</summary>
        private static float DotDistance(int x, int y, float spacing)
        {
            float row = Mathf.Round(y / spacing);
            float offset = (Mathf.Abs(row) % 2f) * spacing * 0.5f;
            float column = Mathf.Round((x - offset) / spacing);
            float dx = x - ((column * spacing) + offset);
            float dy = y - (row * spacing);
            return Mathf.Sqrt((dx * dx) + (dy * dy));
        }

        /// <summary>How much of a pixel a shape covers, from its signed distance in pixels (negative inside), with a soft one-pixel edge.</summary>
        private static float Coverage(float distance)
        {
            return Mathf.Clamp01(0.5f - distance);
        }

        private static void DrawShapes(Color[] pixels, int width, int height)
        {
            // A fixed sequence, so the picture never changes between runs.
            uint seed = 0x9E3779B9u;
            for (int i = 0; i < 22; i++)
            {
                bool left = i % 2 == 0;
                float u = left ? Next(ref seed, 0.07f, 0.30f) : Next(ref seed, 0.70f, 0.93f);
                float v = Next(ref seed, 0.08f, 0.92f);
                float size = height * Next(ref seed, 0.022f, 0.045f);
                float turn = Next(ref seed, -0.35f, 0.35f);
                Color color = s_shapeColors[(int)Next(ref seed, 0f, s_shapeColors.Length - 0.001f)];
                bool heart = i % 3 == 1;
                Draw(pixels, width, height, u * (width - 1), v * (height - 1), size, turn, color, heart);
            }
        }

        private static void Draw(Color[] pixels, int width, int height, float cx, float cy, float size, float turn, Color color, bool heart)
        {
            int reach = Mathf.CeilToInt(size * 1.4f) + 2;
            float cos = Mathf.Cos(turn);
            float sin = Mathf.Sin(turn);
            for (int y = Mathf.Max(0, (int)cy - reach); y <= Mathf.Min(height - 1, (int)cy + reach); y++)
            {
                for (int x = Mathf.Max(0, (int)cx - reach); x <= Mathf.Min(width - 1, (int)cx + reach); x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float lx = ((dx * cos) + (dy * sin)) / size;
                    float ly = ((-dx * sin) + (dy * cos)) / size;
                    float distance = (heart ? Heart(lx, ly) : Star(lx, ly)) * size;
                    float cover = Coverage(distance) * 0.85f;
                    if (cover > 0f)
                    {
                        int i = (y * width) + x;
                        pixels[i] = Color.Lerp(pixels[i], color, cover);
                    }
                }
            }
        }

        /// <summary>A rough signed distance to a heart of about unit size, upright. Pure.</summary>
        private static float Heart(float x, float y)
        {
            // The classic implicit heart, (x² + y² - 1)³ - x² y³ ≤ 0, scaled to its size; its value grows
            // roughly with the distance near the edge, which is enough for a soft edge.
            x *= 1.15f;
            y = (y * 1.15f) + 0.15f;
            float a = (x * x) + (y * y) - 1f;
            float f = (a * a * a) - (x * x * y * y * y);
            return Mathf.Sign(f) * Mathf.Pow(Mathf.Abs(f), 1f / 3f) * 0.5f;
        }

        /// <summary>The signed distance to a rounded five-pointed star of unit outer radius, by its radius at each angle. Pure.</summary>
        private static float Star(float x, float y)
        {
            float radius = Mathf.Sqrt((x * x) + (y * y));
            float angle = Mathf.Atan2(x, y);
            float edge = Mathf.Lerp(0.5f, 1f, Mathf.Pow((Mathf.Cos(angle * 5f) + 1f) * 0.5f, 1.5f));
            return radius - edge;
        }

        /// <summary>The next value from <paramref name="seed"/>, between <paramref name="min"/> and <paramref name="max"/>.</summary>
        private static float Next(ref uint seed, float min, float max)
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            return min + ((max - min) * (seed / (float)uint.MaxValue));
        }
    }
}
