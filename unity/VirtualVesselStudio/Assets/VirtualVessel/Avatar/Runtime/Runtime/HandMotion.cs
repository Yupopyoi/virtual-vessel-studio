using System;
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
    /// The state lives here; the palm is the pure <see cref="HandRetargeting"/>, the timing the pure
    /// <see cref="MotionTiming"/>. Nothing is allocated per frame (CLAUDE.md 23).
    /// </remarks>
    public sealed class HandMotion
    {
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
            private double _observedAt = double.NaN;
            private double _arrivedAt;
            private double _interval = MotionTiming.MinInterval;
            private double _lastNow = double.NaN;

            public Quaternion Shown { get; private set; } = Quaternion.identity;

            public float Presence { get; private set; }

            public void Update(HandPose hand, bool mirror, double now)
            {
                float elapsed = double.IsNaN(_lastNow) ? 0f : (float)Math.Max(0.0, now - _lastNow);
                _lastNow = now;
                bool usable = HandRetargeting.TryPalm(hand, mirror, out Quaternion palm);
                if (usable && hand.Timestamp != _observedAt)
                {
                    // Starting from nothing shown, there is no earlier palm to move from.
                    bool fresh = double.IsNaN(_observedAt) || Presence <= 0f;
                    _from = fresh ? palm : Shown;
                    _to = palm;
                    _interval = MotionTiming.Interval(fresh, hand.Timestamp, _observedAt);
                    _observedAt = hand.Timestamp;
                    _arrivedAt = now;
                }

                Shown = Quaternion.Slerp(_from, _to, MotionTiming.Progress(now, _arrivedAt, _interval));
                double lostFor = hand == null || double.IsNaN(_observedAt) ? double.PositiveInfinity : now - _observedAt;
                Presence = MotionTiming.NextPresence(Presence, usable, lostFor, elapsed);
            }
        }
    }
}
