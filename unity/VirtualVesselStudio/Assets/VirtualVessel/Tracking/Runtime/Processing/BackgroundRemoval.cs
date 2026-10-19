using System;

namespace VirtualVessel.Tracking.Processing
{
    /// <summary>
    /// Paints the background of a camera image gray from a person mask, before the landmarks are found
    /// (tracking detailed design 9.4): with less behind the person, the body points are not pulled
    /// toward furniture or other things in the room.
    /// </summary>
    public static class BackgroundRemoval
    {
        /// <summary>A plain mid gray: what the models see most often around a cut-out person.</summary>
        public const byte Gray = 128;

        /// <summary>Below this person confidence a pixel is background.</summary>
        public const float Background = 0.2f;

        /// <summary>Above this a pixel is the person; between, the two are blended, which keeps soft edges.</summary>
        public const float Person = 0.6f;

        /// <summary>How much of the camera pixel to keep for a person confidence: 0 to 1, smooth between the two limits. Pure.</summary>
        public static float Weight(float confidence)
        {
            float t = Math.Min(1f, Math.Max(0f, (confidence - Background) / (Person - Background)));
            return t * t * (3f - (2f * t));
        }

        /// <summary>
        /// Writes <paramref name="rgba"/> (top row first, 4 bytes a pixel) into <paramref name="output"/>
        /// with the background gray. <paramref name="mask"/> is the person confidence, top row first, of any
        /// size; it is sampled at each pixel. Writing into the caller's buffer avoids allocating per camera
        /// image (CLAUDE.md 15.1, 23).
        /// </summary>
        public static void Apply(ReadOnlySpan<byte> rgba, Span<byte> output, int width, int height, ReadOnlySpan<float> mask, int maskWidth, int maskHeight)
        {
            int pixels = width * height;
            if (rgba.Length < pixels * 4 || output.Length < pixels * 4)
            {
                throw new ArgumentException("The image buffers are smaller than the size.");
            }

            if (maskWidth <= 0 || maskHeight <= 0 || mask.Length < maskWidth * maskHeight)
            {
                throw new ArgumentException("The mask is smaller than its size.", nameof(mask));
            }

            for (int y = 0; y < height; y++)
            {
                int maskRow = (int)((long)y * maskHeight / height) * maskWidth;
                int row = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    float keep = Weight(mask[maskRow + (int)((long)x * maskWidth / width)]);
                    int i = row + (x * 4);
                    output[i] = Blend(rgba[i], keep);
                    output[i + 1] = Blend(rgba[i + 1], keep);
                    output[i + 2] = Blend(rgba[i + 2], keep);
                    output[i + 3] = 255;
                }
            }
        }

        private static byte Blend(byte value, float keep)
        {
            return (byte)(Gray + ((value - Gray) * keep) + 0.5f);
        }
    }
}
