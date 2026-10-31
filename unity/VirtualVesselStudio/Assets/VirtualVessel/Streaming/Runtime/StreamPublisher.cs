using System;
using System.Runtime.InteropServices;
using System.Text;
using VirtualVessel.UI.Streaming;
using VirtualVessel.Video.Recording;

namespace VirtualVessel.Streaming
{
    /// <summary>What a stream is sent with.</summary>
    public readonly struct StreamFormat
    {
        public StreamFormat(string url, int width, int height, int frameRate, StreamingCodec codec, int videoBitrate, int audioBitrate, RecordingAudio audio)
        {
            Url = url;
            Width = width;
            Height = height;
            FrameRate = frameRate;
            Codec = codec;
            VideoBitrate = videoBitrate;
            AudioBitrate = audioBitrate;
            Audio = audio;
        }

        public string Url { get; }

        public int Width { get; }

        public int Height { get; }

        public int FrameRate { get; }

        public StreamingCodec Codec { get; }

        public int VideoBitrate { get; }

        public int AudioBitrate { get; }

        public RecordingAudio Audio { get; }
    }

    /// <summary>The plugin's state, as the plugin gives it.</summary>
    public enum PublisherState
    {
        Connecting = 0,
        Live = 1,
        Reconnecting = 2,
        Failed = 3,
        Closed = 4,
    }

    /// <summary>The plugin's problem codes (VV_STREAM_PROBLEM_*).</summary>
    public enum PublisherProblem
    {
        None = 0,
        Unreachable = 1,
        Rejected = 2,
        ConnectionLost = 3,
        EncoderFailed = 4,
    }

    public readonly struct PublisherStatus
    {
        public PublisherStatus(PublisherState state, PublisherProblem problem, bool hardware, int reconnects, long bytesSent, long queueMs, long framesPushed, long framesSent, long framesDropped, long encodeMicroseconds, double avOffsetMs, long framesDroppedFromQueue = 0)
        {
            State = state;
            Problem = problem;
            Hardware = hardware;
            Reconnects = reconnects;
            BytesSent = bytesSent;
            QueueMs = queueMs;
            FramesPushed = framesPushed;
            FramesSent = framesSent;
            FramesDropped = framesDropped;
            EncodeMicroseconds = encodeMicroseconds;
            AvOffsetMs = avOffsetMs;
            FramesDroppedFromQueue = framesDroppedFromQueue;
        }

        public PublisherState State { get; }

        public PublisherProblem Problem { get; }

        public bool Hardware { get; }

        public int Reconnects { get; }

        public long BytesSent { get; }

        public long QueueMs { get; }

        public long FramesPushed { get; }

        public long FramesSent { get; }

        public long FramesDropped { get; }

        public long EncodeMicroseconds { get; }

        public double AvOffsetMs { get; }

        /// <summary>Of <see cref="FramesDropped"/>, those lost because the connection fell behind; the rest, because the encoder did.</summary>
        public long FramesDroppedFromQueue { get; }
    }

    /// <summary>Encodes pushed NV12 frames and sends them, on threads of its own.</summary>
    public interface IStreamPublisher : IFrameSink
    {
        PublisherStatus GetStatus();

        /// <summary>Sends what is queued and disconnects; blocks for up to about two seconds.</summary>
        void Close();
    }

    /// <summary>The plugin did not open (streaming detailed design 12). The message never holds the key or the URL's path.</summary>
    public sealed class StreamPublisherException : Exception
    {
        public StreamPublisherException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>Wraps VirtualVessel.Stream.Native (vv_stream.h).</summary>
    public sealed class NativeStreamPublisher : IStreamPublisher
    {
        private const string Library = "VirtualVessel.Stream.Native";
        private const int ApiVersion = 2;
        private const int Ok = 0;
        private const int CodecH264 = 0;
        private const int CodecAv1 = 1;
        private const int KeyframeSeconds = 2;

        private IntPtr _handle;

        private NativeStreamPublisher(IntPtr handle)
        {
            _handle = handle;
        }

        /// <summary>Starts connecting and encoding; does not wait for the connection.</summary>
        public static NativeStreamPublisher Open(StreamFormat format, string key)
        {
            CheckVersion();
            byte[] url = Utf8(format.Url);
            byte[] keyBytes = Utf8(key);
            GCHandle urlPin = GCHandle.Alloc(url, GCHandleType.Pinned);
            GCHandle keyPin = GCHandle.Alloc(keyBytes, GCHandleType.Pinned);
            try
            {
                var config = new Config
                {
                    Url = urlPin.AddrOfPinnedObject(),
                    Key = keyPin.AddrOfPinnedObject(),
                    Width = format.Width,
                    Height = format.Height,
                    Fps = format.FrameRate,
                    Codec = format.Codec == StreamingCodec.Av1 ? CodecAv1 : CodecH264,
                    VideoBitrate = format.VideoBitrate,
                    AudioBitrate = format.AudioBitrate,
                    KeyframeSeconds = KeyframeSeconds,
                    AudioRead = format.Audio.Read,
                    AudioContext = format.Audio.Context,
                };

                int result = vv_stream_open(ref config, out IntPtr handle);
                if (result != Ok || handle == IntPtr.Zero)
                {
                    throw new StreamPublisherException($"The streaming plugin did not open ({result}) for {format.Codec} {format.Width}x{format.Height} at {format.FrameRate} fps.");
                }

                return new NativeStreamPublisher(handle);
            }
            finally
            {
                // The plugin has its own copy; this one does not stay in memory.
                Array.Clear(keyBytes, 0, keyBytes.Length);
                keyPin.Free();
                urlPin.Free();
            }
        }

        /// <summary>Connects as far as RTMP connect, without publishing. Blocks for up to the timeout.</summary>
        public static PublisherProblem TestConnection(string url, int timeoutMs)
        {
            CheckVersion();
            byte[] bytes = Utf8(url);
            GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                return (PublisherProblem)vv_stream_test_connection(pin.AddrOfPinnedObject(), timeoutMs);
            }
            finally
            {
                pin.Free();
            }
        }

        public bool Push(IntPtr data, int length, long timestamp)
        {
            return _handle != IntPtr.Zero && vv_stream_push_video(_handle, data, length, timestamp) == Ok;
        }

        public PublisherStatus GetStatus()
        {
            if (_handle == IntPtr.Zero)
            {
                return default;
            }

            vv_stream_get_stats(_handle, out Stats stats);
            return new PublisherStatus(
                (PublisherState)stats.State,
                (PublisherProblem)stats.Problem,
                stats.Hardware != 0,
                stats.Reconnects,
                stats.BytesSent,
                stats.QueueMs,
                stats.FramesPushed,
                stats.FramesSent,
                stats.FramesDropped,
                stats.EncodeUsTotal,
                stats.AvOffsetUs / 1000.0,
                stats.FramesDroppedQueue);
        }

        public void Close()
        {
            IntPtr handle = _handle;
            _handle = IntPtr.Zero;
            if (handle != IntPtr.Zero)
            {
                vv_stream_close(handle);
            }
        }

        private static void CheckVersion()
        {
            int version;
            try
            {
                version = vv_stream_api_version();
            }
            catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException)
            {
                throw new StreamPublisherException("The streaming plugin is missing.", exception);
            }

            if (version != ApiVersion)
            {
                throw new StreamPublisherException($"The streaming plugin has API version {version}; version {ApiVersion} is needed.");
            }
        }

        private static byte[] Utf8(string text)
        {
            byte[] bytes = new byte[Encoding.UTF8.GetByteCount(text) + 1];
            Encoding.UTF8.GetBytes(text, 0, text.Length, bytes, 0);
            return bytes;
        }

        // vv_stream_config.
        [StructLayout(LayoutKind.Sequential)]
        private struct Config
        {
            public IntPtr Url;
            public IntPtr Key;
            public int Width;
            public int Height;
            public int Fps;
            public int Codec;
            public int VideoBitrate;
            public int AudioBitrate;
            public int KeyframeSeconds;
            public IntPtr AudioRead;
            public IntPtr AudioContext;
        }

        // vv_stream_stats.
        [StructLayout(LayoutKind.Sequential)]
        private struct Stats
        {
            public int State;
            public int Problem;
            public int Hardware;
            public int Reconnects;
            public long BytesSent;
            public long QueueMs;
            public long FramesPushed;
            public long FramesSent;
            public long FramesDropped;
            public long EncodeUsTotal;
            public long AvOffsetUs;
            public long FramesDroppedQueue;
        }

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_stream_api_version();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_stream_open(ref Config config, out IntPtr stream);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_stream_push_video(IntPtr stream, IntPtr nv12, int length, long qpc);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_stream_get_stats(IntPtr stream, out Stats stats);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_stream_close(IntPtr stream);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_stream_test_connection(IntPtr url, int timeoutMs);
    }
}
