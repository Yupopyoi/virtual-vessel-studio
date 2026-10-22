using System;
using UnityEngine;

namespace VirtualVessel.Avatar
{
    /// <summary>The parts of the face the user can tune (avatar detailed design 7.4).</summary>
    public enum FaceTuningPart
    {
        /// <summary>How easily the eyes close.</summary>
        Eyes,

        /// <summary>How easily one eye closes alone.</summary>
        Wink,

        /// <summary>How much the brows show: surprise, anger, and sadness.</summary>
        Brows,

        Mouth,

        Gaze,

        Smile,
    }

    /// <summary>
    /// How strongly the user's face moves the avatar's, part by part, as written in avatar-settings.json
    /// (avatar detailed design 7.4, 10). Faces move differently, so this belongs to the user, not to an
    /// avatar. Each value is a sensitivity: 1 is the default, 2 gives the full expression for half the
    /// movement.
    /// </summary>
    public sealed class FaceTuning : IEquatable<FaceTuning>
    {
        public const float Min = 0.5f;
        public const float Max = 3f;

        public static readonly int PartCount = Enum.GetValues(typeof(FaceTuningPart)).Length;

        public float Eyes { get; set; } = 1f;

        public float Wink { get; set; } = 1f;

        public float Brows { get; set; } = 1f;

        public float Mouth { get; set; } = 1f;

        public float Gaze { get; set; } = 1f;

        public float Smile { get; set; } = 1f;

        /// <summary>
        /// Whether one eye may close alone. Off by default: a wink is easily misread (one eye squints
        /// when the head turns), so both eyes close together unless the user turns it on (user's
        /// decision, 2026-10-21).
        /// </summary>
        public bool Winks { get; set; }

        /// <summary>A copy with winks allowed or not. Pure.</summary>
        public FaceTuning WithWinks(bool winks)
        {
            FaceTuning next = Clone();
            next.Winks = winks;
            return next;
        }

        /// <summary>The value of <paramref name="part"/>, within <see cref="Min"/> to <see cref="Max"/>. Pure.</summary>
        public float Get(FaceTuningPart part)
        {
            float value = part switch
            {
                FaceTuningPart.Eyes => Eyes,
                FaceTuningPart.Wink => Wink,
                FaceTuningPart.Brows => Brows,
                FaceTuningPart.Mouth => Mouth,
                FaceTuningPart.Gaze => Gaze,
                FaceTuningPart.Smile => Smile,
                _ => throw new ArgumentOutOfRangeException(nameof(part), part, null),
            };
            return Clamp(value);
        }

        /// <summary>A copy with <paramref name="part"/> set to <paramref name="value"/>, kept within range. Pure.</summary>
        public FaceTuning With(FaceTuningPart part, float value)
        {
            FaceTuning next = Clone();
            value = Clamp(value);
            switch (part)
            {
                case FaceTuningPart.Eyes:
                    next.Eyes = value;
                    break;
                case FaceTuningPart.Wink:
                    next.Wink = value;
                    break;
                case FaceTuningPart.Brows:
                    next.Brows = value;
                    break;
                case FaceTuningPart.Mouth:
                    next.Mouth = value;
                    break;
                case FaceTuningPart.Gaze:
                    next.Gaze = value;
                    break;
                case FaceTuningPart.Smile:
                    next.Smile = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(part), part, null);
            }

            return next;
        }

        public FaceTuning Clone()
        {
            return (FaceTuning)MemberwiseClone();
        }

        /// <summary>
        /// Within <see cref="Min"/> to <see cref="Max"/>; a value a hand-edited or newer file put outside
        /// is read as the nearest end, and 1 when it is not a number. Pure.
        /// </summary>
        public static float Clamp(float value)
        {
            return float.IsNaN(value) ? 1f : Mathf.Clamp(value, Min, Max);
        }

        public bool Equals(FaceTuning other)
        {
            if (other == null || Winks != other.Winks)
            {
                return false;
            }

            for (int i = 0; i < PartCount; i++)
            {
                if (!Get((FaceTuningPart)i).Equals(other.Get((FaceTuningPart)i)))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is FaceTuning other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Get(FaceTuningPart.Eyes), Get(FaceTuningPart.Wink), Get(FaceTuningPart.Brows), Get(FaceTuningPart.Mouth), Get(FaceTuningPart.Gaze), Get(FaceTuningPart.Smile), Winks);
        }
    }
}
