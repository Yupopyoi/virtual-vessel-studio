using System;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// The seconds since the last rendered frame, worked out in one place for every step of the avatar's
    /// motion: inside <see cref="ObservationClock"/>, and for the steps that follow every frame rather than
    /// observations, the clearance (avatar detailed design 7.6) and the arms' turning limit (7.8).
    /// </summary>
    public sealed class FrameClock
    {
        private double _lastNow = double.NaN;

        /// <summary>Seconds since the last call, never negative; <paramref name="first"/> on the first call, when it is 0.</summary>
        public float Tick(double now, out bool first)
        {
            first = double.IsNaN(_lastNow);
            float elapsed = first ? 0f : (float)Math.Max(0.0, now - _lastNow);
            _lastNow = now;
            return elapsed;
        }
    }

    /// <summary>
    /// The timing of one tracked part's observations (avatar detailed design 7.3): when the newest was taken
    /// and arrived, and how far along the move toward it is. Shared by the face, the body, and the hands.
    /// </summary>
    public sealed class ObservationClock
    {
        private readonly FrameClock _frames = new FrameClock();
        private double _arrivedAt;
        private double _interval = MotionTiming.MinInterval;

        /// <summary>When the newest observation was taken; NaN before the first.</summary>
        public double ObservedAt { get; private set; } = double.NaN;

        public bool HasObserved => !double.IsNaN(ObservedAt);

        /// <summary>Seconds since the last frame, 0 on the first; called once a frame.</summary>
        public float Tick(double now)
        {
            return _frames.Tick(now, out _);
        }

        /// <summary>Whether an observation taken at <paramref name="timestamp"/> is not the newest one already taken.</summary>
        public bool IsNew(double timestamp)
        {
            return timestamp != ObservedAt;
        }

        /// <summary>
        /// Takes an observation taken at <paramref name="timestamp"/>, arriving <paramref name="now"/>; the move
        /// toward it takes the time since the one before (<see cref="MotionTiming.Interval"/>).
        /// </summary>
        public void Arrive(bool fresh, double timestamp, double now)
        {
            _interval = MotionTiming.Interval(fresh, timestamp, ObservedAt);
            ObservedAt = timestamp;
            _arrivedAt = now;
        }

        /// <summary>How far along the move toward the newest observation is, 0 to 1 (<see cref="MotionTiming.Progress"/>).</summary>
        public float Progress(double now)
        {
            return MotionTiming.Progress(now, _arrivedAt, _interval);
        }
    }
}
