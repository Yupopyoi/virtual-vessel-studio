using System;
using System.Threading.Tasks;
using VirtualVessel.Core.State;

namespace VirtualVessel.UI.Streaming
{
    /// <summary>Where a stream is (streaming detailed design 7).</summary>
    public enum StreamingState
    {
        Idle,
        Connecting,
        Live,
        Reconnecting,
        Stopping,
        Failed,
    }

    /// <summary>Why a stream could not start, or what is wrong with it, kept until the next start (streaming detailed design 6).</summary>
    public enum StreamingProblem
    {
        None,

        /// <summary>No stream key is registered.</summary>
        NoStreamKey,

        /// <summary>Name resolution, the connection, or TLS failed, or the URL is not usable.</summary>
        ServerUnreachable,

        /// <summary>The service refused to publish, usually for a wrong key.</summary>
        Rejected,

        /// <summary>The connection was lost: reconnecting, or given up after ten minutes.</summary>
        ConnectionLost,

        /// <summary>The encoder could not open, or failed while streaming.</summary>
        EncoderFailed,

        /// <summary>The stream goes on without audio.</summary>
        AudioUnavailable,
    }

    public enum StreamDestinationKind
    {
        YouTube,
        CustomRtmp,
    }

    /// <summary>The video format of the stream (streaming detailed design 8.3).</summary>
    public enum StreamingCodec
    {
        Av1,
        H264,
    }

    /// <summary>What the user sets for the stream. The key's value is never here, only whether one is registered.</summary>
    public readonly struct StreamingOptions : IEquatable<StreamingOptions>
    {
        public StreamingOptions(StreamDestinationKind destination, string serverUrl, bool keyRegistered, StreamingCodec codec, bool av1Available, int videoBitrate)
        {
            Destination = destination;
            ServerUrl = serverUrl;
            KeyRegistered = keyRegistered;
            Codec = codec;
            Av1Available = av1Available;
            VideoBitrate = videoBitrate;
        }

        public StreamDestinationKind Destination { get; }

        /// <summary>The URL the stream goes to: the destination's default unless changed.</summary>
        public string ServerUrl { get; }

        public bool KeyRegistered { get; }

        /// <summary>The format the next stream uses.</summary>
        public StreamingCodec Codec { get; }

        /// <summary>Whether both the GPU and the destination can use AV1; otherwise it cannot be chosen.</summary>
        public bool Av1Available { get; }

        /// <summary>Bits per second.</summary>
        public int VideoBitrate { get; }

        /// <summary>Whether a stream can go to <paramref name="url"/>: rtmp:// or rtmps:// with a host. Pure.</summary>
        public static bool IsRtmpUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri parsed) && (parsed.Scheme == "rtmp" || parsed.Scheme == "rtmps") && parsed.Host.Length > 0;
        }

        /// <summary>With rtmp:// the key travels unencrypted, and the setup screen says so. Pure.</summary>
        public static bool IsUnencrypted(string url)
        {
            return url != null && url.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase);
        }

        public StreamingOptions WithDestination(StreamDestinationKind destination)
        {
            return new StreamingOptions(destination, ServerUrl, KeyRegistered, Codec, Av1Available, VideoBitrate);
        }

        public StreamingOptions WithServerUrl(string serverUrl)
        {
            return new StreamingOptions(Destination, serverUrl, KeyRegistered, Codec, Av1Available, VideoBitrate);
        }

        public StreamingOptions WithCodec(StreamingCodec codec)
        {
            return new StreamingOptions(Destination, ServerUrl, KeyRegistered, codec, Av1Available, VideoBitrate);
        }

        public StreamingOptions WithVideoBitrate(int videoBitrate)
        {
            return new StreamingOptions(Destination, ServerUrl, KeyRegistered, Codec, Av1Available, videoBitrate);
        }

        public bool Equals(StreamingOptions other)
        {
            return Destination == other.Destination && string.Equals(ServerUrl, other.ServerUrl, StringComparison.Ordinal) && KeyRegistered == other.KeyRegistered &&
                Codec == other.Codec && Av1Available == other.Av1Available && VideoBitrate == other.VideoBitrate;
        }

        public override bool Equals(object obj)
        {
            return obj is StreamingOptions other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Destination, ServerUrl, KeyRegistered, Codec, Av1Available, VideoBitrate);
        }
    }

    /// <summary>How the stream is going, for the live screen.</summary>
    public readonly struct StreamingStats
    {
        public StreamingStats(long bitsPerSecond, long queueMs, long droppedFrames, int reconnects)
        {
            BitsPerSecond = bitsPerSecond;
            QueueMs = queueMs;
            DroppedFrames = droppedFrames;
            Reconnects = reconnects;
        }

        /// <summary>The actual bitrate over the last few seconds.</summary>
        public long BitsPerSecond { get; }

        public long QueueMs { get; }

        public long DroppedFrames { get; }

        public int Reconnects { get; }
    }

    /// <summary>
    /// Streaming the stream video (streaming detailed design 5). The Streaming module implements it; the
    /// live screen starts and stops it, and the setup screen sets its options. No member returns the key.
    /// </summary>
    public interface IStreamingService
    {
        IReadOnlyObservable<StreamingState> State { get; }

        IReadOnlyObservable<StreamingProblem> Problem { get; }

        /// <summary>How long the stream has been live; zero otherwise.</summary>
        TimeSpan Elapsed { get; }

        IReadOnlyObservable<StreamingOptions> Options { get; }

        /// <summary>Ignored while streaming, when the settings cannot change.</summary>
        void SetOptions(StreamingOptions options);

        /// <summary>Registers the key in the operating system's credential store; empty removes it. False when that failed.</summary>
        bool SetStreamKey(string key);

        StreamingStats Stats { get; }

        /// <summary>Connects as far as RTMP connect, so no stream starts. <see cref="StreamingProblem.None"/> when it worked.</summary>
        Task<StreamingProblem> TestConnectionAsync();

        /// <summary>Starts the stream; ignored while one is running.</summary>
        void Start();

        /// <summary>Ends the stream; ignored when none is running.</summary>
        void Stop();
    }
}
