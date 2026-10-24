namespace VirtualVessel.Tracking.Processing
{
    /// <summary>
    /// The 2D body (tracking detailed design 8.6): every body joint at the hips' depth, so the body moves
    /// only across and up, as the camera sees it well. Applied after the corrections and before smoothing;
    /// recordings keep what was observed.
    /// </summary>
    public static class BodyFlattening
    {
        /// <summary>
        /// Sets the depth of every joint of <paramref name="body"/> to the hips' (0 in person space). Writes
        /// the caller's copy of the observation, which is reused every frame (CLAUDE.md 15.1, 23).
        /// </summary>
        public static void Flatten(BodyPose body)
        {
            for (int i = 0; i < BodyPose.JointCount; i++)
            {
                body.Positions[i].z = 0f;
            }
        }
    }
}
