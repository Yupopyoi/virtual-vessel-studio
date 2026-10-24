using UnityEngine;

namespace VirtualVessel.Tracking.Processing
{
    /// <summary>
    /// Straightens an arm that points at the camera (tracking detailed design 8.4). With one camera, depth
    /// along its axis is least reliable there: on the user's recording of arms thrust forward, MediaPipe
    /// bent them 33-40 degrees. When both the upper arm and the forearm point toward the camera, the elbow
    /// moves toward the line from the shoulder to the wrist, more the closer both point to the camera; the
    /// shoulder and the wrist stay, so the hand stays where it is.
    /// </summary>
    /// <remarks>
    /// Rebuilding only the depth from bone lengths was compared first and did not straighten them, because
    /// MediaPipe's x and y already bend the arm (tracking detailed design 8.4).
    /// </remarks>
    public sealed class CameraAxisArmStraightening : IObservationCorrection
    {
        /// <summary>How far toward the camera (the cosine to its axis) both parts of the arm point before it starts to straighten.</summary>
        public const float Start = 0.5f;

        /// <summary>How far toward the camera they point when it is fully straight.</summary>
        public const float Full = 0.8f;

        /// <summary>Toward the camera in person space: the camera faces the person, who faces +Z.</summary>
        private static readonly Vector3 s_towardCamera = Vector3.forward;

        public ObservationTraits Requires => ObservationTraits.MonocularDepth;

        public void Apply(TrackingFrame observation)
        {
            BodyPose body = observation.Body;
            if (!body.IsUsable)
            {
                return;
            }

            StraightenArm(body, BodyJoint.LeftShoulder, BodyJoint.LeftElbow, BodyJoint.LeftWrist);
            StraightenArm(body, BodyJoint.RightShoulder, BodyJoint.RightElbow, BodyJoint.RightWrist);
        }

        /// <summary>
        /// Where the elbow goes: toward the point on the shoulder-to-wrist line that splits it as the upper
        /// arm and the forearm do, by how far both point toward <paramref name="towardCamera"/> between
        /// <paramref name="start"/> and <paramref name="full"/>. Pure.
        /// </summary>
        public static Vector3 Straightened(Vector3 shoulder, Vector3 elbow, Vector3 wrist, Vector3 towardCamera, float start, float full)
        {
            Vector3 upper = elbow - shoulder;
            Vector3 fore = wrist - elbow;
            float upperLength = upper.magnitude;
            float foreLength = fore.magnitude;
            if (upperLength < 1e-6f || foreLength < 1e-6f)
            {
                return elbow;
            }

            float toward = Mathf.Min(Vector3.Dot(upper, towardCamera) / upperLength, Vector3.Dot(fore, towardCamera) / foreLength);
            float weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, full, toward));
            if (weight <= 0f)
            {
                return elbow;
            }

            Vector3 onLine = Vector3.Lerp(shoulder, wrist, upperLength / (upperLength + foreLength));
            return Vector3.Lerp(elbow, onLine, weight);
        }

        private static void StraightenArm(BodyPose body, BodyJoint shoulder, BodyJoint elbow, BodyJoint wrist)
        {
            // A hidden wrist's position is a guess, and moving the elbow toward it would turn the upper arm.
            if (!body.IsSeen(shoulder) || !body.IsSeen(elbow) || !body.IsSeen(wrist))
            {
                return;
            }

            body[elbow] = Straightened(body[shoulder], body[elbow], body[wrist], s_towardCamera, Start, Full);
        }
    }
}
