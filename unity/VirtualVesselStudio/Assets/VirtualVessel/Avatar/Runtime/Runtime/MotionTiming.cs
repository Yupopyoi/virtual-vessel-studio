using System;
using UnityEngine;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// The timing the face and the body share (avatar detailed design 7.3, 8.4): moving from what is shown
    /// toward the newest observation over one observation interval, and how much of what is tracked is
    /// shown when it is lost and found again. Pure.
    /// </summary>
    public static class MotionTiming
    {
        /// <summary>A lost part keeps its last values this long before it starts going back.</summary>
        public const double HoldSeconds = 0.5;

        /// <summary>The time to go from the tracked values back to the rest.</summary>
        public const double FadeOutSeconds = 1.0;

        /// <summary>The time to come back to the tracked values once found again.</summary>
        public const double FadeInSeconds = 0.2;

        // Bounds of the interval moved over: at least one 120 Hz frame, and at most a short pause, so a
        // late observation does not drag the avatar along slowly.
        public const double MinInterval = 1.0 / 120.0;
        public const double MaxInterval = 0.2;

        /// <summary>
        /// How far along from what is shown to the newest observation: 0 when it arrived, 1 one interval
        /// later, and held at 1 when the next one is late (no guessing ahead).
        /// </summary>
        public static float Progress(double now, double arrivedAt, double interval)
        {
            return interval > 0.0 ? Mathf.Clamp01((float)((now - arrivedAt) / interval)) : 1f;
        }

        /// <summary>
        /// The next presence: up over <see cref="FadeInSeconds"/> while tracked, held for
        /// <see cref="HoldSeconds"/> after the part is lost, then down over <see cref="FadeOutSeconds"/>.
        /// </summary>
        public static float NextPresence(float presence, bool usable, double lostSeconds, float elapsedSeconds)
        {
            if (usable)
            {
                return Mathf.Min(1f, presence + (float)(elapsedSeconds / FadeInSeconds));
            }

            if (lostSeconds <= HoldSeconds)
            {
                return presence;
            }

            return Mathf.Max(0f, presence - (float)(elapsedSeconds / FadeOutSeconds));
        }

        /// <summary>
        /// The interval to move over toward an observation taken at <paramref name="observedAt"/>: the
        /// time since the one before, within the bounds; the shortest when there is none to move from.
        /// </summary>
        public static double Interval(bool fresh, double observedAt, double previousObservedAt)
        {
            return fresh ? MinInterval : Math.Min(MaxInterval, Math.Max(MinInterval, observedAt - previousObservedAt));
        }
    }
}
