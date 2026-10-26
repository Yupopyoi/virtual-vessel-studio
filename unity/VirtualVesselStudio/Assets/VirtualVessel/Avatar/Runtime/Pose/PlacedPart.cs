using UnityEngine;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// A part of the body where it is this frame: an ellipsoid (the head) or an elliptic cylinder upright
    /// along its own up (a part of the torso), with its center, rotation, and half sizes in the root's space.
    /// </summary>
    /// <remarks>
    /// A part of the torso is reached only across its front or through its middle, and by half a limb's
    /// thickness. An arm hanging at the side met the sides of a loose cardigan, measured with the torso, and
    /// was swung forward 30-60° (the user's screen recording, 2026-10-24); a sleeve against the body is cloth
    /// on cloth and looks right, and turning in depth could not take an arm out sideways anyway.
    /// </remarks>
    public readonly struct PlacedPart
    {
        /// <summary>The share of a limb's thickness a part of the torso is reached by.</summary>
        public const float TorsoContactShare = 0.5f;

        /// <summary>How near its axis, as a share of its size, a part of the torso is reached from any side.</summary>
        public const float TorsoCoreShare = 0.5f;

        private readonly Quaternion _inverse;

        private PlacedPart(bool round, Vector3 center, Quaternion rotation, Vector3 radii)
        {
            Exists = true;
            Round = round;
            Center = center;
            _inverse = Quaternion.Inverse(rotation);
            Radii = radii;
        }

        /// <summary>False for the default value: a part the model does not have.</summary>
        public bool Exists { get; }

        public bool Round { get; }

        public Vector3 Center { get; }

        public Vector3 Radii { get; }

        public static PlacedPart Ellipsoid(Vector3 center, Quaternion rotation, Vector3 radii)
        {
            return new PlacedPart(true, center, rotation, radii);
        }

        public static PlacedPart Cylinder(Vector3 center, Quaternion rotation, Vector3 halfSizes)
        {
            return new PlacedPart(false, center, rotation, halfSizes);
        }

        /// <summary>
        /// Whether a capsule from <paramref name="from"/> to <paramref name="to"/> of <paramref name="radius"/>
        /// reaches into the part. The part grown by the radius stands in for the capsule's rounded sides,
        /// in a space where it is the unit sphere or the unit cylinder from -1 to 1 high. Pure.
        /// </summary>
        public bool Hits(Vector3 from, Vector3 to, float radius)
        {
            if (!Exists)
            {
                return false;
            }

            float grow = Round ? radius : radius * TorsoContactShare;
            Vector3 a = ToUnit(from, grow);
            Vector3 along = ToUnit(to, grow) - a;
            return Round ? HitsSphere(a, along) : HitsCylinder(a, along);
        }

        private static bool HitsSphere(Vector3 a, Vector3 along)
        {
            float lengthSquared = along.sqrMagnitude;
            float t = lengthSquared > 1e-12f ? Mathf.Clamp01(-Vector3.Dot(a, along) / lengthSquared) : 0f;
            return (a + (along * t)).sqrMagnitude < 1f;
        }

        private static bool HitsCylinder(Vector3 a, Vector3 along)
        {
            // The part of the segment within the cylinder's height, then its nearest point to the axis.
            float start = 0f;
            float end = 1f;
            if (Mathf.Abs(along.y) < 1e-9f)
            {
                if (Mathf.Abs(a.y) >= 1f)
                {
                    return false;
                }
            }
            else
            {
                float t0 = (-1f - a.y) / along.y;
                float t1 = (1f - a.y) / along.y;
                start = Mathf.Max(start, Mathf.Min(t0, t1));
                end = Mathf.Min(end, Mathf.Max(t0, t1));
                if (start > end)
                {
                    return false;
                }
            }

            var flatA = new Vector2(a.x, a.z);
            var flatAlong = new Vector2(along.x, along.z);
            float lengthSquared = flatAlong.sqrMagnitude;
            float t = lengthSquared > 1e-12f ? Mathf.Clamp(-Vector2.Dot(flatA, flatAlong) / lengthSquared, start, end) : start;
            Vector2 nearest = flatA + (flatAlong * t);

            // Inside, and through the middle or across the front (+Z), not at the sides or the back.
            return nearest.sqrMagnitude < 1f
                && (nearest.sqrMagnitude < TorsoCoreShare * TorsoCoreShare || nearest.y > Mathf.Abs(nearest.x));
        }

        private Vector3 ToUnit(Vector3 point, float grow)
        {
            Vector3 local = _inverse * (point - Center);
            return new Vector3(local.x / (Radii.x + grow), local.y / (Radii.y + grow), local.z / (Radii.z + grow));
        }
    }
}
