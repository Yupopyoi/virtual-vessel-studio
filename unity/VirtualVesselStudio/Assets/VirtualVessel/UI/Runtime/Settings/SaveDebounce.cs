namespace VirtualVessel.UI.Settings
{
    /// <summary>
    /// When to save after a burst of changes: once, a fixed delay after the last one
    /// (UI foundation detailed design 7.9). An immutable value; each method returns the next state.
    /// </summary>
    internal readonly struct SaveDebounce
    {
        private SaveDebounce(bool pending, long dueTimestamp)
        {
            Pending = pending;
            DueTimestamp = dueTimestamp;
        }

        public static SaveDebounce Idle => default;

        public bool Pending { get; }

        /// <summary>Monotonic timestamp at which the save is due; meaningful only while pending.</summary>
        public long DueTimestamp { get; }

        /// <summary>A change happened at <paramref name="now"/>: the save moves to <paramref name="delayTicks"/> after it.</summary>
        public SaveDebounce Changed(long now, long delayTicks)
        {
            return new SaveDebounce(true, now + delayTicks);
        }

        public bool IsDue(long now)
        {
            return Pending && now >= DueTimestamp;
        }
    }
}
