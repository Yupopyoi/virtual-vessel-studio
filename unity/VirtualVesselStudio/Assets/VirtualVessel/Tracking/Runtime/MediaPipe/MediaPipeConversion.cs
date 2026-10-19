using System;
using UnityEngine;

namespace VirtualVessel.Tracking.MediaPipe
{
    /// <summary>
    /// From MediaPipe's points to the common form (tracking detailed design 5.2, 6, 16.2). Pure
    /// functions on plain arrays, not on the plugin's types, so they are tested without the plugin
    /// and stay the same when its API changes. The provider copies the plugin's results into arrays
    /// and calls these.
    /// </summary>
    /// <remarks>
    /// MediaPipe world coordinates (Pose and Hand) have x to the right of the image, y down, and z
    /// away from the camera, in meters. The person faces the camera, and the camera image is not
    /// mirrored, so the person's right is the image's left: person space (+X the person's right, +Y
    /// up, +Z the person's front) is (-x, -y, -z). MediaPipe names joints by the person's own side.
    /// The Write methods fill the caller's objects, which are reused to avoid allocating per
    /// observation (CLAUDE.md 15.1, 23).
    /// </remarks>
    internal static class MediaPipeConversion
    {
        public const int PoseLandmarkCount = 33;
        public const int HandLandmarkCount = 21;

        /// <summary>Degrees of eye rotation at a look value of 1; provisional until checked with a camera.</summary>
        public const float MaxGazeDegrees = 25f;

        /// <summary>
        /// How far, as a share of the image width, a detected hand's wrist or a face's nose may be from the
        /// body's (tracking detailed design 6.4, 8.3). Farther ones belong to something behind the person.
        /// </summary>
        public const float MaxDistanceFromBody = 0.12f;

        /// <summary>The face mesh point at the tip of the nose.</summary>
        public const int FaceNoseTip = 1;

        private const int Computed = -1;

        // The Pose landmark for each BodyJoint; Computed for the midpoints MediaPipe has no point for.
        private static readonly int[] s_poseIndex =
        {
            Computed, // Head: between the ears
            0,        // Nose
            7,        // LeftEar
            8,        // RightEar
            Computed, // Neck: between the shoulders
            11,       // LeftShoulder
            12,       // RightShoulder
            13,       // LeftElbow
            14,       // RightElbow
            15,       // LeftWrist
            16,       // RightWrist
            Computed, // Hips: between the hips, the origin of MediaPipe's world coordinates too
            23,       // LeftHip
            24,       // RightHip
            25,       // LeftKnee
            26,       // RightKnee
            27,       // LeftAnkle
            28,       // RightAnkle
            31,       // LeftFoot (foot index)
            32,       // RightFoot
        };

        // Joints whose confidence makes the body's: what the upper body needs.
        private static readonly BodyJoint[] s_upperBody =
        {
            BodyJoint.Nose, BodyJoint.LeftShoulder, BodyJoint.RightShoulder, BodyJoint.LeftElbow,
            BodyJoint.RightElbow, BodyJoint.LeftWrist, BodyJoint.RightWrist,
        };

        // Turning half a turn about Y maps the plugin's camera space (x right, y up, z away) to person space.
        private static readonly Quaternion s_halfTurn = Quaternion.AngleAxis(180f, Vector3.up);

        /// <summary>A MediaPipe world point (Pose or Hand) in person space.</summary>
        public static Vector3 ToPerson(Vector3 world)
        {
            return new Vector3(-world.x, -world.y, -world.z);
        }

        /// <summary>
        /// Fills <paramref name="body"/> from the 33 Pose landmarks: <paramref name="world"/> in MediaPipe
        /// world coordinates, <paramref name="image"/> 0 to 1 from the top left of the image, and
        /// <paramref name="visibility"/> 0 to 1.
        /// </summary>
        public static void WriteBody(Vector3[] world, Vector2[] image, float[] visibility, double time, BodyPose body)
        {
            CheckLength(world, PoseLandmarkCount, nameof(world));
            CheckLength(image, PoseLandmarkCount, nameof(image));
            CheckLength(visibility, PoseLandmarkCount, nameof(visibility));

            for (int joint = 0; joint < BodyPose.JointCount; joint++)
            {
                int index = s_poseIndex[joint];
                if (index == Computed)
                {
                    continue;
                }

                body.Positions[joint] = ToPerson(world[index]);
                body.ImagePositions[joint] = image[index];
                body.JointConfidence[joint] = Mathf.Clamp01(visibility[index]);
            }

            Midpoint(body, BodyJoint.Head, BodyJoint.LeftEar, BodyJoint.RightEar);
            Midpoint(body, BodyJoint.Neck, BodyJoint.LeftShoulder, BodyJoint.RightShoulder);
            Midpoint(body, BodyJoint.Hips, BodyJoint.LeftHip, BodyJoint.RightHip);

            float confidence = 0f;
            foreach (BodyJoint joint in s_upperBody)
            {
                confidence += body.JointConfidence[(int)joint];
            }

            body.Confidence = confidence / s_upperBody.Length;
            body.Status = TrackingStatus.Tracked;
            body.Timestamp = time;
        }

        /// <summary>A MediaPipe blendshape name, such as "eyeBlinkLeft", as a <see cref="FaceShape"/>; false for "_neutral" and unknown names.</summary>
        public static bool TryParseShape(string name, out FaceShape shape)
        {
            shape = default;
            return !string.IsNullOrEmpty(name)
                && char.IsLetter(name[0])
                && Enum.TryParse(name, ignoreCase: true, out shape)
                && Enum.IsDefined(typeof(FaceShape), shape);
        }

        /// <summary>The same movement on the other side, such as EyeBlinkRight for EyeBlinkLeft; others unchanged.</summary>
        public static FaceShape OtherSide(FaceShape shape)
        {
            string name = shape.ToString();
            if (name.EndsWith("Left", StringComparison.Ordinal))
            {
                return (FaceShape)Enum.Parse(typeof(FaceShape), name.Substring(0, name.Length - 4) + "Right");
            }

            if (name.EndsWith("Right", StringComparison.Ordinal))
            {
                return (FaceShape)Enum.Parse(typeof(FaceShape), name.Substring(0, name.Length - 5) + "Left");
            }

            return shape;
        }

        /// <summary>
        /// The head rotation in person space from the facial transformation matrix as the plugin gives
        /// it (camera space with x right, y up, z away from the camera). A face looking into the camera
        /// is the identity.
        /// </summary>
        public static Quaternion HeadRotation(Matrix4x4 matrix)
        {
            Quaternion camera = matrix.rotation;
            return s_halfTurn * camera * Quaternion.Inverse(s_halfTurn);
        }

        /// <summary>The head position in person space, meters, from the matrix's translation in centimeters.</summary>
        public static Vector3 HeadPosition(Matrix4x4 matrix)
        {
            Vector4 translation = matrix.GetColumn(3);
            return new Vector3(-translation.x, translation.y, -translation.z) * 0.01f;
        }

        /// <summary>
        /// Yaw (positive toward the person's right) and pitch (positive up), degrees, of one eye from
        /// its look values. Looking in is toward the nose: the person's right for the left eye.
        /// </summary>
        public static Vector2 Gaze(float lookIn, float lookOut, float lookUp, float lookDown, bool leftEye)
        {
            float yaw = (leftEye ? lookIn - lookOut : lookOut - lookIn) * MaxGazeDegrees;
            float pitch = (lookUp - lookDown) * MaxGazeDegrees;
            return new Vector2(yaw, pitch);
        }

        /// <summary>
        /// Fills <paramref name="face"/>: <paramref name="shapes"/> per <see cref="FaceShape"/>, already on
        /// the person's own sides, and the plugin's facial transformation matrix.
        /// </summary>
        public static void WriteFace(float[] shapes, ulong supported, Matrix4x4 matrix, double time, FacePose face)
        {
            CheckLength(shapes, FacePose.ShapeCount, nameof(shapes));
            for (int i = 0; i < FacePose.ShapeCount; i++)
            {
                face.Shapes[i] = Mathf.Clamp01(shapes[i]);
            }

            face.SupportedShapes = supported;
            face.HeadRotation = HeadRotation(matrix);
            face.HeadPosition = HeadPosition(matrix);
            face.LeftEyeGaze = Gaze(face[FaceShape.EyeLookInLeft], face[FaceShape.EyeLookOutLeft], face[FaceShape.EyeLookUpLeft], face[FaceShape.EyeLookDownLeft], leftEye: true);
            face.RightEyeGaze = Gaze(face[FaceShape.EyeLookInRight], face[FaceShape.EyeLookOutRight], face[FaceShape.EyeLookUpRight], face[FaceShape.EyeLookDownRight], leftEye: false);

            // The landmarker reports a face only above its presence threshold and gives no score.
            face.Confidence = 1f;
            face.Status = TrackingStatus.Tracked;
            face.Timestamp = time;
        }

        /// <summary>
        /// The palm's rotation in person space: forward from the wrist to the base of the middle finger,
        /// up out of the back of the hand. The thumb is on opposite sides of the two hands, so the left
        /// hand turns the normal around.
        /// </summary>
        public static Quaternion PalmRotation(Vector3 wrist, Vector3 indexBase, Vector3 middleBase, Vector3 littleBase, bool left)
        {
            Vector3 forward = middleBase - wrist;
            Vector3 back = Vector3.Cross(indexBase - wrist, littleBase - wrist);
            if (left)
            {
                back = -back;
            }

            return forward.sqrMagnitude < 1e-12f || back.sqrMagnitude < 1e-12f
                ? Quaternion.identity
                : Quaternion.LookRotation(forward, back);
        }

        /// <summary>
        /// Fills <paramref name="hand"/> from 21 Hand landmarks. Their world coordinates are around the
        /// hand itself, so the shape is moved to <paramref name="wrist"/>, the wrist in person space from
        /// the body (zero when the body is not seen).
        /// </summary>
        public static void WriteHand(Vector3[] world, Vector2[] image, float confidence, Vector3 wrist, bool left, double time, HandPose hand)
        {
            CheckLength(world, HandLandmarkCount, nameof(world));
            CheckLength(image, HandLandmarkCount, nameof(image));
            Vector3 origin = ToPerson(world[0]);
            for (int i = 0; i < HandPose.JointCount; i++)
            {
                hand.Positions[i] = ToPerson(world[i]) - origin + wrist;
                hand.ImagePositions[i] = image[i];
            }

            hand.PalmRotation = PalmRotation(
                hand[HandJoint.Wrist], hand[HandJoint.IndexMcp], hand[HandJoint.MiddleMcp], hand[HandJoint.LittleMcp], left);
            hand.Confidence = Mathf.Clamp01(confidence);
            hand.Status = TrackingStatus.Tracked;
            hand.Timestamp = time;
        }

        /// <summary>
        /// Whether a single detected hand is the person's left (tracking detailed design 6.4): the body
        /// wrist it is nearer to in the image when the body is seen. Otherwise MediaPipe's handedness,
        /// which assumes a mirrored image: on this unmirrored camera "Right" is the person's left.
        /// </summary>
        public static bool IsLeft(Vector2 handWrist, bool bodySeen, Vector2 leftWrist, Vector2 rightWrist, string handedness)
        {
            if (bodySeen)
            {
                return (handWrist - leftWrist).sqrMagnitude <= (handWrist - rightWrist).sqrMagnitude;
            }

            return string.Equals(handedness, "Right", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// For two detected hands, whether the first is the person's left: the pairing with the body
        /// wrists that is nearer in total, so both never go to the same side. Without the body, the
        /// first hand's handedness decides, as for one hand.
        /// </summary>
        public static bool FirstIsLeft(Vector2 first, Vector2 second, bool bodySeen, Vector2 leftWrist, Vector2 rightWrist, string firstHandedness)
        {
            if (!bodySeen)
            {
                return IsLeft(first, false, leftWrist, rightWrist, firstHandedness);
            }

            float asIs = (first - leftWrist).magnitude + (second - rightWrist).magnitude;
            float swapped = (first - rightWrist).magnitude + (second - leftWrist).magnitude;
            return asIs <= swapped;
        }

        /// <summary>
        /// Whether a detected point belongs to the person: near the body's matching point when the body is
        /// seen, and always when it is not, since there is nothing to compare with. Pure.
        /// </summary>
        public static bool IsNearBody(Vector2 detected, Vector2 body, bool bodySeen)
        {
            return !bodySeen || (detected - body).sqrMagnitude <= MaxDistanceFromBody * MaxDistanceFromBody;
        }

        /// <summary>
        /// Which of <paramref name="count"/> detected faces is the person's: the one whose nose is nearest the
        /// body's nose and near enough; the first (the most confident) when the body is not seen; -1 for
        /// none. Pure.
        /// </summary>
        public static int ChooseFace(Vector2[] noses, int count, Vector2 bodyNose, bool bodySeen)
        {
            if (count <= 0)
            {
                return -1;
            }

            if (!bodySeen)
            {
                return 0;
            }

            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                float distance = (noses[i] - bodyNose).sqrMagnitude;
                if (distance < bestDistance && IsNearBody(noses[i], bodyNose, true))
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static void Midpoint(BodyPose body, BodyJoint joint, BodyJoint a, BodyJoint b)
        {
            body[joint] = (body[a] + body[b]) * 0.5f;
            body.ImagePositions[(int)joint] = (body.ImagePositions[(int)a] + body.ImagePositions[(int)b]) * 0.5f;
            body.JointConfidence[(int)joint] = Mathf.Min(body.JointConfidence[(int)a], body.JointConfidence[(int)b]);
        }

        private static void CheckLength<T>(T[] array, int length, string name)
        {
            if (array == null || array.Length < length)
            {
                throw new ArgumentException($"Needs {length} values.", name);
            }
        }
    }
}
