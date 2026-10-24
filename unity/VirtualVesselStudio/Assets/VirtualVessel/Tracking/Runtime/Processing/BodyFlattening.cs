namespace VirtualVessel.Tracking.Processing
{
    /// <summary>
    /// The 2D body (tracking detailed design 8.6): every body joint at the hips' depth, so the body moves
    /// only across and up, as the camera sees it well, with the elbows and wrists a little in front so the
    /// arms always pass in front of the body. Applied after the corrections and before smoothing;
    /// recordings keep what was observed.
    /// </summary>
    public static class BodyFlattening
    {
        /// <summary>
        /// How far in front of the body the elbows and wrists are put, in meters. With them at the body's
        /// own depth, the avatar's arms were kept out of its torso by going behind it (the user's camera
        /// check, 2026-10-24); a hanging upper arm leans about 20° forward with this.
        /// </summary>
        public const float ArmsForwardMeters = 0.1f;

        /// <summary>
        /// How much further forward the wrists are than the elbows, in meters, so the forearm leans forward
        /// a little. Level with the elbow, a forearm swung from up to down passed straight through the line
        /// of the upper arm, its bend turned over at once, and the arm twisted up to 120° in a frame on the
        /// recordings replayed in 2D; leaning forward, it bends forward on the way, as in 3D.
        /// </summary>
        public const float ForearmForwardMeters = 0.05f;

        /// <summary>
        /// Sets the depth of every joint of <paramref name="body"/> to the hips' (0 in person space), of the
        /// elbows to <see cref="ArmsForwardMeters"/> in front, and of the wrists <see cref="ForearmForwardMeters"/>
        /// further. Writes the caller's copy of the observation, which is reused every frame (CLAUDE.md 15.1, 23).
        /// </summary>
        public static void Flatten(BodyPose body)
        {
            for (int i = 0; i < BodyPose.JointCount; i++)
            {
                body.Positions[i].z = Depth((BodyJoint)i);
            }
        }

        private static float Depth(BodyJoint joint)
        {
            switch (joint)
            {
                case BodyJoint.LeftElbow:
                case BodyJoint.RightElbow:
                    return ArmsForwardMeters;
                case BodyJoint.LeftWrist:
                case BodyJoint.RightWrist:
                    return ArmsForwardMeters + ForearmForwardMeters;
                default:
                    return 0f;
            }
        }
    }
}
