using System;
using UnityEngine;

namespace VirtualVessel.Tracking.Recording
{
    /// <summary>
    /// Made-up movements with known truth and seeded noise (tracking detailed design 12). Only these go
    /// into the repository; recordings of real people stay on each developer's machine.
    /// </summary>
    public static class SyntheticMotion
    {
        public const float UpperArmLength = 0.28f;
        public const float ForearmLength = 0.25f;

        private static readonly Vector3 s_rightShoulder = new Vector3(0.18f, 0.45f, 0f);
        private static readonly Vector3 s_leftShoulder = new Vector3(-0.18f, 0.45f, 0f);

        /// <summary>
        /// Degrees the right arm is raised sideways (0 down, 90 level) at <paramref name="time"/>: still
        /// for 3 s, raised 90° in 0.3 s, held until 6 s, then waved ±30° at 0.5 Hz. Still, fast, and slow
        /// parts test steadiness, catching up, and following.
        /// </summary>
        public static float RaiseAndWave(double time)
        {
            if (time < 3.0)
            {
                return 0f;
            }

            if (time < 3.3)
            {
                return (float)(90.0 * (1.0 - Math.Cos(Math.PI * (time - 3.0) / 0.3)) / 2.0);
            }

            if (time < 6.0)
            {
                return 90f;
            }

            return (float)(90.0 + (30.0 * Math.Sin(2.0 * Math.PI * 0.5 * (time - 6.0))));
        }

        /// <summary>
        /// Observations of a person raising and waving the right arm (<see cref="RaiseAndWave"/>), with
        /// Gaussian noise of <paramref name="noiseMeters"/> on every joint. Same seed, same frames.
        /// </summary>
        public static TrackingFrame[] RaiseAndWaveFrames(double seconds, double rate, float noiseMeters, int seed)
        {
            var random = new System.Random(seed);
            int count = (int)(seconds * rate);
            var frames = new TrackingFrame[count];
            for (int i = 0; i < count; i++)
            {
                double time = i / rate;
                var frame = new TrackingFrame { Sequence = i + 1, Time = time };
                WriteBody(frame.Body, RaiseAndWave(time), time);
                for (int joint = 0; joint < BodyPose.JointCount; joint++)
                {
                    frame.Body.Positions[joint] += Noise(random, noiseMeters);
                }

                WriteFace(frame.Face, time);
                frame.LeftHand.Status = TrackingStatus.Lost;
                frame.RightHand.Status = TrackingStatus.Lost;
                frames[i] = frame;
            }

            return frames;
        }

        /// <summary>How far an arm is raised sideways, in degrees, from its shoulder and elbow: the inverse of the motion.</summary>
        public static float ArmAngle(BodyPose body, bool right)
        {
            Vector3 upperArm = right
                ? body[BodyJoint.RightElbow] - body[BodyJoint.RightShoulder]
                : body[BodyJoint.LeftElbow] - body[BodyJoint.LeftShoulder];
            float sideways = right ? upperArm.x : -upperArm.x;
            return Mathf.Atan2(sideways, -upperArm.y) * Mathf.Rad2Deg;
        }

        /// <summary>A still person with the right arm raised <paramref name="rightArmDegrees"/> sideways.</summary>
        public static void WriteBody(BodyPose body, float rightArmDegrees, double time)
        {
            body.Status = TrackingStatus.Tracked;
            body.Confidence = 0.95f;
            body.Timestamp = time;
            body[BodyJoint.Hips] = Vector3.zero;
            body[BodyJoint.LeftHip] = new Vector3(-0.1f, 0f, 0f);
            body[BodyJoint.RightHip] = new Vector3(0.1f, 0f, 0f);
            body[BodyJoint.Neck] = new Vector3(0f, 0.45f, 0f);
            body[BodyJoint.Head] = new Vector3(0f, 0.62f, 0f);
            body[BodyJoint.Nose] = new Vector3(0f, 0.62f, 0.1f);
            body[BodyJoint.LeftEar] = new Vector3(-0.08f, 0.62f, 0f);
            body[BodyJoint.RightEar] = new Vector3(0.08f, 0.62f, 0f);
            body[BodyJoint.LeftShoulder] = s_leftShoulder;
            body[BodyJoint.RightShoulder] = s_rightShoulder;

            float radians = rightArmDegrees * Mathf.Deg2Rad;
            var right = new Vector3(Mathf.Sin(radians), -Mathf.Cos(radians), 0f);
            body[BodyJoint.RightElbow] = s_rightShoulder + (right * UpperArmLength);
            body[BodyJoint.RightWrist] = s_rightShoulder + (right * (UpperArmLength + ForearmLength));
            body[BodyJoint.LeftElbow] = s_leftShoulder + (Vector3.down * UpperArmLength);
            body[BodyJoint.LeftWrist] = s_leftShoulder + (Vector3.down * (UpperArmLength + ForearmLength));

            body[BodyJoint.LeftKnee] = new Vector3(-0.1f, -0.45f, 0f);
            body[BodyJoint.RightKnee] = new Vector3(0.1f, -0.45f, 0f);
            body[BodyJoint.LeftAnkle] = new Vector3(-0.1f, -0.88f, 0f);
            body[BodyJoint.RightAnkle] = new Vector3(0.1f, -0.88f, 0f);
            body[BodyJoint.LeftFoot] = new Vector3(-0.1f, -0.92f, 0.12f);
            body[BodyJoint.RightFoot] = new Vector3(0.1f, -0.92f, 0.12f);

            for (int i = 0; i < BodyPose.JointCount; i++)
            {
                body.JointConfidence[i] = 0.95f;
            }
        }

        private static void WriteFace(FacePose face, double time)
        {
            face.Status = TrackingStatus.Tracked;
            face.Confidence = 0.95f;
            face.Timestamp = time;
            face.HeadRotation = Quaternion.identity;
            face.SupportedShapes = FacePose.AllShapesExcept(FaceShape.TongueOut);
        }

        private static Vector3 Noise(System.Random random, float scale)
        {
            return new Vector3(Gaussian(random), Gaussian(random), Gaussian(random)) * scale;
        }

        private static float Gaussian(System.Random random)
        {
            // Box–Muller; 1 - NextDouble() keeps the logarithm away from zero.
            double u1 = 1.0 - random.NextDouble();
            double u2 = random.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }
    }
}
