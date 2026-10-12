using System;
using System.Globalization;

namespace VirtualVessel.UI.Editor.Theme
{
    /// <summary>
    /// An sRGB color with alpha, and the calculations USS cannot do: HSL, mixing, and WCAG contrast.
    /// </summary>
    internal readonly struct ThemeColor : IEquatable<ThemeColor>
    {
        public ThemeColor(byte r, byte g, byte b, float a = 1f)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public byte R { get; }

        public byte G { get; }

        public byte B { get; }

        public float A { get; }

        public static ThemeColor FromHex(string hex)
        {
            if (hex == null || hex.Length != 7 || hex[0] != '#')
            {
                throw new FormatException($"Expected #rrggbb but got '{hex}'.");
            }

            return new ThemeColor(
                byte.Parse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        /// <summary>CSS hsl(): hue in degrees, saturation and lightness in percent.</summary>
        public static ThemeColor FromHsl(double hue, double saturation, double lightness)
        {
            double h = ((hue % 360) + 360) % 360 / 360;
            double s = saturation / 100;
            double l = lightness / 100;
            if (s == 0)
            {
                byte gray = ToByte(l);
                return new ThemeColor(gray, gray, gray);
            }

            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            return new ThemeColor(ToByte(HueToRgb(p, q, h + 1.0 / 3)), ToByte(HueToRgb(p, q, h)), ToByte(HueToRgb(p, q, h - 1.0 / 3)));
        }

        /// <summary>CSS color-mix in sRGB: <paramref name="amount"/> of this color over <paramref name="other"/>.</summary>
        public ThemeColor Mix(ThemeColor other, double amount)
        {
            return new ThemeColor(
                ToByte((R * amount + other.R * (1 - amount)) / 255),
                ToByte((G * amount + other.G * (1 - amount)) / 255),
                ToByte((B * amount + other.B * (1 - amount)) / 255));
        }

        public ThemeColor WithAlpha(float alpha)
        {
            return new ThemeColor(R, G, B, alpha);
        }

        /// <summary>This color drawn over an opaque <paramref name="background"/>.</summary>
        public ThemeColor Over(ThemeColor background)
        {
            return A >= 1 ? this : Mix(background, A);
        }

        /// <summary>WCAG 2 relative luminance.</summary>
        public double Luminance()
        {
            static double Channel(byte value)
            {
                double c = value / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(R) + 0.7152 * Channel(G) + 0.0722 * Channel(B);
        }

        /// <summary>WCAG 2 contrast ratio, from 1 to 21. Translucent colors are composited first.</summary>
        public static double Contrast(ThemeColor foreground, ThemeColor background)
        {
            double a = foreground.Over(background).Luminance();
            double b = background.Luminance();
            return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        }

        /// <summary>USS value: #rrggbb when opaque, rgba() otherwise.</summary>
        public string ToUss()
        {
            if (A >= 1)
            {
                return string.Format(CultureInfo.InvariantCulture, "#{0:x2}{1:x2}{2:x2}", R, G, B);
            }

            return string.Format(CultureInfo.InvariantCulture, "rgba({0}, {1}, {2}, {3:0.00})", R, G, B, A);
        }

        public bool Equals(ThemeColor other)
        {
            return R == other.R && G == other.G && B == other.B && A.Equals(other.A);
        }

        public override bool Equals(object obj)
        {
            return obj is ThemeColor other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (R << 16) ^ (G << 8) ^ B ^ A.GetHashCode();
        }

        public override string ToString()
        {
            return ToUss();
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0)
            {
                t += 1;
            }

            if (t > 1)
            {
                t -= 1;
            }

            if (t < 1.0 / 6)
            {
                return p + (q - p) * 6 * t;
            }

            if (t < 0.5)
            {
                return q;
            }

            if (t < 2.0 / 3)
            {
                return p + (q - p) * (2.0 / 3 - t) * 6;
            }

            return p;
        }

        private static byte ToByte(double unit)
        {
            return (byte)Math.Round(Math.Max(0, Math.Min(1, unit)) * 255, MidpointRounding.AwayFromZero);
        }
    }
}
