using System;
using UnityEngine;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// How strongly the user's fingers bend the avatar's, as written in avatar-settings.json (avatar detailed
    /// design 7.5, 10). How far a hand reads bent depends on the person and the camera, so this belongs to the
    /// user, not to an avatar. 1 is the default: the bend read is scaled by it after an open hand's reading
    /// is taken off, so an open hand stays open at any value.
    /// </summary>
    public sealed class FingerTuning : IEquatable<FingerTuning>
    {
        public const float Min = 0.5f;
        public const float Max = 2f;

        public static readonly FingerTuning Default = new FingerTuning();

        /// <summary>The four fingers.</summary>
        public float Bend { get; set; } = 1f;

        /// <summary>The thumb's bend. A tucked thumb folds by where its tip is, which this leaves alone.</summary>
        public float Thumb { get; set; } = 1f;

        /// <summary>A copy with the four fingers' strength, kept within range. Pure.</summary>
        public FingerTuning WithBend(float bend)
        {
            return new FingerTuning { Bend = Clamp(bend), Thumb = Clamp(Thumb) };
        }

        /// <summary>A copy with the thumb's strength, kept within range. Pure.</summary>
        public FingerTuning WithThumb(float thumb)
        {
            return new FingerTuning { Bend = Clamp(Bend), Thumb = Clamp(thumb) };
        }

        /// <summary>
        /// Within <see cref="Min"/> to <see cref="Max"/>; a value a hand-edited or newer file put outside
        /// is read as the nearest end, and 1 when it is not a number. Pure.
        /// </summary>
        public static float Clamp(float value)
        {
            return float.IsNaN(value) ? 1f : Mathf.Clamp(value, Min, Max);
        }

        /// <summary>A copy with every value within range, or the defaults for none (a file written before tuning existed). Pure.</summary>
        public static FingerTuning Normalized(FingerTuning tuning)
        {
            return tuning == null ? new FingerTuning() : new FingerTuning { Bend = Clamp(tuning.Bend), Thumb = Clamp(tuning.Thumb) };
        }

        public bool Equals(FingerTuning other)
        {
            return other != null && Bend.Equals(other.Bend) && Thumb.Equals(other.Thumb);
        }

        public override bool Equals(object obj)
        {
            return obj is FingerTuning other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Bend, Thumb);
        }
    }
}
