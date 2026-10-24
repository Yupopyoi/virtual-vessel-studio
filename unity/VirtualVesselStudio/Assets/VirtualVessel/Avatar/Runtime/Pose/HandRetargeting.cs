using UnityEngine;
using VirtualVessel.Tracking;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// The hand's turn and the forearm's twist from the tracked palm (avatar detailed design 7.1, 7.7). The
    /// palm's twist about the forearm is measured from neutral: the back of the hand along the elbow's
    /// hinge axis, as with the thumb up when the forearm points forward from an arm at the side. The
    /// forearm and wrist turn at most a quarter turn either way from there, and a twist past that is left
    /// out. The shoulder is not turned: turning it for the rest on straight arms twisted the upper arm's
    /// sleeve far more than it looked like the person's (the user's screen recording, 2026-10-24). Pure.
    /// </summary>
    public static class HandRetargeting
    {
        /// <summary>
        /// The share of the forearm's twist its bone takes; the hand takes the rest at the wrist. Models
        /// without twist bones turn the whole forearm mesh with its bone, so all of it at the forearm wrings
        /// the elbow, and all of it at the hand wrings the wrist.
        /// </summary>
        public const float ForearmTwistShare = 0.5f;

        /// <summary>How far the forearm turns either way from neutral, in degrees.</summary>
        public const float ForearmReachDegrees = 90f;

        /// <summary>
        /// Where a reading of the twist wraps, in degrees from neutral toward pronation: a palm turned
        /// further is read as turned the other way (supination). Turning the palm up on an arm held out is
        /// common; turning it far down is not, and palms folded in front of the chest read up to about
        /// 115° of pronation on the user's recordings.
        /// </summary>
        public const float WrapDegrees = 150f;

        /// <summary>How far before the wrap a reading starts to fade back to neutral, in degrees.</summary>
        public const float WrapFadeDegrees = 30f;

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
        /// Twists the forearm of <paramref name="side"/> in <paramref name="into"/> and turns its hand toward
        /// <paramref name="palm"/>, <paramref name="presence"/> of the way from following the forearm (0).
        /// </summary>
        public static void Apply(ArmSide side, Quaternion palm, float presence, BodyTarget into)
        {
            bool left = side.IsLeft;
            Vector3 rest = PoseRetargeting.RestDirection(left);
            Quaternion lower = into[side.LowerArm];
            Vector3 lowerAxis = lower * rest;

            (float forearm, float impossible) = Split(Twist(left, lower, palm));

            // Supination turns the right arm (toward +X) negatively about it, and the left one positively.
            float sign = left ? 1f : -1f;
            Quaternion twisted = Quaternion.AngleAxis(forearm * ForearmTwistShare * presence * sign, lowerAxis) * lower;
            into[side.LowerArm] = twisted;

            Quaternion hand = Quaternion.AngleAxis(-impossible * sign, lowerAxis) * palm * Quaternion.Inverse(Quaternion.LookRotation(rest, Vector3.up));
            into[side.Hand] = Quaternion.Slerp(twisted, hand, presence);
        }

        /// <summary>
        /// The palm's twist about the forearm from neutral, in degrees, supination positive, read from
        /// <see cref="WrapDegrees"/> of pronation to 360° less that of supination. Pure.
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

            float twist = Vector3.SignedAngle(forearmBack, handBack, axis) * (left ? 1f : -1f);
            return twist < -WrapDegrees ? twist + 360f : twist;
        }

        /// <summary>
        /// How a <paramref name="twist"/> (supination positive) is taken: the forearm's part within its
        /// reach, and the part past it, left out of the hand. Within <see cref="WrapFadeDegrees"/> of the wrap
        /// a reading could be either way round, so it fades back to neutral there, and a reading crossing it
        /// does not jump. Pure.
        /// </summary>
        public static (float Forearm, float Impossible) Split(float twist)
        {
            float wrapAt = 360f - WrapDegrees;
            float sure = twist > wrapAt - WrapFadeDegrees
                ? (wrapAt - twist) / WrapFadeDegrees
                : twist < -WrapDegrees + WrapFadeDegrees
                    ? (twist + WrapDegrees) / WrapFadeDegrees
                    : 1f;
            float forearm = Mathf.Clamp(twist, -ForearmReachDegrees, ForearmReachDegrees) * Mathf.Clamp01(sure);
            return (forearm, twist - forearm);
        }
    }
}
