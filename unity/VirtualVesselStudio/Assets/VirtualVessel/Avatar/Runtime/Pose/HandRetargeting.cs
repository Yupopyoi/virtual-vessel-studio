using UnityEngine;
using VirtualVessel.Tracking;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// The hand's turn and the forearm's twist from the tracked palm (avatar detailed design 7.1, 7.5): the
    /// hand takes the palm's whole turn, and the forearm a share of the palm's twist about it, so neither
    /// the elbow nor the wrist wrings alone. Before this the forearm kept the upper arm's twist and the
    /// hand the forearm's, so the palm never turned (the user's camera check, 2026-10-22). Pure.
    /// </summary>
    public static class HandRetargeting
    {
        /// <summary>
        /// The share of the palm's twist the forearm takes; the hand takes the rest at the wrist. Models
        /// without twist bones turn the whole forearm mesh with its bone, so all of it at the forearm wrings
        /// the elbow, and all of it at the hand wrings the wrist.
        /// </summary>
        public const float ForearmTwistShare = 0.5f;

        /// <summary>
        /// How far the palm turns from facing down (the reference pose) toward facing up (supination), and
        /// the other way (pronation), in degrees. A palm turned nearly over is read within these, so its
        /// twist does not flip between the two ways round.
        /// </summary>
        public const float MaxSupinationDegrees = 180f;
        public const float MaxPronationDegrees = 60f;

        /// <summary>The person's hand that moves the avatar's <paramref name="side"/>: the other one in a mirror.</summary>
        public static HandPose PersonHand(TrackingFrame frame, ArmSide side, bool mirror)
        {
            return side.IsLeft != mirror ? frame.LeftHand : frame.RightHand;
        }

        /// <summary>
        /// The palm in the avatar root's space, fingers forward and the back of the hand up, from the
        /// person's <paramref name="hand"/>; reflected through X = 0 in a mirror. False when the hand is not seen.
        /// </summary>
        public static bool TryPalm(HandPose hand, bool mirror, out Quaternion palm)
        {
            palm = Quaternion.identity;
            if (hand == null || !hand.IsUsable)
            {
                return false;
            }

            Vector3 fingers = hand.PalmRotation * Vector3.forward;
            Vector3 back = hand.PalmRotation * Vector3.up;
            if (mirror)
            {
                fingers.x = -fingers.x;
                back.x = -back.x;
            }

            palm = Quaternion.LookRotation(fingers, back);
            return true;
        }

        /// <summary>
        /// Turns the hand of <paramref name="side"/> in <paramref name="into"/>, which holds the arm, to
        /// <paramref name="palm"/> and twists the forearm by its share, both <paramref name="presence"/> of
        /// the way from following the forearm (0) to the palm (1).
        /// </summary>
        public static void Apply(ArmSide side, Quaternion palm, float presence, BodyTarget into)
        {
            Quaternion lower = into[side.LowerArm];
            Vector3 rest = PoseRetargeting.RestDirection(side.IsLeft);
            Quaternion hand = palm * Quaternion.Inverse(Quaternion.LookRotation(rest, Vector3.up));
            Quaternion twisted = Quaternion.AngleAxis(Twist(side.IsLeft, lower, palm) * ForearmTwistShare * presence, lower * rest) * lower;
            into[side.LowerArm] = twisted;
            into[side.Hand] = Quaternion.Slerp(twisted, hand, presence);
        }

        /// <summary>
        /// The palm's twist about the forearm from the back of the forearm (up in the reference pose), in
        /// degrees, read within the reach of supination and pronation. Pure.
        /// </summary>
        public static float Twist(bool left, Quaternion lower, Quaternion palm)
        {
            Vector3 axis = lower * PoseRetargeting.RestDirection(left);
            Vector3 forearmBack = Vector3.ProjectOnPlane(lower * Vector3.up, axis);
            Vector3 handBack = Vector3.ProjectOnPlane(palm * Vector3.up, axis);
            if (forearmBack.sqrMagnitude < 1e-8f || handBack.sqrMagnitude < 1e-8f)
            {
                return 0f;
            }

            // Supination turns the right forearm (toward +X) negatively about it, and the left one positively.
            // Outside the reach, the nearer end: a palm a little past facing up is turned up, not pronated.
            float twist = Vector3.SignedAngle(forearmBack, handBack, axis) * (left ? 1f : -1f);
            if (twist < -MaxPronationDegrees)
            {
                twist = (-MaxPronationDegrees - twist) < (twist + 360f - MaxSupinationDegrees) ? -MaxPronationDegrees : MaxSupinationDegrees;
            }

            return Mathf.Min(twist, MaxSupinationDegrees) * (left ? 1f : -1f);
        }
    }
}
