using System;
using System.Globalization;

namespace VirtualVessel.Core.Geometry
{
    /// <summary>
    /// A rectangle in a space where the reference area is 0 to 1 on each axis, with the origin at the
    /// top left and Y growing downwards (the same as UI Toolkit).
    /// </summary>
    /// <remarks>
    /// Used for placement on the stream screen and for crop ranges of a video, so that stored values do
    /// not depend on any resolution.
    /// </remarks>
    public readonly struct NormalizedRect : IEquatable<NormalizedRect>
    {
        public static readonly NormalizedRect Full = new NormalizedRect(0, 0, 1, 1);

        public NormalizedRect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public double X { get; }

        public double Y { get; }

        public double Width { get; }

        public double Height { get; }

        public double Left => X;

        public double Top => Y;

        public double Right => X + Width;

        public double Bottom => Y + Height;

        public double CenterX => X + Width * 0.5;

        public double CenterY => Y + Height * 0.5;

        public static NormalizedRect FromEdges(double left, double top, double right, double bottom)
        {
            return new NormalizedRect(left, top, right - left, bottom - top);
        }

        public NormalizedRect WithPosition(double x, double y)
        {
            return new NormalizedRect(x, y, Width, Height);
        }

        public bool Equals(NormalizedRect other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Width.Equals(other.Width) && Height.Equals(other.Height);
        }

        public override bool Equals(object obj)
        {
            return obj is NormalizedRect other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Width.GetHashCode();
                hash = (hash * 397) ^ Height.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(NormalizedRect left, NormalizedRect right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(NormalizedRect left, NormalizedRect right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.####}, {1:0.####}, {2:0.####} x {3:0.####})", X, Y, Width, Height);
        }
    }
}
