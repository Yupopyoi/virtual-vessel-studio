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

        // How close a limb may be turned to the camera's axis: past it, it would point the other way on the image.
        private const float AxisMarginDegrees = 5f;

        // How far a limb's end must be on the other side of a part's center before the limb changes the side it
        // is turned to. Without it, a hand behind the head at about the head's depth flipped between front and
        // back, and the arm jumped up to 90° in a frame on the recordings (2026-10-24).
        private const float SideMarginMeters = 0.05f;

        // Halvings of the turn: 60° / 2^12 is about 0.015°.
        private const int Halvings = 12;

        /// <summary>
        /// Writes <paramref name="pose"/> into <paramref name="into"/> with the arms turned out of the head
        /// and torso; <paramref name="into"/> may be <paramref name="pose"/>. Without a shape the pose is
        /// copied as it is. Returns the side each limb was turned to, for the next frame's
        /// <paramref name="previous"/>: a limb keeps its side until its end is clearly on the other one.
        /// </summary>
        /// <param name="neck">The neck's rotation in the root's space; unused when the model has no neck.</param>
        /// <param name="head">The head's rotation in the root's space.</param>
        public static ClearanceSides Apply(AvatarBodyShape shape, BodyTarget pose, Quaternion neck, Quaternion head, ClearanceSides previous, BodyTarget into)
        {
            into.CopyFrom(pose);
            if (shape == null)
            {
                return previous;
            }

            PlacedBody body = Place(shape, into, neck, head, out Chain top);
            (int leftUpper, int leftFore) = ClearArm(shape, body, top, ArmSide.Left, previous.LeftUpper, previous.LeftFore, into);
            (int rightUpper, int rightFore) = ClearArm(shape, body, top, ArmSide.Right, previous.RightUpper, previous.RightFore, into);
            return new ClearanceSides(leftUpper, leftFore, rightUpper, rightFore);
        }

        /// <summary>As the other, with no side remembered.</summary>
        public static void Apply(AvatarBodyShape shape, BodyTarget pose, Quaternion neck, Quaternion head, BodyTarget into)
        {
            Apply(shape, pose, neck, head, default, into);
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
            return Touches(shape, body, top, ArmSide.Left, pose) || Touches(shape, body, top, ArmSide.Right, pose);
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

        private static (int UpperSide, int ForeSide) ClearArm(AvatarBodyShape shape, PlacedBody body, Chain top, ArmSide side, int upperSide, int foreSide, BodyTarget into)
        {
            ArmShape thickness = shape.Arm(side.IsLeft);
            Limb arm = Reach(shape, top, side, into);

            // The upper arm turns alone and the forearm keeps where it points, so both point where they
            // did on the camera's image, and the forearm moves with the elbow in depth.
            var whole = new UpperArmTest(body, arm.Shoulder, arm.UpperArm, arm.Forearm, arm.Hand, thickness);
            Quaternion turn = Turn(whole, upperSide, out upperSide);
            into[side.UpperArm] = turn * into[side.UpperArm];

            var forearm = new ForearmTest(body, arm.Shoulder + (turn * arm.UpperArm), arm.Forearm, arm.Hand, thickness);
            into[side.LowerArm] = Turn(forearm, foreSide, out foreSide) * into[side.LowerArm];
            return (upperSide, foreSide);
        }

        private static bool Touches(AvatarBodyShape shape, PlacedBody body, Chain top, ArmSide side, BodyTarget pose)
        {
            ArmShape thickness = shape.Arm(side.IsLeft);
            Limb arm = Reach(shape, top, side, pose);
            return new UpperArmTest(body, arm.Shoulder, arm.UpperArm, arm.Forearm, arm.Hand, thickness).Hits(Quaternion.identity, out _)
                || new ForearmTest(body, arm.Shoulder + arm.UpperArm, arm.Forearm, arm.Hand, thickness).Hits(Quaternion.identity, out _);
        }

        /// <summary>Where an arm starts and the vectors along its upper arm, forearm, and hand.</summary>
        private static Limb Reach(AvatarBodyShape shape, Chain top, ArmSide side, BodyTarget pose)
        {
            Quaternion upper = pose[side.UpperArm];
            Quaternion lower = pose[side.LowerArm];
            Chain shoulder = top.Then(shape, side.ShoulderBone, pose[side.Shoulder]);
            Vector3 forearmRest = shape.Rest(side.HandBone) - shape.Rest(side.LowerArmBone);

            // The hand follows the forearm until it has its own rotation (step 6).
            return new Limb(
                shoulder.Then(shape, side.UpperArmBone, upper).At,
                upper * (shape.Rest(side.LowerArmBone) - shape.Rest(side.UpperArmBone)),
                lower * forearmRest,
                lower * (forearmRest.normalized * shape.Arm(side.IsLeft).HandLength));
        }

        /// <summary>
        /// The least turn about the test's pivot that takes the limb out of every part it reaches, within
        /// the plane of the limb and the camera's axis (+Z), so the limb points where it did on the camera's
        /// image and only its depth changes. It goes toward the side of the part the limb's end is on, kept
        /// from <paramref name="previous"/> (1 front, -1 back, 0 none yet) until the end is clearly on the
        /// other; to the other side only when that one cannot clear; and never across the camera's axis,
        /// where the limb would point the other way on the image. When neither side clears, the most toward
        /// the first. None when the limb is clear or points along the camera's axis.
        /// </summary>
        /// <remarks>
        /// The camera sees across and up well and depth poorly: tracking places a limb in front of the body
        /// too close to it and one behind it too close as well (tracking detailed design 8.4). So a limb
        /// tracked through the head or chest belongs further toward the side it is on. Turning by the least
        /// angle in any direction turned forearms folded in front of the face up across it, and ones in
        /// front of the chest down (the user's screen recording, 2026-10-24); choosing the side by the least
        /// angle flipped between front and back, and the arm jumped up to 69° in a frame on the recordings.
        /// </remarks>
        private static Quaternion Turn<T>(T test, int previous, out int side)
            where T : struct, ITurnTest
        {
            side = previous;
            if (!test.Hits(Quaternion.identity, out Vector3 center))
            {
                return Quaternion.identity;
            }

            Vector3 limb = test.Direction;
            Vector3 front = Vector3.ProjectOnPlane(Vector3.forward, limb);
            if (limb.sqrMagnitude < 1e-12f || front.sqrMagnitude < 1e-6f)
            {
                return Quaternion.identity;
            }

            float ahead = (test.Pivot + limb).z - center.z;
            side = previous != 0 && ahead * previous > -SideMarginMeters ? previous : (ahead >= 0f ? 1 : -1);

            // Turning forward about this axis brings the limb toward +Z, reaching it after the angle between them.
            Vector3 forward = Vector3.Cross(limb, front).normalized;
            bool inFront = side > 0;
            Vector3 first = inFront ? forward : -forward;
            float firstLimit = Mathf.Min(MaxTurnDegrees, Vector3.Angle(limb, inFront ? Vector3.forward : Vector3.back) - AxisMarginDegrees);
            float secondLimit = Mathf.Min(MaxTurnDegrees, Vector3.Angle(limb, inFront ? Vector3.back : Vector3.forward) - AxisMarginDegrees);
            float angle = firstLimit > 0f ? Clearing(test, first, firstLimit) : float.PositiveInfinity;
            if (!float.IsPositiveInfinity(angle))
            {
                return Quaternion.AngleAxis(angle, first);
            }

            angle = secondLimit > 0f ? Clearing(test, -first, secondLimit) : float.PositiveInfinity;
            if (!float.IsPositiveInfinity(angle))
            {
                // Remembered, so the limb stays on this side while the first cannot clear either.
                side = -side;
                return Quaternion.AngleAxis(angle, -first);
            }

            return Quaternion.AngleAxis(Mathf.Max(0f, firstLimit), first);
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

        /// <summary>
        /// The side, in depth, each limb was last turned to: 1 front, -1 back, 0 none yet. Kept by the caller
        /// from frame to frame (<see cref="Apply(AvatarBodyShape, BodyTarget, Quaternion, Quaternion, ClearanceSides, BodyTarget)"/>).
        /// </summary>
        public readonly struct ClearanceSides
        {
            public ClearanceSides(int leftUpper, int leftFore, int rightUpper, int rightFore)
            {
                LeftUpper = leftUpper;
                LeftFore = leftFore;
                RightUpper = rightUpper;
                RightFore = rightFore;
            }

            public int LeftUpper { get; }

            public int LeftFore { get; }

            public int RightUpper { get; }

            public int RightFore { get; }
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
        /// The upper arm about the shoulder, carrying the forearm and hand without turning them: the upper arm
        /// against the head, and the forearm and hand against the torso. A head the shoulder is already in is
        /// left out, as no turn about it would help.
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
                Vector3 wrist = elbow + _forearm;
                for (int i = 0; i < PlacedBody.Count; i++)
                {
                    PlacedPart part = _body[i];
                    bool hit = (_parts & (1 << i)) != 0
                        && (part.Round
                            ? part.Hits(Pivot, elbow, _thickness.UpperRadius)
                            : part.Hits(elbow, wrist, _thickness.LowerRadius) || part.Hits(wrist, wrist + _hand, _thickness.HandRadius));
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
