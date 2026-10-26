namespace VirtualVessel.Tracking.Processing
{
    /// <summary>The tracking state of one part between observations.</summary>
    public readonly struct PartTracking
    {
        public PartTracking(TrackingStatus status, int observedInARow, double lastObserved)
        {
            Status = status;
            ObservedInARow = observedInARow;
            LastObserved = lastObserved;
        }

        /// <summary>Not seen yet.</summary>
        public static PartTracking Initial => new PartTracking(TrackingStatus.Lost, 0, double.NegativeInfinity);

        public TrackingStatus Status { get; }

        public int ObservedInARow { get; }

        /// <summary>Image time of the last observation.</summary>
        public double LastObserved { get; }
    }

    /// <summary>Thresholds of <see cref="TrackingStateMachine"/>.</summary>
    public readonly struct TrackingThresholds
    {
        public TrackingThresholds(float lowConfidence, double lostAfter, int trackedAfter)
        {
            LowConfidence = lowConfidence;
            LostAfter = lostAfter;
            TrackedAfter = trackedAfter;
        }

        /// <summary>Confidence 0.5, lost after 0.2 s, tracked again after 2 observations (tracking detailed design 7.3).</summary>
        public static TrackingThresholds Default => new TrackingThresholds(0.5f, 0.2, 2);

        public float LowConfidence { get; }

        public double LostAfter { get; }

        public int TrackedAfter { get; }
    }

    /// <summary>
    /// Decides each part's status (tracking detailed design 7.3). It goes Lost by time and back to
    /// Tracked only after observations in a row, so a loss of a frame or two does not flicker. Pure.
    /// </summary>
    public static class TrackingStateMachine
    {
        /// <param name="observed">Whether a new observation of the part arrived.</param>
        /// <param name="observedAt">Its image time.</param>
        /// <param name="now">The current time on the same clock.</param>
        public static PartTracking Next(
            PartTracking current,
            bool supported,
            bool enabled,
            bool observed,
            float confidence,
            double observedAt,
            double now,
            in TrackingThresholds thresholds)
        {
            if (!supported)
            {
                return new PartTracking(TrackingStatus.NotSupported, 0, current.LastObserved);
            }

            if (!enabled)
            {
                return new PartTracking(TrackingStatus.Disabled, 0, current.LastObserved);
            }

            if (observed)
            {
                int inARow = current.ObservedInARow + 1;
                bool wasUsable = current.Status == TrackingStatus.Tracked || current.Status == TrackingStatus.LowConfidence;
                TrackingStatus status = wasUsable || inARow >= thresholds.TrackedAfter
                    ? (confidence >= thresholds.LowConfidence ? TrackingStatus.Tracked : TrackingStatus.LowConfidence)
                    : TrackingStatus.Lost;
                return new PartTracking(status, inARow, observedAt);
            }

            if (current.Status == TrackingStatus.Disabled || current.Status == TrackingStatus.NotSupported)
            {
                return new PartTracking(TrackingStatus.Lost, 0, current.LastObserved);
            }

            return now - current.LastObserved > thresholds.LostAfter
                ? new PartTracking(TrackingStatus.Lost, 0, current.LastObserved)
                : current;
        }
    }
}
