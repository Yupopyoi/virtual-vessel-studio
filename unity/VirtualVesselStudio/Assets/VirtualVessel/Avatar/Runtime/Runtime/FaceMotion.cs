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
    /// The state lives here; the decisions are the pure <see cref="NextPresence"/> and
    /// <see cref="Progress"/>. Buffers are reused, so a frame allocates nothing (CLAUDE.md 23).
    /// </remarks>
    public sealed class FaceMotion
    {
        /// <summary>A lost face keeps its last values this long before it starts going back.</summary>
        public const double HoldSeconds = 0.5;

        /// <summary>The time to go from the tracked face back to neutral.</summary>
        public const double FadeOutSeconds = 1.0;

        /// <summary>The time to come back to the tracked face once it is found again.</summary>
        public const double FadeInSeconds = 0.2;

        // Bounds of the interval moved over: at least one 120 Hz frame, and at most a short pause, so a
        // late observation does not drag the face along slowly.
        private const double MinInterval = 1.0 / 120.0;
        private const double MaxInterval = 0.2;

        private readonly ExpressionMapping _mapping;
        private readonly FaceTarget _observed = new FaceTarget();
        private readonly FaceTarget _from = new FaceTarget();
        private readonly FaceTarget _to = new FaceTarget();
        private readonly FaceTarget _shown = new FaceTarget();
        private readonly FaceTarget _neutral = new FaceTarget();
        private double _observedAt = double.NaN;
        private double _arrivedAt;
        private double _interval = MinInterval;
        private double _lastNow = double.NaN;

        public FaceMotion(ExpressionMapping mapping)
        {
            _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        }

        /// <summary>What the avatar's face shows this frame.</summary>
        public FaceTarget Current { get; } = new FaceTarget();

        /// <summary>How much of the tracked face is shown: 1 tracked, 0 neutral.</summary>
        public float Presence { get; private set; }

        /// <param name="face">The tracked face this frame; null when there is no tracking.</param>
        /// <param name="now">Seconds on the clock of the face's timestamps.</param>
        public void Update(FacePose face, bool mirror, double now)
        {
            float elapsed = double.IsNaN(_lastNow) ? 0f : (float)Math.Max(0.0, now - _lastNow);
            _lastNow = now;

            bool usable = face != null && face.IsUsable;
            if (usable && face.Timestamp != _observedAt)
            {
                Observe(face, mirror, now);
            }

            FaceTarget.Lerp(_from, _to, Progress(now, _arrivedAt, _interval), _shown);
            double lostFor = face == null || double.IsNaN(_observedAt) ? double.PositiveInfinity : now - face.Timestamp;
            Presence = NextPresence(Presence, usable, lostFor, elapsed);
            FaceTarget.Lerp(_neutral, _shown, Presence, Current);
        }

        /// <summary>
        /// How far along from the shown face to the newest observation: 0 when it arrived, 1 one
        /// interval later, and held at 1 when the next one is late (no guessing ahead). Pure.
        /// </summary>
        public static float Progress(double now, double arrivedAt, double interval)
        {
            return interval > 0.0 ? Mathf.Clamp01((float)((now - arrivedAt) / interval)) : 1f;
        }

        /// <summary>
        /// The next presence: up over <see cref="FadeInSeconds"/> while tracked, held for
        /// <see cref="HoldSeconds"/> after the face is lost, then down over <see cref="FadeOutSeconds"/>. Pure.
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

        private void Observe(FacePose face, bool mirror, double now)
        {
            _mapping.Evaluate(face, mirror, _observed);
            _observed.Head = HeadPose.FromTracking(face.HeadRotation, mirror);

            // Starting from nothing shown, there is no earlier face to move from: begin at the observation
            // and let the presence fade it in.
            bool fresh = double.IsNaN(_observedAt) || Presence <= 0f;
            if (!fresh && face.Status == TrackingStatus.LowConfidence)
            {
                // An unsure observation moves the face only part of the way (avatar detailed design 8.4).
                FaceTarget.Lerp(_to, _observed, face.Confidence, _observed);
            }

            _from.CopyFrom(fresh ? _observed : _shown);
            _to.CopyFrom(_observed);
            _interval = fresh ? MinInterval : Math.Min(MaxInterval, Math.Max(MinInterval, face.Timestamp - _observedAt));
            _observedAt = face.Timestamp;
            _arrivedAt = now;
        }
    }
}
