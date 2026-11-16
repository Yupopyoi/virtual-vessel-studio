using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace VirtualVessel.Audio
{
    /// <summary>
    /// Reads audio files with the audio plugin, which uses Windows' Media Foundation (audio detailed design 5.4):
    /// M4A (AAC), MP3, WAV, and others, so the application carries no codec of its own.
    /// </summary>
    public static class AudioFile
    {
        private const string Library = "VirtualVessel.Audio.Native";
        private const int Ok = 0;
        private const int OutOfMemory = -2;
        private const int TooLong = -6;

        /// <summary>The longest file read, in seconds (VV_AUDIO_MAX_FILE_SECONDS).</summary>
        public const int MaxSeconds = 600;

        /// <summary>The files offered in a file dialog; Windows can decode more.</summary>
        public static IReadOnlyList<string> Extensions { get; } = new[] { ".wav", ".m4a", ".mp3" };

        /// <summary>The file's samples mixed to mono, at the rate it was made. Call it off the main thread.</summary>
        /// <exception cref="InvalidDataException">The file is missing, cannot be decoded, or is too long.</exception>
        /// <exception cref="AudioEngineException">The audio plugin is missing or older.</exception>
        public static float[] ReadMono(string path, out int rate)
        {
            int result;
            IntPtr clip;
            long count;
            int error;
            try
            {
                result = vv_audio_decode_file(path, out clip, out rate, out count, out error);
            }
            catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException)
            {
                throw new AudioEngineException($"The audio plugin {Library} is missing or too old to read files; build it with tools/native/build-audio.ps1.", exception);
            }

            if (result != Ok)
            {
                throw new InvalidDataException(result switch
                {
                    TooLong => $"The file is longer than {MaxSeconds} s.",
                    OutOfMemory => "There is not enough memory to read the file.",
                    _ => $"Windows cannot read this file ({result}, HRESULT 0x{error:X8}).",
                });
            }

            try
            {
                var samples = new float[count];
                vv_audio_clip_read(clip, samples, count);
                return samples;
            }
            finally
            {
                vv_audio_clip_free(clip);
            }
        }

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int vv_audio_decode_file(string path, out IntPtr clip, out int rate, out long count, out int error);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vv_audio_clip_read(IntPtr clip, [Out] float[] samples, long capacity);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern void vv_audio_clip_free(IntPtr clip);
    }
}
