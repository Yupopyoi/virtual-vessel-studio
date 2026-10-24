using UnityEngine;
using VirtualVessel.Tracking;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// The avatar's palms over time (avatar detailed design 7.3, 7.5, 8.4), as <see cref="FaceMotion"/> is
    /// for the face: the hands are inferred apart from the body, so each palm moves toward its newest
    /// observation over one observation interval, holds when its hand is lost, then lets the hand follow
    /// the forearm again, and fades back in when seen.
    /// </summary>
    /// <remarks>
    /// The state lives here; the palm is the pure <see cref="HandRetargeting"/>, the timing
    /// <see cref="ObservationClock"/>. Nothing is allocated per frame (CLAUDE.md 23).
    /// </remarks>
    public sealed class HandMotion
    {
        /// <summary>
        /// The most a shown palm turns in a second. A palm read the other way round for long enough to be
        /// accepted turns over a third of a second instead of flipping in a frame (the user's screen
        /// recording, 2026-10-24).
        /// </summary>
        public const float MaxDegreesPerSecond = 540f;

        private readonly Side _left = new Side();
        private readonly Side _right = new Side();

        /// <summary>The avatar's left palm in its root's space, fingers forward and the back of the hand up.</summary>
        public Quaternion LeftPalm => _left.Shown;

        public Quaternion RightPalm => _right.Shown;

        /// <summary>How much of the tracked palm is shown, per the avatar's own side: 1 tracked, 0 following the forearm.</summary>
        public float LeftPresence => _left.Presence;

        public float RightPresence => _right.Presence;

        /// <param name="frame">The tracking frame; null when there is no tracking.</param>
        /// <param name="now">Seconds on the clock of the hands' timestamps.</param>
        public void Update(TrackingFrame frame, bool mirror, double now)
        {
            _left.Update(frame == null ? null : HandRetargeting.PersonHand(frame, ArmSide.Left, mirror), mirror, now);
            _right.Update(frame == null ? null : HandRetargeting.PersonHand(frame, ArmSide.Right, mirror), mirror, now);
        }

        private sealed class Side
        {
            private Quaternion _from = Quaternion.identity;
            private Quaternion _to = Quaternion.identity;
            private readonly ObservationClock _clock = new ObservationClock();

            public Quaternion Shown { get; private set; } = Quaternion.identity;

            public float Presence { get; private set; }

            public void Update(HandPose hand, bool mirror, double now)
            {
                float elapsed = _clock.Tick(now);
                bool usable = HandRetargeting.TryPalm(hand, mirror, out Quaternion palm);
                if (usable && _clock.IsNew(hand.Timestamp))
                {
                    // Starting from nothing shown, there is no earlier palm to move from.
                    bool fresh = !_clock.HasObserved || Presence <= 0f;
                    _from = fresh ? palm : Shown;
                    _to = palm;
                    _clock.Arrive(fresh, hand.Timestamp, now);
                }

                Quaternion toward = Quaternion.Slerp(_from, _to, _clock.Progress(now));
                Shown = Presence <= 0f || elapsed <= 0f ? toward : Quaternion.RotateTowards(Shown, toward, MaxDegreesPerSecond * elapsed);
                double lostFor = hand == null || !_clock.HasObserved ? double.PositiveInfinity : now - _clock.ObservedAt;
                Presence = MotionTiming.NextPresence(Presence, usable, lostFor, elapsed);
            }
        }
    }
}
