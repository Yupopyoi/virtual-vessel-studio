using System;
using System.Globalization;
using System.IO;
using VirtualVessel.UI.Recording;

namespace VirtualVessel.Video.Recording
{
    /// <summary>File names, folders, free space, and quality of recordings (recording detailed design 8.2, 10). Pure.</summary>
    public static class RecordingNames
    {
        public const string Prefix = "VirtualVesselStudio_";
        public const string Extension = ".mp4";

        /// <summary>The folder under the user's Videos folder that recordings go to by default.</summary>
        public const string DefaultFolderName = "Virtual Vessel Studio";

        /// <summary>Free space needed to start: about 45 minutes at the default bitrates.</summary>
        public const long StartReserveBytes = 2L * 1024 * 1024 * 1024;

        /// <summary>Below this a recording stops, so the disk is not filled to the last byte.</summary>
        public const long StopReserveBytes = 500L * 1024 * 1024;

        /// <summary>"VirtualVesselStudio_2026-10-27_22-30-05.mp4" from the local time.</summary>
        public static string FileName(DateTime localTime)
        {
            return Prefix + localTime.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + Extension;
        }

        /// <summary>
        /// The full path of a new recording in <paramref name="folder"/>: the time's name, or with "_2",
        /// "_3" when a file of that name is there, so a recording never replaces another.
        /// </summary>
        public static string NewFile(string folder, DateTime localTime, Func<string, bool> exists)
        {
            string name = FileName(localTime);
            string path = Path.Combine(folder, name);
            string stem = Path.GetFileNameWithoutExtension(name);
            for (int number = 2; exists(path); number++)
            {
                path = Path.Combine(folder, stem + "_" + number.ToString(CultureInfo.InvariantCulture) + Extension);
            }

            return path;
        }

        /// <summary>The folder recordings go to: the setting's, or Videos/Virtual Vessel Studio.</summary>
        public static string Folder(string setting, string videosFolder)
        {
            return string.IsNullOrWhiteSpace(setting) ? DefaultFolder(videosFolder) : setting;
        }

        public static string DefaultFolder(string videosFolder)
        {
            return Path.Combine(videosFolder, DefaultFolderName);
        }

        /// <summary>Whether a recording may start. Unknown free space does not stop it: writing will tell.</summary>
        public static bool HasRoom(long? freeBytes)
        {
            return freeBytes == null || freeBytes.Value >= StartReserveBytes;
        }

        /// <summary>Whether a recording in progress must stop for lack of space.</summary>
        public static bool MustStop(long? freeBytes)
        {
            return freeBytes != null && freeBytes.Value < StopReserveBytes;
        }

        /// <summary>The quality the bitrate is closest to; a bitrate edited in the file reads as one of the two.</summary>
        public static RecordingQuality QualityOf(int videoBitrate)
        {
            return videoBitrate >= (RecordingSettings.StandardBitrate + RecordingSettings.HighBitrate) / 2 ? RecordingQuality.High : RecordingQuality.Standard;
        }

        public static int BitrateOf(RecordingQuality quality)
        {
            return quality == RecordingQuality.High ? RecordingSettings.HighBitrate : RecordingSettings.StandardBitrate;
        }
    }
}
