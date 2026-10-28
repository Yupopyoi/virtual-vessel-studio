using System;
using System.Runtime.InteropServices;
using UnityEngine;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.UI.Recording;

namespace VirtualVessel.Video.Recording
{
    /// <summary>
    /// What a recording needs from outside the service: the encoder, the frame reader, the disk, and the
    /// local time. <see cref="Native"/> gives the real ones; tests give fakes, so the service's states can
    /// be checked without a GPU or the plugin.
    /// </summary>
    public sealed class RecordingEnvironment
    {
        public RecordingEnvironment(
            Func<string, EncoderFormat, IVideoEncoder> openEncoder,
            Func<EncoderFormat, IVideoEncoder, IStreamFrameReader> readFrames,
            Func<string, long?> freeBytes,
            Func<DateTime> localNow,
            string videosFolder,
            bool av1Available)
        {
            OpenEncoder = openEncoder ?? throw new ArgumentNullException(nameof(openEncoder));
            ReadFrames = readFrames ?? throw new ArgumentNullException(nameof(readFrames));
            FreeBytes = freeBytes ?? throw new ArgumentNullException(nameof(freeBytes));
            LocalNow = localNow ?? throw new ArgumentNullException(nameof(localNow));
            VideosFolder = videosFolder ?? throw new ArgumentNullException(nameof(videosFolder));
            Av1Available = av1Available;
        }

        /// <summary>Opens an encoder for the path; runs on a worker thread and throws <see cref="EncoderException"/>.</summary>
        public Func<string, EncoderFormat, IVideoEncoder> OpenEncoder { get; }

        public Func<EncoderFormat, IVideoEncoder, IStreamFrameReader> ReadFrames { get; }

        /// <summary>The free space of the disk a folder is on; null when it cannot be read.</summary>
        public Func<string, long?> FreeBytes { get; }

        public Func<DateTime> LocalNow { get; }

        /// <summary>The user's own Videos folder.</summary>
        public string VideosFolder { get; }

        /// <summary>Whether the GPU has an AV1 encoder, read once at startup.</summary>
        public bool Av1Available { get; }

        /// <summary>The native encoder reading the stream video back from the GPU.</summary>
        public static RecordingEnvironment Native(IReadOnlyObservable<Texture> video, IMonotonicClock clock)
        {
            return new RecordingEnvironment(
                NativeEncoder.Open,
                (format, encoder) => new StreamFrameReader(video, format, encoder, clock),
                DiskFreeBytes,
                () => DateTime.Now,
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                NativeEncoder.HardwareAvailable(RecordingCodec.Av1));
        }

        /// <summary>The free space for the caller on the disk of <paramref name="folder"/>; works for network folders too.</summary>
        public static long? DiskFreeBytes(string folder)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (!GetDiskFreeSpaceExW(folder, out ulong available, out _, out _))
            {
                return null;
            }

            return available > long.MaxValue ? long.MaxValue : (long)available;
#else
            return null;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDiskFreeSpaceExW(string directory, out ulong freeBytesAvailable, out ulong totalBytes, out ulong totalFreeBytes);
#endif
    }
}
