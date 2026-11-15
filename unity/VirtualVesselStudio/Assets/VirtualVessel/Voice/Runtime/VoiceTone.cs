using System;

namespace VirtualVessel.Voice
{
    /// <summary>
    /// The finish on a converted voice (system design 9.9; Voice detailed design 8.6): four gentle filter bands, each in
    /// dB from -6 to +6, 0 leaving the voice as it is. Kept with each voice (the user's decision, 2026-11-15), so a voice
    /// sounds the same in every project.
    /// </summary>
    public readonly struct VoiceTone : IEquatable<VoiceTone>
    {
        public const float Limit = 6f;

        public VoiceTone(float body, float clarity, float brightness, float air)
        {
            Body = Clamp(body);
            Clarity = Clamp(clarity);
            Brightness = Clamp(brightness);
            Air = Clamp(air);
        }

        /// <summary>Thin to full: a low shelf at <see cref="ToneFilter.BodyHz"/>.</summary>
        public float Body { get; }

        /// <summary>Muffled to clear: a cut of the low middle at <see cref="ToneFilter.ClarityHz"/> as it rises.</summary>
        public float Clarity { get; }

        /// <summary>Dark to bright: a high shelf at <see cref="ToneFilter.BrightnessHz"/>.</summary>
        public float Brightness { get; }

        /// <summary>None to airy: a high shelf at <see cref="ToneFilter.AirHz"/>.</summary>
        public float Air { get; }

        /// <summary>Whether it changes nothing, so the filter can be left out.</summary>
        public bool IsFlat => Body == 0f && Clarity == 0f && Brightness == 0f && Air == 0f;

        /// <summary>Within the limits, as a hand-edited file may hold any number. Pure.</summary>
        public static float Clamp(float decibels)
        {
            return float.IsNaN(decibels) ? 0f : Math.Max(-Limit, Math.Min(Limit, decibels));
        }

        public bool Equals(VoiceTone other)
        {
            return Body == other.Body && Clarity == other.Clarity && Brightness == other.Brightness && Air == other.Air;
        }

        public override bool Equals(object obj)
        {
            return obj is VoiceTone other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Body, Clarity, Brightness, Air);
        }

        public static bool operator ==(VoiceTone left, VoiceTone right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(VoiceTone left, VoiceTone right)
        {
            return !left.Equals(right);
        }
    }
}
