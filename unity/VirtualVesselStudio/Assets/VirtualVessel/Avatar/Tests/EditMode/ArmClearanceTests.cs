using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Tracking;
using VirtualVessel.Tracking.Recording;

namespace VirtualVessel.Avatar.Tests
{
    /// <summary>Keeping the arms out of the head and torso, and measuring a model's body for it (avatar detailed design 7.6).</summary>
    public sealed class ArmClearanceTests
    {
        // An avatar with a big head for its arms, as anime models have: the head reaches 0.13 m to each side
        // of its center, the upper arm starts 0.1 m from the middle.
        private static readonly Vector3 s_headCenter = new Vector3(0f, 0.12f, 0f);
        private static readonly Vector3 s_headRadii = new Vector3(0.13f, 0.15f, 0.13f);

        // The torso's parts on Spine, Chest, and UpperChest: 0.11-0.13 m to each side, 0.08-0.09 m to the front.
        private static readonly PartShape?[] s_torso =
        {
            new PartShape(Vector3.zero, new Vector3(0.11f, 0.06f, 0.08f)),
            new PartShape(new Vector3(0f, 0.05f, 0f), new Vector3(0.12f, 0.08f, 0.09f)),
            new PartShape(new Vector3(0f, 0.04f, 0f), new Vector3(0.13f, 0.06f, 0.09f)),
        };

        [Test]
        public void AForearmAcrossTheFace_TurnsOutAtTheElbow_ToTheSideItWasOn()
        {
            AvatarBodyShape shape = Shape(s_headRadii);
            foreach (float side in new[] { 1f, -1f })
            {
                // The right upper arm raised beside the head, the forearm across it a little in front or behind.
                BodyTarget pose = Pose(new Vector3(0.5f, 0.866f, 0f), new Vector3(-1f, 0f, 0.15f * side));
                Assert.That(ArmClearance.Touches(shape, pose, Quaternion.identity, Quaternion.identity), Is.True, "Through the head as tracked.");

                var cleared = new BodyTarget();
                ArmClearance.Apply(shape, pose, Quaternion.identity, Quaternion.identity, cleared);

                Assert.That(ArmClearance.Touches(shape, cleared, Quaternion.identity, Quaternion.identity), Is.False, "Out of the head.");
                AssertSame(cleared[UpperBodyBone.RightUpperArm], pose[UpperBodyBone.RightUpperArm], "The upper arm was clear, so it stays.");
                float turned = Quaternion.Angle(cleared[UpperBodyBone.RightLowerArm], pose[UpperBodyBone.RightLowerArm]);
                Assert.That(turned, Is.GreaterThan(1f).And.LessThan(ArmClearance.MaxTurnDegrees), "The least turn that clears, not the most.");
                Vector3 forearm = cleared[UpperBodyBone.RightLowerArm] * Vector3.right;
                Assert.That(Mathf.Sign(forearm.z), Is.EqualTo(side), "In front stays in front, behind stays behind.");
                AssertSame(cleared[UpperBodyBone.LeftUpperArm], pose[UpperBodyBone.LeftUpperArm], "The other arm is untouched.");
            }
        }

        [Test]
        public void AnUpperArmIntoTheHead_TurnsTheWholeArmOutAtTheShoulder()
        {
            AvatarBodyShape shape = Shape(s_headRadii);
            Vector3 inward = new Vector3(-0.2f, 1f, 0f).normalized;
            BodyTarget pose = Pose(inward, inward);
            var cleared = new BodyTarget();

            ArmClearance.Apply(shape, pose, Quaternion.identity, Quaternion.identity, cleared);

            Assert.That(ArmClearance.Touches(shape, cleared, Quaternion.identity, Quaternion.identity), Is.False);
            Vector3 upper = cleared[UpperBodyBone.RightUpperArm] * Vector3.right;
            Assert.That(upper.x, Is.GreaterThan(inward.x), "Turned away from the head, outward.");
        }

        [Test]
        public void FoldedArms_ComeForwardAsAWhole_KeepingTheElbowsBend()
        {
            // Arms folded: the right upper arm down and a little forward, the forearm across the chest, in it.
            AvatarBodyShape shape = Shape(s_headRadii);
            BodyTarget pose = Pose(new Vector3(0f, -0.9f, 0.44f), Vector3.left);
            Assert.That(ArmClearance.Touches(shape, pose, Quaternion.identity, Quaternion.identity), Is.True, "Through the chest as tracked.");
            var cleared = new BodyTarget();

            ArmClearance.Apply(shape, pose, Quaternion.identity, Quaternion.identity, cleared);

            Assert.That(ArmClearance.Touches(shape, cleared, Quaternion.identity, Quaternion.identity), Is.False);
            Assert.That((cleared[UpperBodyBone.RightUpperArm] * Vector3.right).z, Is.GreaterThan((pose[UpperBodyBone.RightUpperArm] * Vector3.right).z), "The arm comes forward.");
            Assert.That(Bend(cleared), Is.EqualTo(Bend(pose)).Within(0.1f), "Turned at the shoulder, so the elbow bends as tracked.");
        }

        [Test]
        public void AnElbowSunkIntoTheChest_TurnsTheWholeArmOut()
        {
            AvatarBodyShape shape = Shape(s_headRadii);
            BodyTarget pose = Pose(new Vector3(-0.2f, -0.6f, 0.3f), Vector3.left);
            var cleared = new BodyTarget();

            ArmClearance.Apply(shape, pose, Quaternion.identity, Quaternion.identity, cleared);

            Assert.That(ArmClearance.Touches(shape, cleared, Quaternion.identity, Quaternion.identity), Is.False);
            Assert.That(Quaternion.Angle(cleared[UpperBodyBone.RightUpperArm], pose[UpperBodyBone.RightUpperArm]), Is.GreaterThan(1f), "Turned at the shoulder.");
        }

        [Test]
        public void ArmsClearOfTheHead_OrNoShape_AreLeftAsTheyAre()
        {
            BodyTarget pose = Pose(Vector3.right, Vector3.up);
            pose[UpperBodyBone.LeftUpperArm] = AvatarRestPose.UpperArm(true, AvatarRestPose.DefaultArmDownDegrees);
            pose[UpperBodyBone.LeftLowerArm] = pose[UpperBodyBone.LeftUpperArm];
            var cleared = new BodyTarget();

            // The left arm hangs at rest beside the torso without reaching it.
            ArmClearance.Apply(Shape(s_headRadii), pose, Quaternion.identity, Quaternion.identity, cleared);
            for (int i = 0; i < BodyTarget.BoneCount; i++)
            {
                AssertSame(cleared.Rotations[i], pose.Rotations[i], ((UpperBodyBone)i).ToString());
            }

            ArmClearance.Apply(null, Pose(Vector3.up, Vector3.left), Quaternion.identity, Quaternion.identity, cleared);
            AssertSame(cleared[UpperBodyBone.RightLowerArm], Quaternion.FromToRotation(Vector3.right, Vector3.left));
            Assert.That(ArmClearance.Touches(null, cleared, Quaternion.identity, Quaternion.identity), Is.False);
        }

        [Test]
        public void AShoulderInsideTheHead_IsNotTurned_SinceNoTurnWouldHelp()
        {
            // A head so big the shoulders are in it: turning would only swing the arm the most every frame.
            AvatarBodyShape shape = Shape(new Vector3(0.6f, 0.6f, 0.6f));
            BodyTarget pose = Pose(new Vector3(0.5f, 0.866f, 0f), Vector3.left);
            var cleared = new BodyTarget();

            ArmClearance.Apply(shape, pose, Quaternion.identity, Quaternion.identity, cleared);

            AssertSame(cleared[UpperBodyBone.RightUpperArm], pose[UpperBodyBone.RightUpperArm]);
            AssertSame(cleared[UpperBodyBone.RightLowerArm], pose[UpperBodyBone.RightLowerArm]);
        }

        [Test]
        public void TheHeadsTurn_CarriesItsShape()
        {
            // The forearm across in front of the face, 0.2 m ahead of the head bone: clear of an upright
            // head, but bowing the head 40° brings the face onto it.
            AvatarBodyShape shape = Shape(s_headRadii);
            BodyTarget pose = Pose(new Vector3(0.054f, 0.14f, 0.2f), Vector3.left);
            Quaternion bowed = Quaternion.AngleAxis(40f, Vector3.right);

            Assert.That(ArmClearance.Touches(shape, pose, Quaternion.identity, Quaternion.identity), Is.False);
            Assert.That(ArmClearance.Touches(shape, pose, Quaternion.identity, bowed), Is.True);
        }

        [Test]
        public void TheController_ShowsAndSetsTheClearedArms()
        {
            AvatarBodyShape shape = Shape(s_headRadii);
            var model = new AvatarFaceTests.RecordingModel { BodyShape = shape };
            var controller = new AvatarController(model, ExpressionMapping.Default);
            var frame = new TrackingFrame();
            SyntheticMotion.WriteArms(frame.Body, Vector3.down, Vector3.down, new Vector3(0.5f, 0.866f, 0f), Vector3.left, 1.0);

            controller.Update(frame, mirror: false, now: 1.0);
            controller.Update(frame, mirror: false, now: 1.5);

            var tracked = new BodyTarget();
            PoseRetargeting.Retarget(frame.Body, false, true, AvatarRestPose.DefaultArmDownDegrees, tracked);
            Quaternion neck = model.Rotations[AvatarBone.Neck];
            Quaternion head = model.Rotations[AvatarBone.Head];
            Assert.That(ArmClearance.Touches(shape, tracked, neck, head), Is.True, "Tracked, the forearm crosses the head.");
            Assert.That(ArmClearance.Touches(shape, controller.Body, neck, head), Is.False, "Shown, it does not.");
            AssertSame(model.Rotations[AvatarBone.RightLowerArm], controller.Body[UpperBodyBone.RightLowerArm], "What is shown is what is set.");
        }

        [Test]
        public void TheShape_IsMeasuredFromTheMesh_LeavingOutHairBelowTheNeck()
        {
            (Vector3[] rest, bool[] has) = Skeleton();
            var head = new List<Vector3>();
            Vector3 center = rest[(int)AvatarBone.Head] + new Vector3(0f, 0.1f, 0.01f);
            var random = new System.Random(7);
            for (int i = 0; i < 4000; i++)
            {
                // Points spread evenly over an ellipsoid 0.12 × 0.14 × 0.11, and long hair hanging to the waist behind.
                float z = (float)((random.NextDouble() * 2.0) - 1.0);
                float around = (float)(random.NextDouble() * 2.0 * Mathf.PI);
                float ring = Mathf.Sqrt(1f - (z * z));
                var direction = new Vector3(ring * Mathf.Cos(around), ring * Mathf.Sin(around), z);
                head.Add(center + Vector3.Scale(direction, new Vector3(0.12f, 0.14f, 0.11f)));
                head.Add(new Vector3(direction.x * 0.1f, 0.9f + (0.4f * Mathf.Abs(direction.y)), -0.1f));
            }

            Vector3 shoulder = rest[(int)AvatarBone.RightUpperArm];
            Vector3 elbow = rest[(int)AvatarBone.RightLowerArm];
            var upperArm = new List<Vector3>();
            for (int i = 0; i <= 100; i++)
            {
                float angle = i * 0.1f * Mathf.PI;
                upperArm.Add(Vector3.Lerp(shoulder, elbow, i / 100f) + new Vector3(0f, 0.05f * Mathf.Cos(angle), 0.05f * Mathf.Sin(angle)));
            }

            Vector3 wrist = rest[(int)AvatarBone.RightHand];
            var hand = new List<Vector3> { wrist, wrist + new Vector3(0.18f, 0f, 0f), wrist + new Vector3(0.1f, 0.02f, 0f) };
            var chest = new List<Vector3>();
            for (int i = 0; i < 400; i++)
            {
                // A box of points 0.24 wide, 0.16 high, and 0.18 deep, centered 0.05 above the chest bone.
                chest.Add(rest[(int)AvatarBone.Chest] + new Vector3(((i % 20) - 9.5f) * 0.012632f, 0.05f + (((i / 20) - 9.5f) * 0.008421f), (i % 2 == 0 ? 0.09f : -0.09f)));
            }

            AvatarBodyShape shape = AvatarBodyShape.Measure(rest, has, new Dictionary<AvatarBone, IReadOnlyList<Vector3>>
            {
                [AvatarBone.Head] = head,
                [AvatarBone.RightUpperArm] = upperArm,
                [AvatarBone.RightHand] = hand,
                [AvatarBone.Chest] = chest,
                [AvatarBone.Spine] = new List<Vector3> { rest[(int)AvatarBone.Spine] },
            });

            Assert.That(shape, Is.Not.Null);
            Assert.That(Vector3.Distance(shape.Head.Center, center - rest[(int)AvatarBone.Head]), Is.LessThan(0.01f), "The hair below the neck is left out.");
            // Evenly over a sphere, each coordinate is even between its ends, so leaving out the outermost
            // 5% on each side leaves 90% of the size.
            Assert.That(Vector3.Distance(shape.Head.Radii, new Vector3(0.12f, 0.14f, 0.11f) * 0.9f), Is.LessThan(0.005f));
            Assert.That(shape.Arm(false).UpperRadius, Is.EqualTo(0.05f).Within(0.001f));
            Assert.That(shape.Arm(false).HandLength, Is.EqualTo(0.18f).Within(0.001f));
            Assert.That(shape.Arm(true).UpperRadius, Is.GreaterThan(0f), "Without its mesh an arm gets a thickness from its length.");
            Assert.That(shape.Torso(1).HasValue, Is.True);
            Assert.That(Vector3.Distance(shape.Torso(1).Value.Center, new Vector3(0f, 0.05f, 0f)), Is.LessThan(0.005f), "From the chest bone.");
            Assert.That(shape.Torso(1).Value.Radii.z, Is.EqualTo(0.09f).Within(0.001f));
            Assert.That(shape.Torso(0).HasValue, Is.False, "Too few points to say the size.");
            Assert.That(shape.Torso(2).HasValue, Is.False, "No points.");
            Assert.That(AvatarBodyShape.Measure(rest, has, new Dictionary<AvatarBone, IReadOnlyList<Vector3>>()), Is.Null, "No head, nothing to keep out of.");
        }

        /// <summary>The right arm's upper arm and forearm pointing the given ways; the left one out sideways.</summary>
        private static BodyTarget Pose(Vector3 upper, Vector3 fore)
        {
            var pose = new BodyTarget();
            pose[UpperBodyBone.RightUpperArm] = Quaternion.FromToRotation(Vector3.right, upper.normalized);
            pose[UpperBodyBone.RightLowerArm] = Quaternion.FromToRotation(Vector3.right, fore.normalized);
            return pose;
        }

        private static AvatarBodyShape Shape(Vector3 headRadii)
        {
            (Vector3[] rest, bool[] has) = Skeleton();
            var arm = new ArmShape(0.045f, 0.04f, 0.15f, 0.03f);
            return new AvatarBodyShape(rest, has, new PartShape(s_headCenter, headRadii), arm, arm, s_torso);
        }

        /// <summary>The reference pose of a small avatar: hips at 1 m, the head bone at 1.5 m, arms out along X.</summary>
        private static (Vector3[] Positions, bool[] Has) Skeleton()
        {
            var rest = new Vector3[AvatarBones.Count];
            var has = new bool[AvatarBones.Count];
            void Bone(AvatarBone bone, float x, float y)
            {
                rest[(int)bone] = new Vector3(x, y, 0f);
                has[(int)bone] = true;
            }

            Bone(AvatarBone.Hips, 0f, 1f);
            Bone(AvatarBone.Spine, 0f, 1.1f);
            Bone(AvatarBone.Chest, 0f, 1.2f);
            Bone(AvatarBone.UpperChest, 0f, 1.3f);
            Bone(AvatarBone.Neck, 0f, 1.42f);
            Bone(AvatarBone.Head, 0f, 1.5f);
            foreach (float side in new[] { -1f, 1f })
            {
                bool left = side < 0f;
                Bone(left ? AvatarBone.LeftShoulder : AvatarBone.RightShoulder, side * 0.03f, 1.38f);
                Bone(left ? AvatarBone.LeftUpperArm : AvatarBone.RightUpperArm, side * 0.1f, 1.38f);
                Bone(left ? AvatarBone.LeftLowerArm : AvatarBone.RightLowerArm, side * 0.35f, 1.38f);
                Bone(left ? AvatarBone.LeftHand : AvatarBone.RightHand, side * 0.58f, 1.38f);
            }

            return (rest, has);
        }

        private static float Bend(BodyTarget pose)
        {
            return Quaternion.Angle(pose[UpperBodyBone.RightUpperArm], pose[UpperBodyBone.RightLowerArm]);
        }

        private static void AssertSame(Quaternion actual, Quaternion expected, string message = null)
        {
            Assert.That(Quaternion.Angle(actual, expected), Is.LessThan(0.05f), $"{message} {actual.eulerAngles} vs {expected.eulerAngles}");
        }
    }
}
