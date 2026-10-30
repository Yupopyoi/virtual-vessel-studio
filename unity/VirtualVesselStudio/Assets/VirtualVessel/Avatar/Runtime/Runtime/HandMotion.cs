using UnityEngine;
using VirtualVessel.Tracking;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// The avatar's palms and fingers over time (avatar detailed design 7.3, 7.5, 8.4), as
    /// <see cref="FaceMotion"/> is for the face: the hands are inferred apart from the body, so each palm and
    /// its fingers move toward their newest observation over one observation interval, hold when the hand is
    /// lost, then let the hand follow the forearm again with the fingers loosely closed, and fade back in
    /// when seen.
    /// </summary>
    /// <remarks>
    /// The state lives here; the palm is the pure <see cref="HandRetargeting"/>, the fingers the pure
    /// <see cref="FingerRetargeting"/>, the timing
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

        /// <summary>
        /// The most a shown finger angle changes in a second: 10° a frame at 60, a fist closing in about
        /// 0.15 s. On the user's recording "1024不自然解消用" a finger angle otherwise jumped up to 48° in a
        /// frame (2026-10-24).
        /// </summary>
        public const float MaxFingerDegreesPerSecond = 600f;

        private readonly Side _left = new Side(ArmSide.Left);
        private readonly Side _right = new Side(ArmSide.Right);

        /// <summary>The avatar's left palm in its root's space, fingers forward and the back of the hand up.</summary>
        public Quaternion LeftPalm => _left.Shown;

        public Quaternion RightPalm => _right.Shown;

        /// <summary>How much of the tracked palm is shown, per the avatar's own side: 1 tracked, 0 following the forearm.</summary>
        public float LeftPresence => _left.Presence;

        public float RightPresence => _right.Presence;

        /// <summary>The avatar's left fingers: as tracked, or loosely closed (<see cref="FingerPose.Relaxed"/>) as far as the hand is not shown.</summary>
        public FingerPose LeftFingers => _left.Fingers;

        public FingerPose RightFingers => _right.Fingers;

        /// <summary>The user's finger strength; used from the next observation.</summary>
        public FingerTuning Tuning { get; set; } = FingerTuning.Default;

        /// <param name="frame">The tracking frame; null when there is no tracking.</param>
        /// <param name="now">Seconds on the clock of the hands' timestamps.</param>
        public void Update(TrackingFrame frame, bool mirror, double now)
        {
            _left.Update(frame == null ? null : HandRetargeting.PersonHand(frame, ArmSide.Left, mirror), mirror, now, Tuning);
            _right.Update(frame == null ? null : HandRetargeting.PersonHand(frame, ArmSide.Right, mirror), mirror, now, Tuning);
        }

        private sealed class Side
        {
            private Quaternion _from = Quaternion.identity;
            private Quaternion _to = Quaternion.identity;
            private readonly ObservationClock _clock = new ObservationClock();
            private readonly ArmSide _side;
            private readonly FingerPose _measured = new FingerPose();
            private readonly FingerPose _fromFingers = new FingerPose();
            private readonly FingerPose _toFingers = new FingerPose();
            private readonly FingerPose _towardFingers = new FingerPose();
            private readonly FingerPose _presentFingers = new FingerPose();

            public Side(ArmSide side)
            {
                _side = side;
                Fingers.CopyFrom(FingerPose.Relaxed);
            }

            public Quaternion Shown { get; private set; } = Quaternion.identity;

            public FingerPose Fingers { get; } = new FingerPose();

            public float Presence { get; private set; }

            public void Update(HandPose hand, bool mirror, double now, FingerTuning tuning)
            {
                float elapsed = _clock.Tick(now);
                bool usable = HandRetargeting.TryPalm(hand, mirror, out Quaternion palm);
                if (usable && _clock.IsNew(hand.Timestamp))
                {
                    // Starting from nothing shown, there is no earlier palm to move from.
                    bool fresh = !_clock.HasObserved || Presence <= 0f;
                    _from = fresh ? palm : Shown;
                    _to = palm;

                    // The person's hand on the avatar's side is the other one in a mirror; its thumb is on its own side.
                    if (!FingerRetargeting.TryMeasure(hand, _side.IsLeft != mirror, _measured, tuning, mirror))
                    {
                        _measured.CopyFrom(_toFingers);
                    }

                    _fromFingers.CopyFrom(fresh ? _measured : _towardFingers);
                    _toFingers.CopyFrom(_measured);
                    _clock.Arrive(fresh, hand.Timestamp, now);
                }

                float progress = _clock.Progress(now);
                Quaternion toward = Quaternion.Slerp(_from, _to, progress);
                FingerPose.Lerp(_fromFingers, _toFingers, progress, _towardFingers);
                Shown = Presence <= 0f || elapsed <= 0f ? toward : Quaternion.RotateTowards(Shown, toward, MaxDegreesPerSecond * elapsed);
                double lostFor = hand == null || !_clock.HasObserved ? double.PositiveInfinity : now - _clock.ObservedAt;
                Presence = MotionTiming.NextPresence(Presence, usable, lostFor, elapsed);
                FingerPose.Lerp(FingerPose.Relaxed, _towardFingers, Presence, _presentFingers);
                FingerPose.MoveTowards(Fingers, _presentFingers, MaxFingerDegreesPerSecond * elapsed, Fingers);
            }
        }
    }
}
