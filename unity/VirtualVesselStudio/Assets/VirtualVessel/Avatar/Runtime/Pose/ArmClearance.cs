using UnityEngine;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// Keeps the avatar's arms out of its head and torso (avatar detailed design 7.6). Only directions are
    /// retargeted, so on an avatar built differently from the person, an arm the person holds beside the
    /// head or across the chest passes through the avatar's. An arm that would is turned the least that
    /// clears it: first the whole arm about the shoulder, when the upper arm reaches the head or any of the
    /// arm below the elbow sinks into the torso, so folded arms come forward as a whole; then the forearm
    /// about the elbow, when the forearm or the hand still reaches either, so a forearm over the head
    /// bends up. Of the turns away from the part's center, up, down, and to either side, the smallest
    /// wins. Pure: only the caller's buffer is written, and nothing is allocated (CLAUDE.md 23).
    /// </summary>
    /// <remarks>
    /// The upper arm itself is not kept out of the torso: it starts at the torso's edge, where the two
    /// always meet. Bending the elbow alone to take folded arms out of the chest would swing the forearm
    /// 50-60° on the sample models (2026-10-24), as the tracked elbow is already in the chest.
    /// </remarks>
    public static class ArmClearance
    {
        /// <summary>The most an arm is turned; past it the arm stays where that turn puts it, so it never jumps.</summary>
        public const float MaxTurnDegrees = 60f;

        // Halvings of the turn: 60° / 2^12 is about 0.015°.
        private const int Halvings = 12;

        /// <summary>
        /// Writes <paramref name="pose"/> into <paramref name="into"/> with the arms turned out of the head
        /// and torso; <paramref name="into"/> may be <paramref name="pose"/>. Without a shape the pose is
        /// copied as it is.
        /// </summary>
        /// <param name="neck">The neck's rotation in the root's space; unused when the model has no neck.</param>
        /// <param name="head">The head's rotation in the root's space.</param>
        public static void Apply(AvatarBodyShape shape, BodyTarget pose, Quaternion neck, Quaternion head, BodyTarget into)
        {
            into.CopyFrom(pose);
            if (shape == null)
            {
                return;
            }

            PlacedBody body = Place(shape, into, neck, head, out Chain top);
            ClearArm(shape, body, top, left: true, into);
            ClearArm(shape, body, top, left: false, into);
        }

        /// <summary>
        /// Whether either arm of <paramref name="pose"/> reaches into the head or torso where
        /// <see cref="Apply"/> would turn it; false without a shape. Pure.
        /// </summary>
        public static bool Touches(AvatarBodyShape shape, BodyTarget pose, Quaternion neck, Quaternion head)
        {
            if (shape == null)
            {
                return false;
            }

            PlacedBody body = Place(shape, pose, neck, head, out Chain top);
            return Touches(shape, body, top, left: true, pose) || Touches(shape, body, top, left: false, pose);
        }

        /// <summary>
        /// The head and torso where <paramref name="pose"/> puts them, and the top of the torso the shoulders
        /// hang from: forward along the chain from the hips, which do not move, each bone where its parent's
        /// rotation carries the rest offset between them.
        /// </summary>
        private static PlacedBody Place(AvatarBodyShape shape, BodyTarget pose, Quaternion neck, Quaternion head, out Chain top)
        {
            var chain = new Chain(shape.Rest(AvatarBone.Hips));
            chain = chain.Then(shape, AvatarBone.Spine, pose[UpperBodyBone.Spine]);
            PlacedPart spine = PlaceTorso(shape, 0, chain);
            chain = chain.Then(shape, AvatarBone.Chest, pose[UpperBodyBone.Chest]);
            PlacedPart chest = PlaceTorso(shape, 1, chain);
            chain = chain.Then(shape, AvatarBone.UpperChest, pose[UpperBodyBone.UpperChest]);
            PlacedPart upperChest = PlaceTorso(shape, 2, chain);
            top = chain;
            chain = chain.Then(shape, AvatarBone.Neck, neck);
            chain = chain.Then(shape, AvatarBone.Head, head);
            PartShape headShape = shape.Head;
            var placedHead = PlacedPart.Ellipsoid(chain.At + (head * headShape.Center), head, headShape.Radii);
            return new PlacedBody(placedHead, spine, chest, upperChest);
        }

        private static PlacedPart PlaceTorso(AvatarBodyShape shape, int index, Chain at)
        {
            // Only on the bone it was measured on; a missing bone's chain stops at its parent.
            AvatarBone bone = AvatarBodyShape.TorsoBones[index];
            return shape.Has(bone) && shape.Torso(index) is PartShape part
                ? PlacedPart.Cylinder(at.At + (at.Rotation * part.Center), at.Rotation, part.Radii)
                : default;
        }

        private static void ClearArm(AvatarBodyShape shape, PlacedBody body, Chain top, bool left, BodyTarget into)
        {
            UpperBodyBone upper = left ? UpperBodyBone.LeftUpperArm : UpperBodyBone.RightUpperArm;
            UpperBodyBone lower = left ? UpperBodyBone.LeftLowerArm : UpperBodyBone.RightLowerArm;
            ArmShape thickness = shape.Arm(left);
            Limb arm = Reach(shape, top, left, into);

            var whole = new UpperArmTest(body, arm.Shoulder, arm.UpperArm, arm.Forearm, arm.Hand, thickness);
            Quaternion turn = Turn(whole);
            into[upper] = turn * into[upper];
            into[lower] = turn * into[lower];

            var forearm = new ForearmTest(body, arm.Shoulder + (turn * arm.UpperArm), turn * arm.Forearm, turn * arm.Hand, thickness);
            into[lower] = Turn(forearm) * into[lower];
        }

        private static bool Touches(AvatarBodyShape shape, PlacedBody body, Chain top, bool left, BodyTarget pose)
        {
            ArmShape thickness = shape.Arm(left);
            Limb arm = Reach(shape, top, left, pose);
            return new UpperArmTest(body, arm.Shoulder, arm.UpperArm, arm.Forearm, arm.Hand, thickness).Hits(Quaternion.identity, out _)
                || new ForearmTest(body, arm.Shoulder + arm.UpperArm, arm.Forearm, arm.Hand, thickness).Hits(Quaternion.identity, out _);
        }

        /// <summary>Where an arm starts and the vectors along its upper arm, forearm, and hand.</summary>
        private static Limb Reach(AvatarBodyShape shape, Chain top, bool left, BodyTarget pose)
        {
            AvatarBone upperBone = left ? AvatarBone.LeftUpperArm : AvatarBone.RightUpperArm;
            AvatarBone lowerBone = left ? AvatarBone.LeftLowerArm : AvatarBone.RightLowerArm;
            AvatarBone handBone = left ? AvatarBone.LeftHand : AvatarBone.RightHand;
            Quaternion upper = pose[left ? UpperBodyBone.LeftUpperArm : UpperBodyBone.RightUpperArm];
            Quaternion lower = pose[left ? UpperBodyBone.LeftLowerArm : UpperBodyBone.RightLowerArm];
            Chain shoulder = top.Then(shape, left ? AvatarBone.LeftShoulder : AvatarBone.RightShoulder, pose[left ? UpperBodyBone.LeftShoulder : UpperBodyBone.RightShoulder]);
            Vector3 forearmRest = shape.Rest(handBone) - shape.Rest(lowerBone);

            // The hand follows the forearm until it has its own rotation (step 6).
            return new Limb(
                shoulder.Then(shape, upperBone, upper).At,
                upper * (shape.Rest(lowerBone) - shape.Rest(upperBone)),
                lower * forearmRest,
                lower * (forearmRest.normalized * shape.Arm(left).HandLength));
        }

        /// <summary>
        /// The least turn about the test's pivot that takes the limb out of every part it reaches. It tries
        /// turning away from the center of the part reached first, then up, down, and to either side, and
        /// keeps the smallest turn that clears; away from the center wins a tie. When none clears within
        /// <see cref="MaxTurnDegrees"/>, the most away from the center. None when the limb is clear.
        /// </summary>
        /// <remarks>
        /// Away from the center alone is not enough: a limb pointing nearly through the center has no clear
        /// away, and turning it down across the face may never clear where turning it up would at once.
        /// </remarks>
        private static Quaternion Turn<T>(T test)
            where T : struct, ITurnTest
        {
            if (!test.Hits(Quaternion.identity, out Vector3 center))
            {
                return Quaternion.identity;
            }

            Vector3 direction = test.Direction;
            if (direction.sqrMagnitude < 1e-12f)
            {
                return Quaternion.identity;
            }

            direction.Normalize();
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, direction);
            if (up.sqrMagnitude < 1e-6f)
            {
                up = Vector3.ProjectOnPlane(Vector3.forward, direction);
            }

            up.Normalize();
            Vector3 side = Vector3.Cross(direction, up);
            Vector3 away = -Vector3.ProjectOnPlane(center - test.Pivot, direction);
            Vector3 awayAxis = away.sqrMagnitude > 1e-10f ? Vector3.Cross(direction, away.normalized) : Vector3.Cross(direction, up);

            float best = Clearing(test, awayAxis, MaxTurnDegrees);
            Vector3 bestAxis = awayAxis;
            Consider(test, Vector3.Cross(direction, up), ref best, ref bestAxis);
            Consider(test, Vector3.Cross(direction, -up), ref best, ref bestAxis);
            Consider(test, Vector3.Cross(direction, side), ref best, ref bestAxis);
            Consider(test, Vector3.Cross(direction, -side), ref best, ref bestAxis);
            return Quaternion.AngleAxis(float.IsPositiveInfinity(best) ? MaxTurnDegrees : best, float.IsPositiveInfinity(best) ? awayAxis : bestAxis);
        }

        private static void Consider<T>(T test, Vector3 axis, ref float best, ref Vector3 bestAxis)
            where T : struct, ITurnTest
        {
            // Only a turn smaller than the best so far can win, so one check at that size rules most out.
            float angle = Clearing(test, axis, float.IsPositiveInfinity(best) ? MaxTurnDegrees : best);
            if (angle < best)
            {
                best = angle;
                bestAxis = axis;
            }
        }

        /// <summary>
        /// The least turn about <paramref name="axis"/> (a unit vector) up to <paramref name="limit"/> that
        /// clears, by halving; infinity when even the limit does not.
        /// </summary>
        private static float Clearing<T>(T test, Vector3 axis, float limit)
            where T : struct, ITurnTest
        {
            if (test.Hits(Quaternion.AngleAxis(limit, axis), out _))
            {
                return float.PositiveInfinity;
            }

            float hitting = 0f;
            float clear = limit;
            for (int i = 0; i < Halvings; i++)
            {
                float middle = (hitting + clear) * 0.5f;
                if (test.Hits(Quaternion.AngleAxis(middle, axis), out _))
                {
                    hitting = middle;
                }
                else
                {
                    clear = middle;
                }
            }

            return clear;
        }

        /// <summary>A limb turned about a pivot, and whether it then reaches into a part of the body.</summary>
        private interface ITurnTest
        {
            Vector3 Pivot { get; }

            /// <summary>Where the limb points from the pivot.</summary>
            Vector3 Direction { get; }

            /// <summary>Whether the limb turned by <paramref name="turn"/> reaches into a part, and that part's center.</summary>
            bool Hits(Quaternion turn, out Vector3 center);
        }

        /// <summary>
        /// The whole arm about the shoulder: the upper arm against the head, and the forearm and hand against
        /// the torso. A head the shoulder is already in is left out, as no turn about it would help.
        /// </summary>
        private readonly struct UpperArmTest : ITurnTest
        {
            private readonly PlacedBody _body;
            private readonly Vector3 _upperArm;
            private readonly Vector3 _forearm;
            private readonly Vector3 _hand;
            private readonly ArmShape _thickness;
            private readonly int _parts;

            public UpperArmTest(PlacedBody body, Vector3 shoulder, Vector3 upperArm, Vector3 forearm, Vector3 hand, ArmShape thickness)
            {
                _body = body;
                Pivot = shoulder;
                _upperArm = upperArm;
                _forearm = forearm;
                _hand = hand;
                _thickness = thickness;
                _parts = 0;
                for (int i = 0; i < PlacedBody.Count; i++)
                {
                    PlacedPart part = body[i];
                    if (part.Exists && !(part.Round && part.Hits(shoulder, shoulder, thickness.UpperRadius)))
                    {
                        _parts |= 1 << i;
                    }
                }
            }

            public Vector3 Pivot { get; }

            public Vector3 Direction => _upperArm;

            public bool Hits(Quaternion turn, out Vector3 center)
            {
                Vector3 elbow = Pivot + (turn * _upperArm);
                Vector3 wrist = elbow + (turn * _forearm);
                for (int i = 0; i < PlacedBody.Count; i++)
                {
                    PlacedPart part = _body[i];
                    bool hit = (_parts & (1 << i)) != 0
                        && (part.Round
                            ? part.Hits(Pivot, elbow, _thickness.UpperRadius)
                            : part.Hits(elbow, wrist, _thickness.LowerRadius) || part.Hits(wrist, wrist + (turn * _hand), _thickness.HandRadius));
                    if (hit)
                    {
                        center = part.Center;
                        return true;
                    }
                }

                center = default;
                return false;
            }
        }

        /// <summary>The forearm and hand about the elbow, against the head and torso. A part the elbow is in is left out.</summary>
        private readonly struct ForearmTest : ITurnTest
        {
            private readonly PlacedBody _body;
            private readonly Vector3 _forearm;
            private readonly Vector3 _hand;
            private readonly ArmShape _thickness;
            private readonly int _parts;

            public ForearmTest(PlacedBody body, Vector3 elbow, Vector3 forearm, Vector3 hand, ArmShape thickness)
            {
                _body = body;
                Pivot = elbow;
                _forearm = forearm;
                _hand = hand;
                _thickness = thickness;
                _parts = 0;
                for (int i = 0; i < PlacedBody.Count; i++)
                {
                    PlacedPart part = body[i];
                    if (part.Exists && !part.Hits(elbow, elbow, thickness.LowerRadius))
                    {
                        _parts |= 1 << i;
                    }
                }
            }

            public Vector3 Pivot { get; }

            public Vector3 Direction => _forearm;

            public bool Hits(Quaternion turn, out Vector3 center)
            {
                Vector3 wrist = Pivot + (turn * _forearm);
                Vector3 tip = wrist + (turn * _hand);
                for (int i = 0; i < PlacedBody.Count; i++)
                {
                    PlacedPart part = _body[i];
                    if ((_parts & (1 << i)) != 0 && (part.Hits(Pivot, wrist, _thickness.LowerRadius) || part.Hits(wrist, tip, _thickness.HandRadius)))
                    {
                        center = part.Center;
                        return true;
                    }
                }

                center = default;
                return false;
            }
        }

        /// <summary>The head and the torso's parts this frame.</summary>
        private readonly struct PlacedBody
        {
            public const int Count = 4;

            private readonly PlacedPart _head;
            private readonly PlacedPart _spine;
            private readonly PlacedPart _chest;
            private readonly PlacedPart _upperChest;

            public PlacedBody(PlacedPart head, PlacedPart spine, PlacedPart chest, PlacedPart upperChest)
            {
                _head = head;
                _spine = spine;
                _chest = chest;
                _upperChest = upperChest;
            }

            public PlacedPart this[int index]
            {
                get
                {
                    switch (index)
                    {
                        case 0: return _head;
                        case 1: return _spine;
                        case 2: return _chest;
                        default: return _upperChest;
                    }
                }
            }
        }

        /// <summary>
        /// A part of the body where it is this frame: an ellipsoid (the head) or an elliptic cylinder upright
        /// along its own up (a part of the torso), with its center, rotation, and half sizes in the root's space.
        /// </summary>
        public readonly struct PlacedPart
        {
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

                Vector3 a = ToUnit(from, radius);
                Vector3 along = ToUnit(to, radius) - a;
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
                return (flatA + (flatAlong * t)).sqrMagnitude < 1f;
            }

            private Vector3 ToUnit(Vector3 point, float grow)
            {
                Vector3 local = _inverse * (point - Center);
                return new Vector3(local.x / (Radii.x + grow), local.y / (Radii.y + grow), local.z / (Radii.z + grow));
            }
        }

        private readonly struct Limb
        {
            public Limb(Vector3 shoulder, Vector3 upperArm, Vector3 forearm, Vector3 hand)
            {
                Shoulder = shoulder;
                UpperArm = upperArm;
                Forearm = forearm;
                Hand = hand;
            }

            public Vector3 Shoulder { get; }

            public Vector3 UpperArm { get; }

            public Vector3 Forearm { get; }

            public Vector3 Hand { get; }
        }

        /// <summary>Where the chain has reached: a bone's position, its rest position, and its rotation.</summary>
        private readonly struct Chain
        {
            private readonly Vector3 _rest;

            public Chain(Vector3 hips)
                : this(hips, hips, Quaternion.identity)
            {
            }

            private Chain(Vector3 at, Vector3 rest, Quaternion rotation)
            {
                At = at;
                _rest = rest;
                Rotation = rotation;
            }

            public Vector3 At { get; }

            public Quaternion Rotation { get; }

            /// <summary>The next bone on <paramref name="rotation"/>; the same place when the model lacks it.</summary>
            public Chain Then(AvatarBodyShape shape, AvatarBone bone, Quaternion rotation)
            {
                if (!shape.Has(bone))
                {
                    return this;
                }

                Vector3 rest = shape.Rest(bone);
                return new Chain(At + (Rotation * (rest - _rest)), rest, rotation);
            }
        }
    }
}
