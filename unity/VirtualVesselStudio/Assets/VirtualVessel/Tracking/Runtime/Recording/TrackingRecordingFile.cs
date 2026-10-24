using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace VirtualVessel.Tracking.Recording
{
    /// <summary>
    /// Recording files in the recordings folder (tracking detailed design 12.3): their names, what the
    /// list shows, and their observations for replay. File access lives here; naming is pure.
    /// </summary>
    public static class TrackingRecordingFile
    {
        private const string NameTimeFormat = "yyyyMMdd-HHmmss";

        private static readonly Regex s_name = new Regex(@"^\d{8}-\d{6}(-\d{1,4})?\.vvtrack$", RegexOptions.CultureInvariant);

        /// <summary>Whether <paramref name="name"/> is a recording's file name as this application makes them, and nothing else. Pure.</summary>
        public static bool IsRecordingName(string name)
        {
            return name != null && s_name.IsMatch(name);
        }

        /// <summary>
        /// The file name for a recording started at <paramref name="startedAtUtc"/>: the UTC time, with
        /// "-2", "-3", and so on when <paramref name="exists"/> says the name is taken. Pure apart from
        /// asking <paramref name="exists"/>.
        /// </summary>
        public static string NameFor(DateTime startedAtUtc, Func<string, bool> exists)
        {
            string stem = startedAtUtc.ToString(NameTimeFormat, CultureInfo.InvariantCulture);
            string name = stem + TrackingRecordingFormat.Extension;
            for (int n = 2; exists(name); n++)
            {
                name = $"{stem}-{n}{TrackingRecordingFormat.Extension}";
            }

            return name;
        }

        /// <summary>
        /// What the list shows for the file at <paramref name="path"/>: read from the header and the first
        /// and last observations only. A file this version cannot read is listed as such, not dropped
        /// (CLAUDE.md 25).
        /// </summary>
        public static TrackingRecordingInfo ReadInfo(string path)
        {
            var file = new FileInfo(path);
            string id = file.Name;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (!TryReadHeader(stream, out TrackingRecordingHeader header, out int headerLength))
                {
                    return TrackingRecordingInfo.Unreadable(id, file.Length);
                }

                int count = TrackingRecordingFormat.FrameCount(stream.Length, headerLength);
                double seconds = 0;
                if (count > 1)
                {
                    seconds = ReadTimeAt(stream, headerLength, count - 1) - ReadTimeAt(stream, headerLength, 0);
                }

                return new TrackingRecordingInfo(id, header.Description, header.StartedAtUtc, Math.Max(0, seconds), stream.Length, count, readable: true);
            }
            catch (IOException)
            {
                return TrackingRecordingInfo.Unreadable(id, file.Exists ? file.Length : 0);
            }
            catch (UnauthorizedAccessException)
            {
                return TrackingRecordingInfo.Unreadable(id, 0);
            }
        }

        /// <summary>
        /// The header and every whole observation of the file, for replay; a cut-off last observation is
        /// left out. Throws <see cref="InvalidDataException"/> for a file this version cannot read.
        /// </summary>
        public static (TrackingRecordingHeader Header, List<TrackingFrame> Frames) ReadAll(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (!TryReadHeader(stream, out TrackingRecordingHeader header, out int headerLength))
            {
                throw new InvalidDataException($"{Path.GetFileName(path)} is not a tracking recording this version can read.");
            }

            int count = TrackingRecordingFormat.FrameCount(stream.Length, headerLength);
            var frames = new List<TrackingFrame>(count);
            var buffer = new byte[TrackingRecordingFormat.FrameSize];
            stream.Position = headerLength;
            for (int i = 0; i < count; i++)
            {
                ReadExactly(stream, buffer);
                var frame = new TrackingFrame();
                TrackingRecordingFormat.DecodeFrame(buffer, frame);
                frames.Add(frame);
            }

            return (header, frames);
        }

        private static bool TryReadHeader(Stream stream, out TrackingRecordingHeader header, out int headerLength)
        {
            header = default;
            headerLength = 0;
            // Up to the longest fixed header; an older version's is shorter, and the description follows it.
            var fixedPart = new byte[(int)Math.Min(stream.Length, TrackingRecordingFormat.FixedHeaderSize)];
            ReadExactly(stream, fixedPart);
            if (!TrackingRecordingFormat.TryDecodeFixedHeader(fixedPart, out int version, out int frameSize, out DateTime startedAt, out TrackingRegions supported, out ObservationTraits traits, out int descriptionBytes, out int fixedLength)
                || stream.Length < fixedLength + descriptionBytes)
            {
                return false;
            }

            var description = new byte[descriptionBytes];
            stream.Position = fixedLength;
            ReadExactly(stream, description);
            header = new TrackingRecordingHeader(version, frameSize, startedAt, supported, traits, Encoding.UTF8.GetString(description));
            headerLength = fixedLength + descriptionBytes;
            return true;
        }

        private static double ReadTimeAt(Stream stream, int headerLength, int index)
        {
            var bytes = new byte[TrackingRecordingFormat.TimeOffset + 8];
            stream.Position = headerLength + ((long)index * TrackingRecordingFormat.FrameSize);
            ReadExactly(stream, bytes);
            return TrackingRecordingFormat.ReadTime(bytes);
        }

        private static void ReadExactly(Stream stream, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n == 0)
                {
                    throw new EndOfStreamException();
                }

                read += n;
            }
        }
    }
}
