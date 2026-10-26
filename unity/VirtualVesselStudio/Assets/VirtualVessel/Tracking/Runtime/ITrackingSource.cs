using System;
using VirtualVessel.Core.State;

namespace VirtualVessel.Tracking
{
    /// <summary>
    /// What other modules use (tracking detailed design 5.1): the latest smoothed observation of one
    /// person. All members are used on the main thread.
    /// </summary>
    public interface ITrackingSource
    {
        IReadOnlyObservable<TrackingState> State { get; }

        IReadOnlyObservable<TrackingProblem> Problem { get; }

        /// <summary>Parts the provider can track.</summary>
        TrackingRegions Supported { get; }

        /// <summary>The user's choices; saved and restored at the next start.</summary>
        IReadOnlyObservable<TrackingOptions> Options { get; }

        /// <summary>
        /// The latest smoothed observation, updated every frame. The same object is returned each time
        /// and overwritten by the next update, so read it within the frame and do not keep it.
        /// </summary>
        TrackingFrame Current { get; }

        /// <summary>Camera images inferred per second over the last second; 0 while nothing arrives.</summary>
        double ObservationsPerSecond { get; }

        void Start();

        void Stop();

        /// <summary>Applies new choices; a change of parts takes effect at once.</summary>
        void SetOptions(TrackingOptions options);
    }

    /// <summary>
    /// An input method that produces raw observations in the common form (tracking detailed design 5.2).
    /// A new input such as mocopi is added by implementing this contract.
    /// </summary>
    public interface ITrackingProvider : IDisposable
    {
        TrackingRegions Supported { get; }

        /// <summary>What kind of observation this is, for the corrections that suit it (tracking detailed design 8.4).</summary>
        ObservationTraits Traits { get; }

        IReadOnlyObservable<TrackingState> State { get; }

        /// <summary>Returns without waiting; the result comes through <see cref="State"/>.</summary>
        void Start(TrackingOptions options);

        /// <summary>Changes the options while running; inference for disabled parts stops.</summary>
        void SetOptions(TrackingOptions options);

        void Stop();

        /// <summary>Called every frame on the main thread to advance reading the input.</summary>
        void Tick();

        /// <summary>
        /// Copies the latest observation into <paramref name="into"/>; false when there is nothing new.
        /// Copying into the caller's object avoids a per-frame allocation (CLAUDE.md 15.1, 23).
        /// </summary>
        bool TryTakeLatest(TrackingFrame into);
    }
}
