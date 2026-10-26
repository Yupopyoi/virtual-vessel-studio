using UnityEngine;

namespace VirtualVessel.Tracking.Processing
{
    /// <summary>
    /// The person's front (tracking detailed design 8.5, system design 8.11). With the camera to one side,
    /// a person facing their own front is seen turned, and so was the avatar. "Face the front" takes how
    /// far the person is turned now as their front; every observation is then turned back by that much
    /// about the vertical, before smoothing.
    /// </summary>
    public static class FacingCorrection
    {
        /// <summary>
        /// The most the front is turned from the camera's. A camera further to the side sees too little of
        /// the far side of the body to track it, so a larger turn more likely comes from the person looking
        /// away when the front was taken.
        /// </summary>
        public const float MaxDegrees = 60f;

        /// <summary>
        /// How far <paramref name="body"/> is turned to its right about the vertical, in degrees, from the
        /// line between its shoulders; false when the shoulders are not seen. Pure.
        /// </summary>
        public static bool TryBodyYaw(BodyPose body, out float degrees)
        {
            degrees = 0f;
            if (body == null || !body.IsUsable || !body.IsSeen(BodyJoint.LeftShoulder) || !body.IsSeen(BodyJoint.RightShoulder))
            {
                return false;
            }

            return TryYawOfRight(body[BodyJoint.RightShoulder] - body[BodyJoint.LeftShoulder], out degrees);
        }

        /// <summary>How far <paramref name="face"/>'s head is turned to its right about the vertical, in degrees; false when it is not tracked. Pure.</summary>
        public static bool TryHeadYaw(FacePose face, out float degrees)
        {
            degrees = 0f;
            return face != null && face.IsUsable && TryYawOfRight(face.HeadRotation * Vector3.right, out degrees);
        }

        /// <summary>
        /// How far the person in <paramref name="frame"/> is turned to the right, in degrees: from the
        /// shoulders, or from the head without them. The 2D body (<paramref name="flat"/>) faces the camera
        /// by construction, so only the head tells the turn there. False when neither is seen. Pure.
        /// </summary>
        public static bool TryTurn(TrackingFrame frame, bool flat, out float degrees)
        {
            if (!flat && TryBodyYaw(frame.Body, out degrees))
            {
                return true;
            }

            return TryHeadYaw(frame.Face, out degrees);
        }

        /// <summary>
        /// The front after taking the person's turn now as the front: <paramref name="current"/>, the front
        /// already in use, plus <paramref name="seen"/>, the turn seen in observations already turned back
        /// by it, within <see cref="MaxDegrees"/>. Pure.
        /// </summary>
        public static float Next(float current, float seen)
        {
            return Mathf.Clamp(current + seen, -MaxDegrees, MaxDegrees);
        }

        /// <summary>The turn that brings an observation back to the front <paramref name="degrees"/> away. Pure.</summary>
        public static Quaternion TurnBack(float degrees)
        {
            return Quaternion.AngleAxis(-degrees, Vector3.up);
        }

        /// <summary>
        /// Turns <paramref name="frame"/> by <paramref name="turn"/> about the vertical through the hips (the
        /// person-space origin): the head, the hands, and, when <paramref name="body"/>, the body. The 2D body
        /// faces the camera by construction and keeps its place. The head's position is relative to the
        /// camera and stays. Writes the caller's copy of the observation, which is reused every frame
        /// (CLAUDE.md 15.1, 23).
        /// </summary>
        public static void Turn(TrackingFrame frame, Quaternion turn, bool body)
        {
            if (body)
            {
                Turn(frame.Body.Positions, turn);
            }

            Turn(frame.LeftHand, turn);
            Turn(frame.RightHand, turn);
            frame.Face.HeadRotation = turn * frame.Face.HeadRotation;
        }

        private static void Turn(HandPose hand, Quaternion turn)
        {
            Turn(hand.Positions, turn);
            hand.PalmRotation = turn * hand.PalmRotation;
        }

        private static void Turn(Vector3[] positions, Quaternion turn)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = turn * positions[i];
            }
        }

        private static bool TryYawOfRight(Vector3 right, out float degrees)
        {
            // A turn of a degrees about +Y (to the right, seen from above) takes +X (the person's right) to (cos a, 0, -sin a).
            degrees = 0f;
            if ((right.x * right.x) + (right.z * right.z) < 1e-8f)
            {
                return false;
            }

            degrees = Mathf.Atan2(-right.z, right.x) * Mathf.Rad2Deg;
            return true;
        }
    }
}
