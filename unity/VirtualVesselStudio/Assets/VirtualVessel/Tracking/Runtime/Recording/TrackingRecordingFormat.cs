using System;
using System.Buffers.Binary;
using System.Text;
using UnityEngine;

namespace VirtualVessel.Tracking.Recording
{
    /// <summary>The start of a recording file (tracking detailed design 12.3).</summary>
    public readonly struct TrackingRecordingHeader
    {
        public TrackingRecordingHeader(int version, int frameSize, DateTime startedAtUtc, TrackingRegions supported, ObservationTraits traits, string description)
        {
            Version = version;
            FrameSize = frameSize;
            StartedAtUtc = startedAtUtc;
            Supported = supported;
            Traits = traits;
            Description = description ?? string.Empty;
        }

        public int Version { get; }

        /// <summary>Bytes per observation; every observation in a file has this size.</summary>
        public int FrameSize { get; }

        public DateTime StartedAtUtc { get; }

        public TrackingRegions Supported { get; }

        /// <summary>What kind of observation the recorded provider made, so a replay gets the same corrections.</summary>
        public ObservationTraits Traits { get; }

        public string Description { get; }
    }

    /// <summary>
    /// Turns recording headers and observations into bytes and back (tracking detailed design 12.3):
    /// little-endian, every observation the same size. Pure: each function reads only its arguments and
    /// writes only the caller's buffer, which is reused per observation (CLAUDE.md 15.1, 23).
    /// </summary>
    public static class TrackingRecordingFormat
    {
        public const string Extension = ".vvtrack";

        /// <summary>The version written. Version 2 added the provider's traits; version 1 still reads.</summary>
        public const int Version = 2;

        /// <summary>The longest description, in characters.</summary>
        public const int MaxDescriptionLength = 100;

        /// <summary>The longest description in bytes a reader accepts: a broken length must not make it read megabytes.</summary>
        public const int MaxDescriptionBytes = MaxDescriptionLength * 4;

        /// <summary>Identifier, version, frame size, start time, supported parts, traits, description length.</summary>
        public const int FixedHeaderSize = 4 + 4 + 4 + 8 + 4 + 4 + 4;

        /// <summary>Version 1's fixed header, without the traits.</summary>
        public const int FixedHeaderSizeVersion1 = FixedHeaderSize - 4;

        private const int PartHeaderSize = 1 + 4 + 8;
        private const int BodySize = PartHeaderSize + (BodyPose.JointCount * (12 + 8 + 4));
        private const int HandSize = PartHeaderSize + (HandPose.JointCount * (12 + 8)) + 16;
        private const int FaceSize = PartHeaderSize + 16 + 12 + (FacePose.ShapeCount * 4) + 8 + 8 + 8;

        /// <summary>Bytes per observation: sequence and time, the body, both hands, and the face.</summary>
        public const int FrameSize = 8 + 8 + BodySize + (2 * HandSize) + FaceSize;

        /// <summary>Where an observation's time lies within it, for reading only the first and last times.</summary>
        public const int TimeOffset = 8;

        private static readonly byte[] s_magic = { (byte)'V', (byte)'V', (byte)'T', (byte)'R' };

        /// <summary>
        /// The description as kept: trimmed, without control characters, at most
        /// <see cref="MaxDescriptionLength"/> characters. Pure.
        /// </summary>
        public static string NormalizeDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(description.Length);
            foreach (char c in description.Trim())
            {
                if (!char.IsControl(c))
                {
                    builder.Append(c);
                }
            }

            string text = builder.ToString();
            if (text.Length > MaxDescriptionLength)
            {
                // Do not cut a surrogate pair in half.
                int length = char.IsHighSurrogate(text[MaxDescriptionLength - 1]) ? MaxDescriptionLength - 1 : MaxDescriptionLength;
                text = text.Substring(0, length);
            }

            return text;
        }

        /// <summary>The header as bytes. Pure.</summary>
        public static byte[] EncodeHeader(TrackingRecordingHeader header)
        {
            byte[] description = Encoding.UTF8.GetBytes(header.Description);
            var bytes = new byte[FixedHeaderSize + description.Length];
            var span = bytes.AsSpan();
            s_magic.CopyTo(span);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), header.Version);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8), header.FrameSize);
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(12), header.StartedAtUtc.Ticks);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(20), (int)header.Supported);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(24), (int)header.Traits);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(28), description.Length);
            description.CopyTo(span.Slice(FixedHeaderSize));
            return bytes;
        }

        /// <summary>
        /// The fixed part of a header of version 1 or 2, from the file's first bytes: false when it is not
        /// one, of another version, or with a frame size or description length this version cannot read.
        /// <paramref name="fixedLength"/> is where the description starts. Version 1 had no traits; every
        /// version 1 recording was made with MediaPipe, so it reads as <see cref="ObservationTraits.MonocularDepth"/>. Pure.
        /// </summary>
        public static bool TryDecodeFixedHeader(ReadOnlySpan<byte> bytes, out int version, out int frameSize, out DateTime startedAtUtc, out TrackingRegions supported, out ObservationTraits traits, out int descriptionBytes, out int fixedLength)
        {
            version = 0;
            frameSize = 0;
            startedAtUtc = default;
            supported = TrackingRegions.None;
            traits = ObservationTraits.None;
            descriptionBytes = 0;
            fixedLength = 0;
            if (bytes.Length < 8 || !bytes.Slice(0, 4).SequenceEqual(s_magic))
            {
                return false;
            }

            version = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(4));
            fixedLength = version == 1 ? FixedHeaderSizeVersion1 : FixedHeaderSize;
            if ((version != 1 && version != Version) || bytes.Length < fixedLength)
            {
                return false;
            }

            frameSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8));
            long ticks = BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(12));
            supported = (TrackingRegions)BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(20));
            traits = version == 1 ? ObservationTraits.MonocularDepth : (ObservationTraits)BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(24));
            descriptionBytes = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(fixedLength - 4));
            if (frameSize != FrameSize || descriptionBytes < 0 || descriptionBytes > MaxDescriptionBytes
                || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            {
                return false;
            }

            startedAtUtc = new DateTime(ticks, DateTimeKind.Utc);
            return true;
        }

        /// <summary>Writes <paramref name="frame"/> into the first <see cref="FrameSize"/> bytes of <paramref name="into"/>. Pure apart from writing the caller's buffer.</summary>
        public static void EncodeFrame(TrackingFrame frame, Span<byte> into)
        {
            var writer = new Writer(into);
            writer.Long(frame.Sequence);
            writer.Double(frame.Time);

            BodyPose body = frame.Body;
            writer.Part(body);
            for (int i = 0; i < BodyPose.JointCount; i++)
            {
                writer.Vec3(body.Positions[i]);
                writer.Vec2(body.ImagePositions[i]);
                writer.Float(body.JointConfidence[i]);
            }

            writer.Hand(frame.LeftHand);
            writer.Hand(frame.RightHand);

            FacePose face = frame.Face;
            writer.Part(face);
            writer.Quat(face.HeadRotation);
            writer.Vec3(face.HeadPosition);
            for (int i = 0; i < FacePose.ShapeCount; i++)
            {
                writer.Float(face.Shapes[i]);
            }

            writer.Long((long)face.SupportedShapes);
            writer.Vec2(face.LeftEyeGaze);
            writer.Vec2(face.RightEyeGaze);
        }

        /// <summary>Reads an observation written by <see cref="EncodeFrame"/> into <paramref name="into"/>. Pure apart from writing the caller's object.</summary>
        public static void DecodeFrame(ReadOnlySpan<byte> bytes, TrackingFrame into)
        {
            var reader = new Reader(bytes);
            into.Sequence = reader.Long();
            into.Time = reader.Double();

            BodyPose body = into.Body;
            reader.Part(body);
            for (int i = 0; i < BodyPose.JointCount; i++)
            {
                body.Positions[i] = reader.Vec3();
                body.ImagePositions[i] = reader.Vec2();
                body.JointConfidence[i] = reader.Float();
            }

            reader.Hand(into.LeftHand);
            reader.Hand(into.RightHand);

            FacePose face = into.Face;
            reader.Part(face);
            face.HeadRotation = reader.Quat();
            face.HeadPosition = reader.Vec3();
            for (int i = 0; i < FacePose.ShapeCount; i++)
            {
                face.Shapes[i] = reader.Float();
            }

            face.SupportedShapes = (ulong)reader.Long();
            face.LeftEyeGaze = reader.Vec2();
            face.RightEyeGaze = reader.Vec2();
        }

        /// <summary>The time of an encoded observation, without decoding the rest. Pure.</summary>
        public static double ReadTime(ReadOnlySpan<byte> frame)
        {
            return BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(TimeOffset)));
        }

        /// <summary>How many whole observations follow a header in a file of <paramref name="fileLength"/> bytes; a cut-off last one is not counted. Pure.</summary>
        public static int FrameCount(long fileLength, int headerLength)
        {
            long body = fileLength - headerLength;
            return body <= 0 ? 0 : (int)Math.Min(int.MaxValue, body / FrameSize);
        }

        private ref struct Writer
        {
            private readonly Span<byte> _bytes;
            private int _at;

            public Writer(Span<byte> bytes)
            {
                _bytes = bytes;
                _at = 0;
            }

            public void Long(long value)
            {
                BinaryPrimitives.WriteInt64LittleEndian(_bytes.Slice(_at), value);
                _at += 8;
            }

            public void Double(double value)
            {
                Long(BitConverter.DoubleToInt64Bits(value));
            }

            public void Float(float value)
            {
                BinaryPrimitives.WriteInt32LittleEndian(_bytes.Slice(_at), BitConverter.SingleToInt32Bits(value));
                _at += 4;
            }

            public void Vec2(Vector2 value)
            {
                Float(value.x);
                Float(value.y);
            }

            public void Vec3(Vector3 value)
            {
                Float(value.x);
                Float(value.y);
                Float(value.z);
            }

            public void Quat(Quaternion value)
            {
                Float(value.x);
                Float(value.y);
                Float(value.z);
                Float(value.w);
            }

            public void Part(TrackedPart part)
            {
                _bytes[_at] = (byte)part.Status;
                _at += 1;
                Float(part.Confidence);
                Double(part.Timestamp);
            }

            public void Hand(HandPose hand)
            {
                Part(hand);
                for (int i = 0; i < HandPose.JointCount; i++)
                {
                    Vec3(hand.Positions[i]);
                    Vec2(hand.ImagePositions[i]);
                }

                Quat(hand.PalmRotation);
            }
        }

        private ref struct Reader
        {
            private readonly ReadOnlySpan<byte> _bytes;
            private int _at;

            public Reader(ReadOnlySpan<byte> bytes)
            {
                _bytes = bytes;
                _at = 0;
            }

            public long Long()
            {
                long value = BinaryPrimitives.ReadInt64LittleEndian(_bytes.Slice(_at));
                _at += 8;
                return value;
            }

            public double Double()
            {
                return BitConverter.Int64BitsToDouble(Long());
            }

            public float Float()
            {
                float value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(_bytes.Slice(_at)));
                _at += 4;
                return value;
            }

            public Vector2 Vec2()
            {
                return new Vector2(Float(), Float());
            }

            public Vector3 Vec3()
            {
                return new Vector3(Float(), Float(), Float());
            }

            public Quaternion Quat()
            {
                return new Quaternion(Float(), Float(), Float(), Float());
            }

            public void Part(TrackedPart part)
            {
                part.Status = (TrackingStatus)_bytes[_at];
                _at += 1;
                part.Confidence = Float();
                part.Timestamp = Double();
            }

            public void Hand(HandPose hand)
            {
                Part(hand);
                for (int i = 0; i < HandPose.JointCount; i++)
                {
                    hand.Positions[i] = Vec3();
                    hand.ImagePositions[i] = Vec2();
                }

                hand.PalmRotation = Quat();
            }
        }
    }
}
