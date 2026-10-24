using UnityEngine;

namespace VirtualVessel.Tracking.Processing
{
    /// <summary>
    /// Gives each hand the forearm's depth (tracking detailed design 8.4): the hand keeps the turn from the
    /// forearm it shows across and up, and leans toward or away from the camera as the forearm does. The
    /// hands and the body are inferred apart, each guessing its own depth, and they disagree: on the user's
    /// recordings the hand's direction was 24-63° off the forearm's in 3D, but only 4-26° as seen from the
    /// front, so the avatar's wrists bent where the person's did not (2026-10-24).
    /// </summary>
    public sealed class HandDepthFromForearm : IObservationCorrection
    {
        /// <summary>
        /// How much of the hand must show across and up (its length there over its whole length) for its turn
        /// there to count fully; a hand pointing at the camera shows little and turns by chance.
        /// </summary>
        public const float SeenFrom = 0.1f;
        public const float SeenFully = 0.3f;

        public ObservationTraits Requires => ObservationTraits.MonocularDepth;

        public void Apply(TrackingFrame observation)
        {
            BodyPose body = observation.Body;
            if (!body.IsUsable)
            {
                return;
            }

            Align(body, BodyJoint.LeftElbow, BodyJoint.LeftWrist, observation.LeftHand);
            Align(body, BodyJoint.RightElbow, BodyJoint.RightWrist, observation.RightHand);
        }

        /// <summary>
        /// The palm with its fingers along <paramref name="forearm"/> turned about the camera's axis (Z) as
        /// the hand turns from it across and up, and its back the palm's back at right angles to that. A
        /// hand that shows little across and up turns less, toward lying along the forearm. Pure.
        /// </summary>
        public static Quaternion Aligned(Vector3 forearm, Quaternion palm)
        {
            Vector3 fingers = palm * Vector3.forward;
            var forearmSeen = new Vector2(forearm.x, forearm.y);
            var handSeen = new Vector2(fingers.x, fingers.y);
            if (forearm.sqrMagnitude < 1e-12f || forearmSeen.sqrMagnitude < 1e-12f || handSeen.sqrMagnitude < 1e-12f)
            {
                return palm;
            }

            float seen = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SeenFrom, SeenFully, handSeen.magnitude));
            float turn = Vector2.SignedAngle(forearmSeen, handSeen) * seen;
            Vector3 aligned = Quaternion.AngleAxis(turn, Vector3.forward) * forearm.normalized;
            Vector3 back = Vector3.ProjectOnPlane(palm * Vector3.up, aligned);
            return back.sqrMagnitude < 1e-12f ? palm : Quaternion.LookRotation(aligned, back);
        }

        private static void Align(BodyPose body, BodyJoint elbow, BodyJoint wrist, HandPose hand)
        {
            if (!hand.IsUsable
                || body.JointConfidence[(int)elbow] < BodyPose.MinVisibility
                || body.JointConfidence[(int)wrist] < BodyPose.MinVisibility)
            {
                return;
            }

            hand.PalmRotation = Aligned(body[wrist] - body[elbow], hand.PalmRotation);
        }
    }
}
