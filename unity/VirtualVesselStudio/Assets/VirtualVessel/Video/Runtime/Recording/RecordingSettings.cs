using System;
using System.Collections.Generic;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.Video.Recording
{
    /// <summary>
    /// The project's streaming profile (recording detailed design 10): the encoding values, shared with
    /// streaming later, and where recordings go.
    /// </summary>
    public sealed class RecordingSettings
    {
        public const int SchemaVersion = 1;

        /// <summary>The name when the file is not a project's profile, such as in tests.</summary>
        public const string FileName = "streaming-settings.json";

        public const int StandardBitrate = 6_000_000;
        public const int HighBitrate = 12_000_000;

        public int Width { get; set; } = 1920;

        public int Height { get; set; } = 1080;

        /// <summary>The output rate; the stream renders faster, and every second frame is encoded at 60 fps.</summary>
        public int FrameRate { get; set; } = 30;

        public int VideoBitrate { get; set; } = StandardBitrate;

        /// <summary>For the recording with audio, which comes with the Audio module.</summary>
        public int AudioBitrate { get; set; } = 160_000;

        /// <summary>A full path, or null for the user's Videos folder (<see cref="RecordingNames.DefaultFolder"/>).</summary>
        public string RecordingFolder { get; set; }

        /// <summary>
        /// "av1" or "h264" once the user has chosen; null for AV1 where the GPU has an AV1 encoder and H.264
        /// elsewhere (<see cref="RecordingNames.CodecOf"/>). Added in version 1 without a step: a file without it reads as null.
        /// </summary>
        public string VideoCodec { get; set; }

        public static IReadOnlyList<ConfigMigration> Migrations { get; } = Array.Empty<ConfigMigration>();

        public RecordingSettings Clone()
        {
            return (RecordingSettings)MemberwiseClone();
        }

        /// <summary>
        /// A copy with every value the encoder can use: one edited out of range in the file falls back to
        /// its default, so a typing mistake cannot stop the stream video from starting. Pure.
        /// </summary>
        public RecordingSettings Usable()
        {
            var defaults = new RecordingSettings();
            RecordingSettings next = Clone();
            if (Width < 320 || Width > 3840 || Height < 180 || Height > 2160 || Width % 2 != 0 || Height % 2 != 0)
            {
                next.Width = defaults.Width;
                next.Height = defaults.Height;
            }

            if (FrameRate < 1 || FrameRate > 60)
            {
                next.FrameRate = defaults.FrameRate;
            }

            if (VideoBitrate < 500_000 || VideoBitrate > 50_000_000)
            {
                next.VideoBitrate = defaults.VideoBitrate;
            }

            if (AudioBitrate < 64_000 || AudioBitrate > 320_000)
            {
                next.AudioBitrate = defaults.AudioBitrate;
            }

            return next;
        }
    }
}
