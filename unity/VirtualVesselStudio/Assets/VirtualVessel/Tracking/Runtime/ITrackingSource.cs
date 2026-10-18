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

        /// <summary>Parts the user enabled.</summary>
        IReadOnlyObservable<TrackingRegions> Enabled { get; }

        IReadOnlyObservable<BodyRange> BodyRange { get; }

        /// <summary>
        /// The latest smoothed observation, updated every frame. The same object is returned each time
        /// and overwritten by the next update, so read it within the frame and do not keep it.
        /// </summary>
        TrackingFrame Current { get; }

        void Start();

        void Stop();

        void SetEnabled(TrackingRegions regions);

        void SetBodyRange(BodyRange range);

        /// <summary>Makes the current pose the front and reference (tracking detailed design 8.5).</summary>
        void Recenter();
    }

    /// <summary>
    /// An input method that produces raw observations in the common form (tracking detailed design 5.2).
    /// A new input such as mocopi is added by implementing this contract.
    /// </summary>
    public interface ITrackingProvider : IDisposable
    {
        TrackingRegions Supported { get; }

        IReadOnlyObservable<TrackingState> State { get; }

        /// <summary>Returns without waiting; the result comes through <see cref="State"/>.</summary>
        void Start(TrackingRegions regions);

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
