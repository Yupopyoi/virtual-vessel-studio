using System;
using UnityEngine;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// How one tracked face value turns into an expression weight (avatar detailed design 7.4): values at
    /// or below <see cref="Lower"/> give 0, values at or above <see cref="Upper"/> give <see cref="Gain"/>,
    /// with a smooth start and end in between, so small noise near either end does not show.
    /// </summary>
    public readonly struct ExpressionCurve : IEquatable<ExpressionCurve>
    {
        public ExpressionCurve(float lower, float upper, float gain = 1f)
        {
            if (!(upper > lower))
            {
                throw new ArgumentException($"The upper end ({upper}) must be above the lower end ({lower}).", nameof(upper));
            }

            Lower = lower;
            Upper = upper;
            Gain = gain;
        }

        public float Lower { get; }

        public float Upper { get; }

        /// <summary>The weight at the upper end; below 1 makes an expression weaker.</summary>
        public float Gain { get; }

        /// <summary>
        /// The same curve for a face that moves <paramref name="sensitivity"/> times as easily: both ends
        /// divided by it, so 2 reaches the full weight with half the movement. Pure.
        /// </summary>
        public ExpressionCurve Sensitive(float sensitivity)
        {
            return sensitivity > 0f ? new ExpressionCurve(Lower / sensitivity, Upper / sensitivity, Gain) : this;
        }

        /// <summary>The weight for <paramref name="value"/>. Pure.</summary>
        public float Evaluate(float value)
        {
            float t = Mathf.Clamp01((value - Lower) / (Upper - Lower));
            return t * t * (3f - (2f * t)) * Gain;
        }

        public bool Equals(ExpressionCurve other)
        {
            return Lower.Equals(other.Lower) && Upper.Equals(other.Upper) && Gain.Equals(other.Gain);
        }

        public override bool Equals(object obj)
        {
            return obj is ExpressionCurve other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Lower, Upper, Gain);
        }

        public override string ToString()
        {
            return $"{Lower:0.##}..{Upper:0.##} x{Gain:0.##}";
        }
    }
}
