using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// The head as an ellipsoid on the head bone: its center from the bone and its half sizes, both along
    /// the avatar root's axes in the reference pose, so the head's rotation turns it.
    /// </summary>
    public readonly struct HeadShape
    {
        public HeadShape(Vector3 center, Vector3 radii)
        {
            Center = center;
            Radii = radii;
        }

        public Vector3 Center { get; }

        public Vector3 Radii { get; }
    }

    /// <summary>How thick an arm is, as the radii of the capsules around its bones, and how far the hand reaches past the wrist.</summary>
    public readonly struct ArmShape
    {
        public ArmShape(float upperRadius, float lowerRadius, float handLength, float handRadius)
        {
            UpperRadius = upperRadius;
            LowerRadius = lowerRadius;
            HandLength = handLength;
            HandRadius = handRadius;
        }

        public float UpperRadius { get; }

        public float LowerRadius { get; }

        public float HandLength { get; }

        public float HandRadius { get; }
    }

    /// <summary>
    /// A model's body in the reference pose, measured once when it loads (avatar detailed design 7.6): where
    /// its upper-body bones are, how big its head is, and how thick its arms are, so the arms can be kept
    /// out of the head (<see cref="ArmClearance"/>). Positions are in the avatar root's space, in meters.
    /// </summary>
    public sealed class AvatarBodyShape
    {
        // When an arm's mesh is not found, its thickness as a share of the upper arm's length.
        private const float FallbackUpperRadius = 0.18f;
        private const float FallbackLowerRadius = 0.15f;
        private const float FallbackHandLength = 0.70f;
        private const float FallbackHandRadius = 0.12f;

        // Shares of the points left out on each side, so ears and tufts that stick out do not set the head's
        // size: on the samples (2026-10-24) the top 5% of one head's points reached 20 cm above its crown.
        private const float Outliers = 0.05f;

        private readonly Vector3[] _rest;
        private readonly bool[] _has;
        private readonly ArmShape _left;
        private readonly ArmShape _right;

        /// <param name="rest">Per <see cref="AvatarBone"/>, where the bone is in the reference pose; read only where <paramref name="has"/> is true.</param>
        public AvatarBodyShape(Vector3[] rest, bool[] has, HeadShape head, ArmShape left, ArmShape right)
        {
            if (rest == null || rest.Length < AvatarBones.Count || has == null || has.Length < AvatarBones.Count)
            {
                throw new ArgumentException("One position and one flag per bone are needed.");
            }

            _rest = (Vector3[])rest.Clone();
            _has = (bool[])has.Clone();
            Head = head;
            _left = left;
            _right = right;
        }

        public HeadShape Head { get; }

        public bool Has(AvatarBone bone)
        {
            return _has[(int)bone];
        }

        public Vector3 Rest(AvatarBone bone)
        {
            return _rest[(int)bone];
        }

        public ArmShape Arm(bool left)
        {
            return left ? _left : _right;
        }

        /// <summary>
        /// The shape from the bones' rest positions and the mesh's points in the reference pose, grouped by
        /// the bone that moves them most: <see cref="AvatarBone.Head"/> (with the hair and ears on it), each
        /// upper arm, forearm, and hand (with its fingers). Null without the head, arms, or hands. Pure.
        /// </summary>
        public static AvatarBodyShape Measure(Vector3[] rest, bool[] has, IReadOnlyDictionary<AvatarBone, IReadOnlyList<Vector3>> points)
        {
            foreach (AvatarBone bone in AvatarBones.Required)
            {
                if (!has[(int)bone])
                {
                    return null;
                }
            }

            IReadOnlyList<Vector3> head = Points(points, AvatarBone.Head);
            if (head.Count == 0)
            {
                return null;
            }

            // Hair that hangs below the neck would make the head reach the chest; only the head itself counts.
            Vector3 headBone = rest[(int)AvatarBone.Head];
            float floor = has[(int)AvatarBone.Neck] ? rest[(int)AvatarBone.Neck].y : headBone.y;
            var xs = new List<float>(head.Count);
            var ys = new List<float>(head.Count);
            var zs = new List<float>(head.Count);
            foreach (Vector3 point in head)
            {
                if (point.y >= floor)
                {
                    xs.Add(point.x);
                    ys.Add(point.y);
                    zs.Add(point.z);
                }
            }

            if (xs.Count == 0)
            {
                return null;
            }

            (float minX, float maxX) = Range(xs);
            (float minY, float maxY) = Range(ys);
            (float minZ, float maxZ) = Range(zs);
            var shape = new HeadShape(
                new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f) - headBone,
                new Vector3((maxX - minX) * 0.5f, (maxY - minY) * 0.5f, (maxZ - minZ) * 0.5f));
            return new AvatarBodyShape(rest, has, shape, MeasureArm(rest, points, left: true), MeasureArm(rest, points, left: false));
        }

        private static ArmShape MeasureArm(Vector3[] rest, IReadOnlyDictionary<AvatarBone, IReadOnlyList<Vector3>> points, bool left)
        {
            AvatarBone upper = left ? AvatarBone.LeftUpperArm : AvatarBone.RightUpperArm;
            AvatarBone lower = left ? AvatarBone.LeftLowerArm : AvatarBone.RightLowerArm;
            AvatarBone hand = left ? AvatarBone.LeftHand : AvatarBone.RightHand;
            Vector3 shoulderAt = rest[(int)upper];
            Vector3 elbowAt = rest[(int)lower];
            Vector3 wristAt = rest[(int)hand];
            float length = Vector3.Distance(shoulderAt, elbowAt);

            float upperRadius = Thickness(Points(points, upper), shoulderAt, elbowAt);
            float lowerRadius = Thickness(Points(points, lower), elbowAt, wristAt);
            float handRadius = Thickness(Points(points, hand), wristAt, wristAt + (wristAt - elbowAt));
            float handLength = Reach(Points(points, hand), wristAt, (wristAt - elbowAt).normalized);
            return new ArmShape(
                upperRadius > 0f ? upperRadius : length * FallbackUpperRadius,
                lowerRadius > 0f ? lowerRadius : length * FallbackLowerRadius,
                handLength > 0f ? handLength : length * FallbackHandLength,
                handRadius > 0f ? handRadius : length * FallbackHandRadius);
        }

        /// <summary>
        /// How far most points lie from the line through a bone: the median distance of those along its
        /// middle, so the joints' bulges at either end do not count. 0 without points or a bone.
        /// </summary>
        public static float Thickness(IReadOnlyList<Vector3> points, Vector3 from, Vector3 to)
        {
            Vector3 axis = to - from;
            float length = axis.magnitude;
            if (length < 1e-6f)
            {
                return 0f;
            }

            axis /= length;
            var distances = new List<float>();
            foreach (Vector3 point in points)
            {
                float along = Vector3.Dot(point - from, axis) / length;
                if (along >= 0.2f && along <= 0.8f)
                {
                    distances.Add(Vector3.ProjectOnPlane(point - from, axis).magnitude);
                }
            }

            if (distances.Count == 0)
            {
                return 0f;
            }

            distances.Sort();
            return distances[distances.Count / 2];
        }

        /// <summary>How far the points reach past <paramref name="from"/> along <paramref name="direction"/>, leaving out the farthest few. 0 without points.</summary>
        public static float Reach(IReadOnlyList<Vector3> points, Vector3 from, Vector3 direction)
        {
            if (points.Count == 0)
            {
                return 0f;
            }

            var along = new List<float>(points.Count);
            foreach (Vector3 point in points)
            {
                along.Add(Vector3.Dot(point - from, direction));
            }

            return Math.Max(0f, Range(along).Max);
        }

        private static IReadOnlyList<Vector3> Points(IReadOnlyDictionary<AvatarBone, IReadOnlyList<Vector3>> points, AvatarBone bone)
        {
            return points != null && points.TryGetValue(bone, out IReadOnlyList<Vector3> found) && found != null ? found : Array.Empty<Vector3>();
        }

        /// <summary>The smallest and largest values once the outliers on each side are left out. Sorts the list.</summary>
        private static (float Min, float Max) Range(List<float> values)
        {
            values.Sort();
            int skip = (int)(values.Count * Outliers);
            return (values[skip], values[values.Count - 1 - skip]);
        }
    }
}
