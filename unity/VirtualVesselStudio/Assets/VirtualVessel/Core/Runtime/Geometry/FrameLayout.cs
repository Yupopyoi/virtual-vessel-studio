using System;

namespace VirtualVessel.Core.Geometry
{
    /// <summary>
    /// Placement of one video, such as a game screen or a second monitor, on the stream screen
    /// (UI foundation detailed design 8.2).
    /// </summary>
    /// <remarks>
    /// Shared by the UI, which edits it, and Stage, which stores it and renders the video accordingly.
    /// </remarks>
    public readonly struct FrameLayout : IEquatable<FrameLayout>
    {
        public FrameLayout(NormalizedRect placement, NormalizedRect crop, bool flipHorizontal = false, bool locked = false, bool allowOutside = false, int order = 0)
        {
            Placement = placement;
            Crop = crop;
            FlipHorizontal = flipHorizontal;
            Locked = locked;
            AllowOutside = allowOutside;
            Order = order;
        }

        /// <summary>Position and size on the stream screen, which is 0 to 1 on each axis.</summary>
        public NormalizedRect Placement { get; }

        /// <summary>The part of the video that is shown, where the whole video is 0 to 1.</summary>
        public NormalizedRect Crop { get; }

        public bool FlipHorizontal { get; }

        /// <summary>When true, moving, resizing, and cropping are not accepted.</summary>
        public bool Locked { get; }

        /// <summary>When true, the frame may extend beyond the stream screen.</summary>
        public bool AllowOutside { get; }

        /// <summary>Stacking order; larger values are in front.</summary>
        public int Order { get; }

        public static FrameLayout Create(NormalizedRect placement)
        {
            return new FrameLayout(placement, NormalizedRect.Full);
        }

        public FrameLayout WithPlacement(NormalizedRect placement)
        {
            return new FrameLayout(placement, Crop, FlipHorizontal, Locked, AllowOutside, Order);
        }

        public FrameLayout WithCrop(NormalizedRect crop)
        {
            return new FrameLayout(Placement, crop, FlipHorizontal, Locked, AllowOutside, Order);
        }

        public FrameLayout WithFlipHorizontal(bool flipHorizontal)
        {
            return new FrameLayout(Placement, Crop, flipHorizontal, Locked, AllowOutside, Order);
        }

        public FrameLayout WithLocked(bool locked)
        {
            return new FrameLayout(Placement, Crop, FlipHorizontal, locked, AllowOutside, Order);
        }

        public FrameLayout WithAllowOutside(bool allowOutside)
        {
            return new FrameLayout(Placement, Crop, FlipHorizontal, Locked, allowOutside, Order);
        }

        public FrameLayout WithOrder(int order)
        {
            return new FrameLayout(Placement, Crop, FlipHorizontal, Locked, AllowOutside, order);
        }

        public bool Equals(FrameLayout other)
        {
            return Placement.Equals(other.Placement)
                && Crop.Equals(other.Crop)
                && FlipHorizontal == other.FlipHorizontal
                && Locked == other.Locked
                && AllowOutside == other.AllowOutside
                && Order == other.Order;
        }

        public override bool Equals(object obj)
        {
            return obj is FrameLayout other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Placement.GetHashCode();
                hash = (hash * 397) ^ Crop.GetHashCode();
                hash = (hash * 397) ^ (FlipHorizontal ? 1 : 0);
                hash = (hash * 397) ^ (Locked ? 2 : 0);
                hash = (hash * 397) ^ (AllowOutside ? 4 : 0);
                hash = (hash * 397) ^ Order;
                return hash;
            }
        }

        public static bool operator ==(FrameLayout left, FrameLayout right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(FrameLayout left, FrameLayout right)
        {
            return !left.Equals(right);
        }
    }
}
