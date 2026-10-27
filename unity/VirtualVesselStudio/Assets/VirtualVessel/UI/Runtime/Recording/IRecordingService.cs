using System;
using VirtualVessel.Core.State;

namespace VirtualVessel.UI.Recording
{
    /// <summary>Where a recording is (recording detailed design 6).</summary>
    public enum RecordingState
    {
        Idle,
        Starting,
        Recording,
        Stopping,
        Failed,
    }

    /// <summary>Why a recording could not start or stopped early, kept until the next start (recording detailed design 6).</summary>
    public enum RecordingProblem
    {
        None,

        /// <summary>The folder cannot be created or written.</summary>
        FolderUnavailable,

        /// <summary>Less free space than a recording needs.</summary>
        DiskFull,

        /// <summary>The encoder could not open, or failed while writing.</summary>
        EncoderFailed,

        /// <summary>Recording goes on without audio.</summary>
        AudioUnavailable,
    }

    /// <summary>The video bitrate as the user chooses it (recording detailed design 18.2).</summary>
    public enum RecordingQuality
    {
        /// <summary>6 Mbit/s.</summary>
        Standard,

        /// <summary>12 Mbit/s.</summary>
        High,
    }

    /// <summary>What the user sets for recordings, and the video's fixed size and rate.</summary>
    public readonly struct RecordingOptions : IEquatable<RecordingOptions>
    {
        public RecordingOptions(string folder, RecordingQuality quality, int width, int height, int frameRate)
        {
            Folder = folder;
            Quality = quality;
            Width = width;
            Height = height;
            FrameRate = frameRate;
        }

        /// <summary>The folder recordings go to, as a full path.</summary>
        public string Folder { get; }

        public RecordingQuality Quality { get; }

        public int Width { get; }

        public int Height { get; }

        public int FrameRate { get; }

        public RecordingOptions WithFolder(string folder)
        {
            return new RecordingOptions(folder, Quality, Width, Height, FrameRate);
        }

        public RecordingOptions WithQuality(RecordingQuality quality)
        {
            return new RecordingOptions(Folder, quality, Width, Height, FrameRate);
        }

        public bool Equals(RecordingOptions other)
        {
            return string.Equals(Folder, other.Folder, StringComparison.Ordinal) && Quality == other.Quality && Width == other.Width && Height == other.Height && FrameRate == other.FrameRate;
        }

        public override bool Equals(object obj)
        {
            return obj is RecordingOptions other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Folder, Quality, Width, Height, FrameRate);
        }
    }

    /// <summary>
    /// Recording the stream video to a file (recording detailed design 5). The Video module implements
    /// it; the live screen starts and stops it, and the setup screen sets its options.
    /// </summary>
    public interface IRecordingService
    {
        IReadOnlyObservable<RecordingState> State { get; }

        IReadOnlyObservable<RecordingProblem> Problem { get; }

        /// <summary>How long the recording in progress has run; zero otherwise.</summary>
        TimeSpan Elapsed { get; }

        /// <summary>The last finished file, for "Open folder"; null before the first.</summary>
        string LastFile { get; }

        IReadOnlyObservable<RecordingOptions> Options { get; }

        /// <summary>Changes the folder or the quality. A recording in progress keeps its own; the next one uses these.</summary>
        void SetOptions(RecordingOptions options);

        /// <summary>The free space where recordings go, in bytes; null when it cannot be read.</summary>
        long? FreeBytes();

        /// <summary>Starts a recording; ignored while one is starting, running, or stopping.</summary>
        void Start();

        /// <summary>Stops the recording and finishes its file; ignored when none is running.</summary>
        void Stop();
    }
}
