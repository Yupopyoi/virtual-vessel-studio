using System;
using UnityEngine;
using VirtualVessel.Tracking;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// The avatar's face over time (avatar detailed design 7.3, 8.4). Every render frame it moves from
    /// what was shown toward the newest observation over one observation interval, so 30 observations a
    /// second still move smoothly at 60 frames; when the face is lost it holds, then goes back to a
    /// neutral face, and when it is found again it fades back in.
    /// </summary>
    /// <remarks>
    /// The state lives here; the timing is <see cref="ObservationClock"/>, shared with the body and the hands.
    /// Buffers are reused, so a frame allocates nothing (CLAUDE.md 23).
    /// </remarks>
    public sealed class FaceMotion
    {
        private ExpressionMapping _mapping;
        private readonly FaceTarget _observed = new FaceTarget();
        private readonly FaceTarget _from = new FaceTarget();
        private readonly FaceTarget _to = new FaceTarget();
        private readonly FaceTarget _shown = new FaceTarget();
        private readonly FaceTarget _neutral = new FaceTarget();
        private readonly ObservationClock _clock = new ObservationClock();

        public FaceMotion(ExpressionMapping mapping)
        {
            _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        }

        /// <summary>What the avatar's face shows this frame.</summary>
        public FaceTarget Current { get; } = new FaceTarget();

        /// <summary>How much of the tracked face is shown: 1 tracked, 0 neutral.</summary>
        public float Presence { get; private set; }

        /// <summary>The mapping in use; a new one, as the user tunes the face, applies from the next observation.</summary>
        public ExpressionMapping Mapping
        {
            get => _mapping;
            set => _mapping = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <param name="face">The tracked face this frame; null when there is no tracking.</param>
        /// <param name="now">Seconds on the clock of the face's timestamps.</param>
        public void Update(FacePose face, bool mirror, double now)
        {
            float elapsed = _clock.Tick(now);

            bool usable = face != null && face.IsUsable;
            if (usable && _clock.IsNew(face.Timestamp))
            {
                Observe(face, mirror, now);
            }

            FaceTarget.Lerp(_from, _to, _clock.Progress(now), _shown);
            double lostFor = face == null || !_clock.HasObserved ? double.PositiveInfinity : now - face.Timestamp;
            Presence = MotionTiming.NextPresence(Presence, usable, lostFor, elapsed);
            FaceTarget.Lerp(_neutral, _shown, Presence, Current);
        }

        private void Observe(FacePose face, bool mirror, double now)
        {
            _mapping.Evaluate(face, mirror, _observed);
            _observed.Head = HeadPose.FromTracking(face.HeadRotation, mirror);

            // Starting from nothing shown, there is no earlier face to move from: begin at the observation
            // and let the presence fade it in.
            bool fresh = !_clock.HasObserved || Presence <= 0f;
            if (!fresh && face.Status == TrackingStatus.LowConfidence)
            {
                // An unsure observation moves the face only part of the way (avatar detailed design 8.4).
                FaceTarget.Lerp(_to, _observed, face.Confidence, _observed);
            }

            _from.CopyFrom(fresh ? _observed : _shown);
            _to.CopyFrom(_observed);
            _clock.Arrive(fresh, face.Timestamp, now);
        }
    }
}
