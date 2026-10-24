using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.State;

namespace VirtualVessel.Tracking.Recording
{
    /// <summary>What the recordings are doing now.</summary>
    public enum RecordingActivity
    {
        Idle,
        Recording,
        Replaying,
    }

    /// <summary>
    /// Recording and replaying observations, for developers (tracking detailed design 5.5, 12): used by
    /// the Diagnostics screen, shown only in Developer Mode. Main thread only.
    /// </summary>
    public interface ITrackingRecordings
    {
        IReadOnlyObservable<RecordingActivity> Activity { get; }

        /// <summary>The recordings in the folder, newest first; refreshed by <see cref="RefreshAsync"/> and after each change.</summary>
        IReadOnlyObservable<IReadOnlyList<TrackingRecordingInfo>> Recordings { get; }

        /// <summary>The recording being recorded or replayed; null while idle.</summary>
        string ActiveId { get; }

        /// <summary>How long the recording in progress is, in seconds.</summary>
        double RecordedSeconds { get; }

        Task RefreshAsync(CancellationToken cancellationToken);

        /// <summary>Starts recording raw observations. Throws <see cref="InvalidOperationException"/> while tracking is not running or during replay.</summary>
        void StartRecording(string description);

        void StopRecording();

        /// <summary>Replays the recording in a loop instead of the camera, starting tracking for it if needed.</summary>
        Task StartReplayAsync(string id, CancellationToken cancellationToken);

        void StopReplay();

        /// <summary>Deletes one recording. Throws <see cref="InvalidOperationException"/> for the one being recorded or replayed.</summary>
        Task DeleteAsync(string id, CancellationToken cancellationToken);

        /// <summary>Deletes every recording that is not being recorded or replayed.</summary>
        Task DeleteAllAsync(CancellationToken cancellationToken);

        /// <summary>Stops recording and replay, as when Developer Mode is turned off.</summary>
        void StopAll();
    }

    /// <summary>One recording as the list shows it (tracking detailed design 12.3).</summary>
    public sealed class TrackingRecordingInfo
    {
        public TrackingRecordingInfo(string id, string description, DateTime startedAtUtc, double seconds, long bytes, int frames, bool readable)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Description = description ?? string.Empty;
            StartedAtUtc = startedAtUtc;
            Seconds = seconds;
            Bytes = bytes;
            Frames = frames;
            Readable = readable;
        }

        /// <summary>The file name; never a path.</summary>
        public string Id { get; }

        public string Description { get; }

        public DateTime StartedAtUtc { get; }

        public double Seconds { get; }

        public long Bytes { get; }

        public int Frames { get; }

        /// <summary>False for a file of an unknown version or with a broken header; it can still be deleted.</summary>
        public bool Readable { get; }

        public static TrackingRecordingInfo Unreadable(string id, long bytes)
        {
            return new TrackingRecordingInfo(id, string.Empty, default, 0, bytes, 0, readable: false);
        }
    }
}
