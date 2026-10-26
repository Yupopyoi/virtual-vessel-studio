using System;
using UnityEngine;

namespace VirtualVessel.Tracking
{
    /// <summary>
    /// What every part carries (tracking detailed design 6.1): whether its values can be used, how
    /// confident they are, and when the image they came from was taken.
    /// </summary>
    public abstract class TrackedPart
    {
        public TrackingStatus Status { get; set; }

        /// <summary>0 to 1.</summary>
        public float Confidence { get; set; }

        /// <summary>
        /// Seconds on the application's monotonic clock when the source image of the values was taken.
        /// While the part is lost, the time of the last usable values.
        /// </summary>
        public double Timestamp { get; set; }

        /// <summary>Whether the values can be used: tracked, possibly with low confidence.</summary>
        public bool IsUsable => Status == TrackingStatus.Tracked || Status == TrackingStatus.LowConfidence;

        protected void CopyHeader(TrackedPart other)
        {
            Status = other.Status;
            Confidence = other.Confidence;
            Timestamp = other.Timestamp;
        }
    }

    /// <summary>
    /// The body in person space: origin at the midpoint of the hips, +Y up, +Z the person's front,
    /// +X the person's right, meters (tracking detailed design 6.1, 6.2).
    /// </summary>
    public sealed class BodyPose : TrackedPart
    {
        public const int JointCount = 20;

        /// <summary>
        /// A joint seen less than this (MediaPipe's visibility, as <see cref="JointConfidence"/>) is not used to
        /// move anything; the one threshold for Tracking's corrections and the avatar alike.
        /// </summary>
        public const float MinVisibility = 0.5f;

        public Vector3[] Positions { get; } = new Vector3[JointCount];

        /// <summary>Positions in the image, 0 to 1 from the top left; used to estimate depth.</summary>
        public Vector2[] ImagePositions { get; } = new Vector2[JointCount];

        public float[] JointConfidence { get; } = new float[JointCount];

        public Vector3 this[BodyJoint joint]
        {
            get => Positions[(int)joint];
            set => Positions[(int)joint] = value;
        }

        /// <summary>
        /// Whether <paramref name="joint"/> is seen, so its position may move something: visible enough and
        /// inside the camera image (<see cref="IsSeen(float, Vector2)"/>).
        /// </summary>
        public bool IsSeen(BodyJoint joint)
        {
            return IsSeen(JointConfidence[(int)joint], ImagePositions[(int)joint]);
        }

        /// <summary>Whether <paramref name="joint"/> is inside the camera image, seen or hidden there.</summary>
        public bool IsInImage(BodyJoint joint)
        {
            return IsInImage(ImagePositions[(int)joint]);
        }

        /// <summary>
        /// Whether a joint of <paramref name="visibility"/> at <paramref name="image"/> (0 to 1 from the top
        /// left) is seen. A joint outside the image is a guess even when MediaPipe calls it visible: on the
        /// user's recordings, a wrist out of the image was visible past <see cref="MinVisibility"/> in up to
        /// 100% of those frames, and moved the avatar's arm where the person's was not (tracking detailed
        /// design 8.4). Pure.
        /// </summary>
        public static bool IsSeen(float visibility, Vector2 image)
        {
            return visibility >= MinVisibility && IsInImage(image);
        }

        /// <summary>Whether <paramref name="image"/> (0 to 1 from the top left) is inside the image, its edges included. Pure.</summary>
        public static bool IsInImage(Vector2 image)
        {
            return image.x >= 0f && image.x <= 1f && image.y >= 0f && image.y <= 1f;
        }

        public void CopyFrom(BodyPose other)
        {
            CopyHeader(other);
            Array.Copy(other.Positions, Positions, JointCount);
            Array.Copy(other.ImagePositions, ImagePositions, JointCount);
            Array.Copy(other.JointConfidence, JointConfidence, JointCount);
        }
    }

    /// <summary>One hand in person space (tracking detailed design 6.4).</summary>
    public sealed class HandPose : TrackedPart
    {
        public const int JointCount = 21;

        public Vector3[] Positions { get; } = new Vector3[JointCount];

        /// <summary>Positions in the image, 0 to 1 from the top left; for showing the points over the camera image.</summary>
        public Vector2[] ImagePositions { get; } = new Vector2[JointCount];

        /// <summary>From the wrist and the bases of the index and little fingers.</summary>
        public Quaternion PalmRotation { get; set; } = Quaternion.identity;

        public Vector3 this[HandJoint joint]
        {
            get => Positions[(int)joint];
            set => Positions[(int)joint] = value;
        }

        public void CopyFrom(HandPose other)
        {
            CopyHeader(other);
            Array.Copy(other.Positions, Positions, JointCount);
            Array.Copy(other.ImagePositions, ImagePositions, JointCount);
            PalmRotation = other.PalmRotation;
        }
    }

    /// <summary>The head and face movement (tracking detailed design 6.3).</summary>
    public sealed class FacePose : TrackedPart
    {
        public const int ShapeCount = 52;

        /// <summary>In person space.</summary>
        public Quaternion HeadRotation { get; set; } = Quaternion.identity;

        /// <summary>Relative to the camera, meters; for leaning and moving closer.</summary>
        public Vector3 HeadPosition { get; set; }

        /// <summary>0 to 1 per <see cref="FaceShape"/>.</summary>
        public float[] Shapes { get; } = new float[ShapeCount];

        /// <summary>One bit per <see cref="FaceShape"/> the provider can track. A mask avoids an array of flags.</summary>
        public ulong SupportedShapes { get; set; }

        /// <summary>Yaw and pitch of the left eye, degrees; positive yaw toward the person's right, positive pitch up.</summary>
        public Vector2 LeftEyeGaze { get; set; }

        public Vector2 RightEyeGaze { get; set; }

        public float this[FaceShape shape]
        {
            get => Shapes[(int)shape];
            set => Shapes[(int)shape] = value;
        }

        public bool Supports(FaceShape shape)
        {
            return (SupportedShapes & (1UL << (int)shape)) != 0;
        }

        public static ulong AllShapesExcept(params FaceShape[] unsupported)
        {
            ulong mask = (1UL << ShapeCount) - 1;
            foreach (FaceShape shape in unsupported)
            {
                mask &= ~(1UL << (int)shape);
            }

            return mask;
        }

        public void CopyFrom(FacePose other)
        {
            CopyHeader(other);
            HeadRotation = other.HeadRotation;
            HeadPosition = other.HeadPosition;
            Array.Copy(other.Shapes, Shapes, ShapeCount);
            SupportedShapes = other.SupportedShapes;
            LeftEyeGaze = other.LeftEyeGaze;
            RightEyeGaze = other.RightEyeGaze;
        }
    }

    /// <summary>
    /// One observation of a person (tracking detailed design 6.1). Providers fill it raw; the tracking
    /// source publishes a smoothed one. Objects are reused, never allocated per observation.
    /// </summary>
    public sealed class TrackingFrame
    {
        public long Sequence { get; set; }

        /// <summary>When this frame was produced, on the same clock as the part timestamps.</summary>
        public double Time { get; set; }

        public BodyPose Body { get; } = new BodyPose();

        /// <summary>The person's own left hand.</summary>
        public HandPose LeftHand { get; } = new HandPose();

        public HandPose RightHand { get; } = new HandPose();

        public FacePose Face { get; } = new FacePose();

        public HandPose Hand(bool left)
        {
            return left ? LeftHand : RightHand;
        }

        public void CopyFrom(TrackingFrame other)
        {
            Sequence = other.Sequence;
            Time = other.Time;
            Body.CopyFrom(other.Body);
            LeftHand.CopyFrom(other.LeftHand);
            RightHand.CopyFrom(other.RightHand);
            Face.CopyFrom(other.Face);
        }

        /// <summary>Moves every time by <paramref name="offset"/> seconds, as when replaying a recording.</summary>
        public void ShiftTime(double offset)
        {
            Time += offset;
            Body.Timestamp += offset;
            LeftHand.Timestamp += offset;
            RightHand.Timestamp += offset;
            Face.Timestamp += offset;
        }
    }
}
