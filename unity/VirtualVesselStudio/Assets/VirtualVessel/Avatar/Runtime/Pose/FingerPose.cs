using System;
using UnityEngine;

namespace VirtualVessel.Avatar
{
    /// <summary>The fingers, in the order of the model's bones (thumb first).</summary>
    public enum Finger
    {
        Thumb,
        Index,
        Middle,
        Ring,
        Little,
    }

    /// <summary>
    /// One hand's finger angles, in degrees (avatar detailed design 7.5): per finger, the bend at its three
    /// joints from the hand out, toward the palm, and how far it is spread toward the thumb. For the thumb
    /// the first joint is the base (CMC), folding it across the palm. Reused every frame (CLAUDE.md 23).
    /// </summary>
    public sealed class FingerPose
    {
        public const int FingerCount = 5;
        public const int JointCount = 3;

        // Per finger: the three bends, then the spread.
        private const int Stride = JointCount + 1;

        /// <summary>A loosely closed hand, the rest when no hand is seen. Not to be written.</summary>
        public static readonly FingerPose Relaxed = CreateRelaxed();

        public float[] Values { get; } = new float[FingerCount * Stride];

        public float Bend(Finger finger, int joint)
        {
            return Values[((int)finger * Stride) + joint];
        }

        public void SetBend(Finger finger, int joint, float degrees)
        {
            Values[((int)finger * Stride) + joint] = degrees;
        }

        public float Spread(Finger finger)
        {
            return Values[((int)finger * Stride) + JointCount];
        }

        public void SetSpread(Finger finger, float degrees)
        {
            Values[((int)finger * Stride) + JointCount] = degrees;
        }

        public void CopyFrom(FingerPose other)
        {
            Array.Copy(other.Values, Values, Values.Length);
        }

        /// <summary>
        /// Writes into <paramref name="into"/> the pose <paramref name="t"/> of the way from
        /// <paramref name="from"/> to <paramref name="to"/>. <paramref name="into"/> may be either input. Pure
        /// apart from writing the caller's buffer.
        /// </summary>
        public static void Lerp(FingerPose from, FingerPose to, float t, FingerPose into)
        {
            t = Mathf.Clamp01(t);
            for (int i = 0; i < into.Values.Length; i++)
            {
                into.Values[i] = Mathf.Lerp(from.Values[i], to.Values[i], t);
            }
        }

        /// <summary>
        /// Writes into <paramref name="into"/> each angle of <paramref name="from"/> moved toward
        /// <paramref name="to"/> by at most <paramref name="maxDegrees"/>. <paramref name="into"/> may be
        /// either input. Pure apart from writing the caller's buffer.
        /// </summary>
        public static void MoveTowards(FingerPose from, FingerPose to, float maxDegrees, FingerPose into)
        {
            for (int i = 0; i < into.Values.Length; i++)
            {
                into.Values[i] = Mathf.MoveTowards(from.Values[i], to.Values[i], maxDegrees);
            }
        }

        private static FingerPose CreateRelaxed()
        {
            var pose = new FingerPose();
            pose.SetBend(Finger.Thumb, 0, 5f);
            pose.SetBend(Finger.Thumb, 1, 10f);
            pose.SetBend(Finger.Thumb, 2, 10f);
            for (Finger finger = Finger.Index; finger <= Finger.Little; finger++)
            {
                pose.SetBend(finger, 0, 15f);
                pose.SetBend(finger, 1, 20f);
                pose.SetBend(finger, 2, 10f);
            }

            return pose;
        }
    }
}
