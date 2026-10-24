using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Tracking;
using VirtualVessel.Tracking.Recording;

namespace VirtualVessel.Avatar.Tests
{
    /// <summary>The upper body on the avatar: torso, shoulders, arms, mirroring, and timing (avatar detailed design 7.1, 7.2, 8.4).</summary>
    public sealed class AvatarBodyTests
    {
        [Test]
        public void ArmsOutSideways_AreTheReferencePose_AndHangingArmsHang()
        {
            BodyTarget target = Retarget(Body(Vector3.left, Vector3.left, Vector3.right, Vector3.right));
            AssertSame(target[UpperBodyBone.LeftUpperArm], Quaternion.identity);
            AssertSame(target[UpperBodyBone.RightLowerArm], Quaternion.identity);
            AssertSame(target[UpperBodyBone.UpperChest], Quaternion.identity);
            Assert.That(target.LeftArmSeen && target.RightArmSeen && target.TorsoSeen, Is.True);

            target = Retarget(Body(Vector3.down, Vector3.down, Vector3.down, Vector3.down));
            AssertSame(target[UpperBodyBone.LeftUpperArm], AvatarRestPose.UpperArm(true, 90f));
            AssertSame(target[UpperBodyBone.RightUpperArm], AvatarRestPose.UpperArm(false, 90f));
            AssertSame(target[UpperBodyBone.RightShoulder], Quaternion.identity, "A lowered arm leaves the shoulder alone.");
        }

        [Test]
        public void AnArm_PointsWhereItIsSeen_AndTheForearmBendsInItsPlane()
        {
            BodyTarget target = Retarget(Body(Vector3.down, Vector3.down, Vector3.forward, Vector3.forward));
            AssertDirection(target[UpperBodyBone.RightUpperArm] * Vector3.right, Vector3.forward, "The right arm reaches forward.");

            // Out sideways with the elbow bent so the forearm points forward: the upper arm keeps the
            // reference pose, the forearm turns forward.
            target = Retarget(Body(Vector3.down, Vector3.down, Vector3.right, Vector3.forward));
            AssertSame(target[UpperBodyBone.RightUpperArm], Quaternion.identity);
            AssertDirection(target[UpperBodyBone.RightLowerArm] * Vector3.right, Vector3.forward);

            // The same bend with the forearm pointing up: the upper arm twists so the elbow bends upward.
            target = Retarget(Body(Vector3.down, Vector3.down, Vector3.right, Vector3.up));
            AssertDirection(target[UpperBodyBone.RightUpperArm] * Vector3.right, Vector3.right);
            AssertDirection(target[UpperBodyBone.RightLowerArm] * Vector3.right, Vector3.up);
            AssertDirection(target[UpperBodyBone.RightUpperArm] * PoseRetargeting.RestBendNormal(false), Vector3.forward, "Twisted a quarter turn: the bend's normal is the arm crossed with the forearm.");
        }

        [Test]
        public void InAMirror_ThePersonsRightArmMovesTheAvatarsLeft()
        {
            BodyPose body = Body(Vector3.down, Vector3.down, Vector3.forward, Vector3.forward);

            BodyTarget mirrored = Retarget(body, mirror: true);

            AssertDirection(mirrored[UpperBodyBone.LeftUpperArm] * Vector3.left, Vector3.forward, "As in a mirror.");
            AssertSame(mirrored[UpperBodyBone.RightUpperArm], AvatarRestPose.UpperArm(false, 90f));
        }

        [Test]
        public void TheTorso_TiltsWithTheShoulders_SharedUpTheSpine_WithinItsLimit()
        {
            BodyPose body = Body(Vector3.down, Vector3.down, Vector3.down, Vector3.down);
            Tilt(body, 20f);
            BodyTarget target = Retarget(body);
            Assert.That(Roll(target[UpperBodyBone.UpperChest]), Is.EqualTo(20f).Within(0.5f));
            Assert.That(Roll(target[UpperBodyBone.Spine]), Is.EqualTo(20f * PoseRetargeting.SpineShare).Within(0.5f));
            Assert.That(Roll(target[UpperBodyBone.Chest]), Is.EqualTo(20f * PoseRetargeting.ChestShare).Within(0.5f));

            Tilt(body, 50f);
            Assert.That(Roll(Retarget(body)[UpperBodyBone.UpperChest]), Is.EqualTo(PoseRetargeting.MaxTiltDegrees).Within(0.5f), "A misread does not bend the torso further.");
            Assert.That(Roll(Retarget(body, hasUpperChest: false)[UpperBodyBone.Chest]), Is.EqualTo(PoseRetargeting.MaxTiltDegrees).Within(0.5f), "Without an UpperChest, Chest takes the whole turn.");
        }

        [Test]
        public void ARaisedArm_LiftsItsShoulderALittle()
        {
            Assert.That(PoseRetargeting.ShoulderRaise(false, Quaternion.identity, Quaternion.identity), Is.EqualTo(0f), "Level.");
            Assert.That(PoseRetargeting.ShoulderRaise(false, AvatarRestPose.UpperArm(false, 60f), Quaternion.identity), Is.EqualTo(0f), "Below level.");
            Assert.That(PoseRetargeting.ShoulderRaise(false, Quaternion.AngleAxis(40f, Vector3.forward), Quaternion.identity), Is.EqualTo(10f).Within(0.01f));
            Assert.That(PoseRetargeting.ShoulderRaise(true, Quaternion.AngleAxis(-90f, Vector3.forward), Quaternion.identity), Is.EqualTo(PoseRetargeting.MaxShoulderRaiseDegrees), "Straight up, at most the limit.");
            AssertDirection(PoseRetargeting.Shoulder(true, 10f, Quaternion.identity) * Vector3.left, Quaternion.AngleAxis(-10f, Vector3.forward) * Vector3.left);
            Assert.That((PoseRetargeting.Shoulder(true, 10f, Quaternion.identity) * Vector3.left).y, Is.GreaterThan(0f), "Lifted, not lowered.");
        }

        [Test]
        public void AnArmNotSeen_Hangs_AndIsSaidSo()
        {
            BodyPose body = Body(Vector3.left, Vector3.left, Vector3.right, Vector3.right);
            body.JointConfidence[(int)BodyJoint.RightElbow] = 0.2f;

            BodyTarget target = Retarget(body);

            Assert.That(target.RightArmSeen, Is.False);
            AssertSame(target[UpperBodyBone.RightUpperArm], AvatarRestPose.UpperArm(false, AvatarRestPose.DefaultArmDownDegrees));
            Assert.That(target.LeftArmSeen, Is.True);

            body.Status = TrackingStatus.Lost;
            Assert.That(Retarget(body).TorsoSeen, Is.False);
        }

        [Test]
        public void AJointOutsideTheImage_IsNotSeen_HoweverVisible()
        {
            // MediaPipe guesses joints out of the image and may call them visible (tracking detailed design 8.4).
            BodyPose body = Body(Vector3.left, Vector3.left, Vector3.right, Vector3.right);
            body.ImagePositions[(int)BodyJoint.RightWrist] = new Vector2(0.9f, 1.2f);
            BodyTarget target = Retarget(body);
            Assert.That(target.RightArmSeen, Is.True);
            Assert.That(target.RightForearmSeen, Is.False, "A wrist below the image does not move the forearm.");

            body.ImagePositions[(int)BodyJoint.RightElbow] = new Vector2(1.1f, 0.6f);
            target = Retarget(body);
            Assert.That(target.RightArmSeen, Is.False, "An elbow beside the image does not move the arm.");
            AssertSame(target[UpperBodyBone.RightUpperArm], AvatarRestPose.UpperArm(false, AvatarRestPose.DefaultArmDownDegrees));
            Assert.That(target.LeftArmSeen, Is.True);
        }

        [Test]
        public void ALostArm_Holds_ThenHangs_AndComesBack()
        {
            var motion = new BodyMotion(hasUpperChest: true);
            BodyPose body = Body(Vector3.left, Vector3.left, Vector3.right, Vector3.right);

            Observe(motion, body, 1.0, 1.0);
            Observe(motion, body, 1.3, 1.3);
            Assert.That(motion.RightArmPresence, Is.EqualTo(1f));
            AssertSame(motion.Current[UpperBodyBone.RightUpperArm], Quaternion.identity);

            body.JointConfidence[(int)BodyJoint.RightElbow] = 0.1f;
            Observe(motion, body, 1.4, 1.4);
            Observe(motion, body, 1.7, 1.7);
            Assert.That(motion.RightArmPresence, Is.EqualTo(1f), "Held for 0.5 s.");
            AssertSame(motion.Current[UpperBodyBone.RightUpperArm], Quaternion.identity, "Where it was last seen, not dropped at once.");

            // Times from whole tenths, so adding 0.1 again and again cannot drift past the last one.
            for (int tenths = 18; tenths <= 30; tenths++)
            {
                Observe(motion, body, tenths / 10.0, tenths / 10.0);
            }

            Assert.That(motion.RightArmPresence, Is.EqualTo(0f));
            AssertSame(motion.Current[UpperBodyBone.RightUpperArm], AvatarRestPose.UpperArm(false, AvatarRestPose.DefaultArmDownDegrees), "Back to hanging.");
            Assert.That(motion.LeftArmPresence, Is.EqualTo(1f), "The other arm is its own.");

            body.JointConfidence[(int)BodyJoint.RightElbow] = 0.9f;
            Observe(motion, body, 3.1, 3.1);
            Assert.That(motion.RightArmPresence, Is.EqualTo(0.5f).Within(0.05f), "Back over 0.2 s.");
        }

        [Test]
        public void AHiddenWrist_KeepsTheForearmsBend_AsTheUpperArmMoves()
        {
            // Out sideways, the forearm bent up; then the wrist goes behind the head and the elbow rises.
            var motion = new BodyMotion(hasUpperChest: true);
            BodyPose body = Body(Vector3.down, Vector3.down, Vector3.right, Vector3.up);
            Observe(motion, body, 1.0, 1.0);
            Observe(motion, body, 1.1, 1.3);
            Quaternion upper = motion.Current[UpperBodyBone.RightUpperArm];
            Quaternion bend = Quaternion.Inverse(upper) * motion.Current[UpperBodyBone.RightLowerArm];

            BodyPose raised = Body(Vector3.down, Vector3.down, new Vector3(1f, 1f, 0f), Vector3.up);
            raised.JointConfidence[(int)BodyJoint.RightWrist] = 0.1f;
            Assert.That(Retarget(raised).RightArmSeen, Is.True, "The upper arm still moves without the wrist.");
            Assert.That(Retarget(raised).RightForearmSeen, Is.False);
            Observe(motion, raised, 1.2, 1.2);
            Observe(motion, raised, 1.3, 1.3);
            motion.Update(raised, false, 1.6);

            Quaternion raisedUpper = motion.Current[UpperBodyBone.RightUpperArm];
            AssertDirection(raisedUpper * Vector3.right, new Vector3(1f, 1f, 0f).normalized, "The upper arm follows the elbow.");
            AssertSame(Quaternion.Inverse(raisedUpper) * motion.Current[UpperBodyBone.RightLowerArm], bend, "The forearm keeps its bend.");
            Assert.That(motion.RightArmPresence, Is.EqualTo(1f), "Nothing fades: the arm is seen.");
        }

        [Test]
        public void WithoutTracking_TheBodyRests()
        {
            var motion = new BodyMotion(hasUpperChest: true);

            motion.Update(null, true, 5.0);

            AssertSame(motion.Current[UpperBodyBone.UpperChest], Quaternion.identity);
            AssertSame(motion.Current[UpperBodyBone.LeftUpperArm], AvatarRestPose.UpperArm(true, AvatarRestPose.DefaultArmDownDegrees));
        }

        [Test]
        public void TheController_SetsTheUpperBody_AndTurnsTheNeckFromTheTorso()
        {
            var model = new AvatarFaceTests.RecordingModel();
            var controller = new AvatarController(model, ExpressionMapping.Default);
            var frame = new TrackingFrame();
            frame.Body.CopyFrom(Body(Vector3.down, Vector3.down, Vector3.right, Vector3.right));
            Tilt(frame.Body, 20f);
            frame.Body.Timestamp = 1.0;

            controller.Update(frame, mirror: false, now: 1.0);
            controller.Update(frame, mirror: false, now: 1.5);

            Assert.That(Roll(model.Rotations[AvatarBone.UpperChest]), Is.EqualTo(20f).Within(0.5f));
            AssertSame(model.Rotations[AvatarBone.Neck], Quaternion.Slerp(model.Rotations[AvatarBone.UpperChest], Quaternion.identity, HeadPose.NeckShare), "The neck turns from the torso toward the head.");
            Assert.That(model.Rotations.ContainsKey(AvatarBone.LeftLowerArm), Is.True);
        }

        private static BodyTarget Retarget(BodyPose body, bool mirror = false, bool hasUpperChest = true)
        {
            var target = new BodyTarget();
            PoseRetargeting.Retarget(body, mirror, hasUpperChest, AvatarRestPose.DefaultArmDownDegrees, target);
            return target;
        }

        private static void Observe(BodyMotion motion, BodyPose body, double timestamp, double now)
        {
            body.Timestamp = timestamp;
            motion.Update(body, false, now);
        }

        /// <summary>A body in person space with each upper arm and forearm pointing the given ways.</summary>
        private static BodyPose Body(Vector3 leftUpper, Vector3 leftFore, Vector3 rightUpper, Vector3 rightFore)
        {
            var body = new BodyPose();
            SyntheticMotion.WriteArms(body, leftUpper, leftFore, rightUpper, rightFore, 0.0);
            return body;
        }

        /// <summary>Tilts the shoulders, toward the person's right shoulder going down, about the hips.</summary>
        private static void Tilt(BodyPose body, float degrees)
        {
            Quaternion tilt = Quaternion.AngleAxis(-degrees, Vector3.forward);
            foreach (BodyJoint joint in new[] { BodyJoint.Neck, BodyJoint.LeftShoulder, BodyJoint.RightShoulder, BodyJoint.LeftElbow, BodyJoint.RightElbow, BodyJoint.LeftWrist, BodyJoint.RightWrist })
            {
                body[joint] = tilt * body[joint];
            }
        }

        /// <summary>How far a rotation turns about the front, positive toward the right going down.</summary>
        private static float Roll(Quaternion rotation)
        {
            Vector3 right = rotation * Vector3.right;
            return -Mathf.Atan2(right.y, right.x) * Mathf.Rad2Deg;
        }

        private static void AssertSame(Quaternion actual, Quaternion expected, string message = null)
        {
            Assert.That(Quaternion.Angle(actual, expected), Is.LessThan(0.05f), $"{message} {actual.eulerAngles} vs {expected.eulerAngles}");
        }

        private static void AssertDirection(Vector3 actual, Vector3 expected, string message = null)
        {
            Assert.That(Vector3.Angle(actual, expected), Is.LessThan(0.05f), $"{message} {actual} vs {expected}");
        }
    }
}
