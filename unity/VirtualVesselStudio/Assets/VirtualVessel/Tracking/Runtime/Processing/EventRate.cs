namespace VirtualVessel.Tracking.Processing
{
    /// <summary>
    /// How many events happen per second, over windows of about a second, such as inferred images.
    /// Immutable: each event returns the next meter, so the counting is a pure function of the times.
    /// </summary>
    public readonly struct EventRate
    {
        private readonly double _windowStart;
        private readonly double _last;
        private readonly int _count;
        private readonly bool _started;

        private EventRate(double windowStart, double last, int count, double rate)
        {
            _windowStart = windowStart;
            _last = last;
            _count = count;
            _started = true;
            Rate = rate;
        }

        /// <summary>Events per second over the last full window; 0 until a window is complete.</summary>
        public double Rate { get; }

        /// <summary>The rate as it stands at <paramref name="now"/>: 0 once a second has passed without an event.</summary>
        public double RateAt(double now)
        {
            return _started && now - _last < 1.0 ? Rate : 0;
        }

        /// <summary>
        /// The meter after an event at <paramref name="now"/> seconds. The first event, or one after a
        /// gap of a second or more, starts over so that the gap does not lower the rate.
        /// </summary>
        public EventRate Next(double now)
        {
            if (!_started || now - _last >= 1.0)
            {
                return new EventRate(now, now, 0, 0);
            }

            int count = _count + 1;
            double elapsed = now - _windowStart;
            return elapsed < 1.0
                ? new EventRate(_windowStart, now, count, Rate)
                : new EventRate(now, now, 0, count / elapsed);
        }
    }
}
