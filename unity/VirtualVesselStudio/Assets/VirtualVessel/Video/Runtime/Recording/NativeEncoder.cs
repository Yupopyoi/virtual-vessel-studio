using System;
using System.Runtime.InteropServices;
using VirtualVessel.UI.Recording;

namespace VirtualVessel.Video.Recording
{
    /// <summary>
    /// The stream bus to record (audio detailed design 5.3), as plain values the composition gives from the
    /// Audio module, so Video does not reference it. Default (no tap) records video only.
    /// </summary>
    public readonly struct RecordingAudio
    {
        public RecordingAudio(IntPtr context, IntPtr read, TimeSpan latency)
        {
            Context = context;
            Read = read;
            Latency = latency;
        }

        public IntPtr Context { get; }

        /// <summary>The tap's native read function (vv_encode_audio_read).</summary>
        public IntPtr Read { get; }

        /// <summary>How far the bus is behind the moment the sound was heard (audio detailed design 8.4).</summary>
        public TimeSpan Latency { get; }

        public bool IsValid => Context != IntPtr.Zero && Read != IntPtr.Zero;
    }

    /// <summary>What a recording is encoded with (recording detailed design 10).</summary>
    public readonly struct EncoderFormat
    {
        public EncoderFormat(int width, int height, int frameRate, int videoBitrate, RecordingCodec codec)
        {
            Width = width;
            Height = height;
            FrameRate = frameRate;
            VideoBitrate = videoBitrate;
            Codec = codec;
            AudioBitrate = 0;
            Audio = default;
        }

        private EncoderFormat(EncoderFormat format, RecordingAudio audio, int audioBitrate)
            : this(format.Width, format.Height, format.FrameRate, format.VideoBitrate, format.Codec)
        {
            Audio = audio;
            AudioBitrate = audioBitrate;
        }

        public RecordingCodec Codec { get; }

        /// <summary>The stream bus to record; without a tap the file is video only.</summary>
        public RecordingAudio Audio { get; }

        /// <summary>AAC in bits per second.</summary>
        public int AudioBitrate { get; }

        public EncoderFormat WithAudio(RecordingAudio audio, int audioBitrate)
        {
            return new EncoderFormat(this, audio, audioBitrate);
        }

        public int Width { get; }

        public int Height { get; }

        public int FrameRate { get; }

        public int VideoBitrate { get; }

        /// <summary>The bytes of one BGRA frame.</summary>
        public int FrameBytes => Width * Height * 4;
    }

    /// <summary>The encoder's counts (vv_encode_stats).</summary>
    public readonly struct EncoderStatus
    {
        public EncoderStatus(long pushed, long written, long dropped, int lastError, bool hardware, long audioFrames = 0, double avOffsetMs = 0)
        {
            Pushed = pushed;
            Written = written;
            Dropped = dropped;
            LastError = lastError;
            Hardware = hardware;
            AudioFrames = audioFrames;
            AvOffsetMs = avOffsetMs;
        }

        /// <summary>Audio frames written, at 48 kHz.</summary>
        public long AudioFrames { get; }

        /// <summary>Where the audio ends against the video at the last write, in ms; positive is ahead.</summary>
        public double AvOffsetMs { get; }

        public long Pushed { get; }

        public long Written { get; }

        /// <summary>Frames with no free slot.</summary>
        public long Dropped { get; }

        /// <summary>The last failing HRESULT, or 0.</summary>
        public int LastError { get; }

        /// <summary>Whether a hardware encoder is in use; known once frames are written.</summary>
        public bool Hardware { get; }

        public bool Failed => LastError != 0;
    }

    /// <summary>
    /// A video file being encoded. Frames are pushed from the main thread; <see cref="Close"/> may run on
    /// another thread once no more frames are pushed.
    /// </summary>
    public interface IVideoEncoder
    {
        /// <summary>Copies a BGRA frame taken at <paramref name="timestamp"/> (the shared clock); false when it was dropped.</summary>
        bool Push(IntPtr bgra, int length, long timestamp);

        EncoderStatus GetStatus();

        /// <summary>Encodes what is waiting and finishes the file; blocks for up to about a second. Returns the last counts.</summary>
        EncoderStatus Close();
    }

    /// <summary>The encoder could not open (recording detailed design 12).</summary>
    public sealed class EncoderException : Exception
    {
        public EncoderException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// <c>VirtualVessel.Encode.Native</c>: H.264 in MP4 through Media Foundation, with a hardware encoder
    /// when there is one (native/encode/include/vv_encode.h). Timestamps are QPC values, which the
    /// application's monotonic clock (Stopwatch) reads on Windows.
    /// </summary>
    public sealed class NativeEncoder : IVideoEncoder
    {
        private const string Library = "VirtualVessel.Encode.Native";
        private const int ApiVersion = 3;
        private const int Ok = 0;
        private const int CodecH264 = 0;
        private const int CodecAv1 = 1;

        private IntPtr _handle;
        private EncoderStatus _last;

        private NativeEncoder(IntPtr handle)
        {
            _handle = handle;
        }

        /// <exception cref="EncoderException">The plugin is missing or of another version, or the file or encoder cannot open.</exception>
        public static NativeEncoder Open(string path, EncoderFormat format)
        {
            int version;
            try
            {
                version = vv_encode_api_version();
            }
            catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException)
            {
                throw new EncoderException($"The encoder plugin {Library} is not installed; build it with tools/native/build-encode.ps1.", exception);
            }

            if (version != ApiVersion)
            {
                throw new EncoderException($"The encoder plugin has API version {version}; version {ApiVersion} is needed.");
            }

            var config = new Config
            {
                Path = path,
                Width = format.Width,
                Height = format.Height,
                Fps = format.FrameRate,
                Bitrate = format.VideoBitrate,
                Codec = CodeOf(format.Codec),
                AudioBitrate = format.AudioBitrate,
                AudioRead = format.Audio.IsValid ? format.Audio.Read : IntPtr.Zero,
                AudioContext = format.Audio.IsValid ? format.Audio.Context : IntPtr.Zero,
            };
            int result = vv_encode_open(ref config, out IntPtr handle);
            if (result != Ok || handle == IntPtr.Zero)
            {
                throw new EncoderException($"The encoder did not open ({result}) for {format.Codec} {format.Width}x{format.Height} at {format.FrameRate} fps.");
            }

            return new NativeEncoder(handle);
        }

        /// <summary>Whether the system has a hardware encoder for <paramref name="codec"/>; false without the plugin.</summary>
        public static bool HardwareAvailable(RecordingCodec codec)
        {
            try
            {
                return vv_encode_api_version() == ApiVersion && vv_encode_hardware_available(CodeOf(codec)) != 0;
            }
            catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException)
            {
                return false;
            }
        }

        private static int CodeOf(RecordingCodec codec)
        {
            return codec == RecordingCodec.Av1 ? CodecAv1 : CodecH264;
        }

        public bool Push(IntPtr bgra, int length, long timestamp)
        {
            return _handle != IntPtr.Zero && vv_encode_push_video(_handle, bgra, length, timestamp) == Ok;
        }

        public EncoderStatus GetStatus()
        {
            if (_handle == IntPtr.Zero)
            {
                return _last;
            }

            vv_encode_get_stats(_handle, out Stats stats);
            _last = new EncoderStatus(stats.Pushed, stats.Written, stats.Dropped, stats.LastError, stats.Hardware != 0, stats.AudioFrames, stats.AvOffsetUs / 1000.0);
            return _last;
        }

        public EncoderStatus Close()
        {
            if (_handle == IntPtr.Zero)
            {
                return _last;
            }

            EncoderStatus before = GetStatus();
            int result = vv_encode_close(_handle);
            _handle = IntPtr.Zero;

            // The counts were read before the drain; a failure in it shows only in the result.
            _last = result == Ok ? before : new EncoderStatus(before.Pushed, before.Written, before.Dropped, before.LastError != 0 ? before.LastError : result, before.Hardware, before.AudioFrames, before.AvOffsetMs);
            return _last;
        }

        // vv_encode_config.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Config
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string Path;
            public int Width;
            public int Height;
            public int Fps;
            public int Bitrate;
            public int Codec;
            public int AudioBitrate;
            public IntPtr AudioRead;
            public IntPtr AudioContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Stats
        {
            public long Pushed;
            public long Written;
            public long Dropped;
            public long LastWriteUs;
            public long MaxWriteUs;
            public long TotalWriteUs;
            public int LastError;
            public int Hardware;
            public long AudioFrames;
            public long AvOffsetUs;
        }

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_encode_api_version();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_encode_hardware_available(int codec);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_encode_open(ref Config config, out IntPtr encoder);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_encode_push_video(IntPtr encoder, IntPtr bgra, int length, long qpc);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_encode_get_stats(IntPtr encoder, out Stats stats);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_encode_close(IntPtr encoder);
    }
}
