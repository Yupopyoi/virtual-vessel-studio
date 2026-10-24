using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Tracking;
using VirtualVessel.Tracking.Recording;

namespace VirtualVessel.Avatar.Tests
{
    /// <summary>The hands' turn and the forearms' twist from the tracked palms (avatar detailed design 7.1, 7.5).</summary>
    public sealed class HandRetargetingTests
    {
        // The right hand of an arm out sideways (+X): palm down is the reference pose, back of the hand up.
        private static readonly Quaternion s_palmDown = Quaternion.LookRotation(Vector3.right, Vector3.up);

        [Test]
        public void APalmDown_IsTheReferencePose()
        {
            var arm = new BodyTarget();

            HandRetargeting.Apply(ArmSide.Right, s_palmDown, 1f, arm);

            AssertSame(arm[UpperBodyBone.RightLowerArm], Quaternion.identity);
            AssertSame(arm[UpperBodyBone.RightHand], Quaternion.identity);
        }

        [Test]
        public void APalmTurnedUp_TwistsTheForearmHalfway_AndTheHandAllTheWay()
        {
            var arm = new BodyTarget();
            Quaternion palmUp = Quaternion.LookRotation(Vector3.right, Vector3.down);

            HandRetargeting.Apply(ArmSide.Right, palmUp, 1f, arm);

            // Turning the right palm up (supination) rolls the back of the hand from up, through back, to down.
            AssertDirection(arm[UpperBodyBone.RightLowerArm] * Vector3.up, Vector3.back, "The forearm half way: the back of the forearm faces back.");
            AssertDirection(arm[UpperBodyBone.RightHand] * Vector3.up, Vector3.down, "The hand all the way.");
            AssertDirection(arm[UpperBodyBone.RightHand] * Vector3.right, Vector3.right, "Still pointing along the arm.");
        }

        [Test]
        public void APalmNearlyOver_IsReadAsTurnedUp_EitherSideOfIt()
        {
            // 170° and 190° of supination: the second is read as 180°, not as 170° of pronation the other way.
            Quaternion lower = Quaternion.identity;
            float before = HandRetargeting.Twist(false, lower, Quaternion.AngleAxis(-170f, Vector3.right) * s_palmDown);
            float past = HandRetargeting.Twist(false, lower, Quaternion.AngleAxis(-190f, Vector3.right) * s_palmDown);
            float pronated = HandRetargeting.Twist(false, lower, Quaternion.AngleAxis(90f, Vector3.right) * s_palmDown);

            Assert.That(before, Is.EqualTo(-170f).Within(0.1f));
            Assert.That(past, Is.EqualTo(-HandRetargeting.MaxSupinationDegrees).Within(0.1f), "Kept on the supination side.");
            Assert.That(pronated, Is.EqualTo(HandRetargeting.MaxPronationDegrees).Within(0.1f), "Pronation stops at its reach.");
        }

        [Test]
        public void ThePresence_BlendsFromFollowingTheForearm_ToThePalm()
        {
            Quaternion palmUp = Quaternion.LookRotation(Vector3.right, Vector3.down);
            var hidden = new BodyTarget();
            var half = new BodyTarget();

            HandRetargeting.Apply(ArmSide.Right, palmUp, 0f, hidden);
            HandRetargeting.Apply(ArmSide.Right, palmUp, 0.5f, half);

            AssertSame(hidden[UpperBodyBone.RightLowerArm], Quaternion.identity, "Not seen: no twist.");
            AssertSame(hidden[UpperBodyBone.RightHand], Quaternion.identity, "The hand follows the forearm.");
            Assert.That(Quaternion.Angle(half[UpperBodyBone.RightLowerArm], Quaternion.identity), Is.EqualTo(45f).Within(0.1f), "Half of the forearm's half.");
        }

        [Test]
        public void InAMirror_ThePersonsOtherHand_MovesTheAvatarsHand_Reflected()
        {
            var frame = new TrackingFrame();
            Hand(frame.RightHand, Quaternion.LookRotation(Vector3.right, Vector3.down), 1.0);
            Hand(frame.LeftHand, Quaternion.LookRotation(Vector3.left, Vector3.up), 1.0);

            Assert.That(HandRetargeting.PersonHand(frame, ArmSide.Left, mirror: true), Is.SameAs(frame.RightHand));
            Assert.That(HandRetargeting.PersonHand(frame, ArmSide.Left, mirror: false), Is.SameAs(frame.LeftHand));
            Assert.That(HandRetargeting.TryPalm(frame.RightHand, mirror: true, out Quaternion palm), Is.True);
            AssertDirection(palm * Vector3.forward, Vector3.left, "The fingers reflected to the avatar's left.");
            AssertDirection(palm * Vector3.up, Vector3.down, "The palm still up.");

            frame.RightHand.Status = TrackingStatus.Lost;
            Assert.That(HandRetargeting.TryPalm(frame.RightHand, mirror: true, out _), Is.False);
        }

        [Test]
        public void ALostHand_Holds_ThenFollowsTheForearm_AndComesBack()
        {
            var frame = new TrackingFrame();
            var motion = new HandMotion();
            Quaternion palmUp = Quaternion.LookRotation(Vector3.right, Vector3.down);
            Hand(frame.RightHand, palmUp, 1.0);
            motion.Update(frame, mirror: false, now: 1.0);
            motion.Update(frame, mirror: false, now: 1.3);
            Assert.That(motion.RightPresence, Is.EqualTo(1f));
            AssertSame(motion.RightPalm, palmUp);

            frame.RightHand.Status = TrackingStatus.Lost;
            motion.Update(frame, mirror: false, now: 1.4);
            Assert.That(motion.RightPresence, Is.EqualTo(1f), "Held.");
            for (int tenths = 15; tenths <= 30; tenths++)
            {
                motion.Update(frame, mirror: false, now: tenths / 10.0);
            }

            Assert.That(motion.RightPresence, Is.EqualTo(0f), "Then the hand follows the forearm.");

            Hand(frame.RightHand, s_palmDown, 3.1);
            motion.Update(frame, mirror: false, now: 3.1);
            AssertSame(motion.RightPalm, s_palmDown, "Starts from the palm seen again.");
            motion.Update(frame, mirror: false, now: 3.4);
            Assert.That(motion.RightPresence, Is.EqualTo(1f), "Back in 0.2 s.");
        }

        [Test]
        public void TheController_TurnsTheHand_AndTwistsTheForearm()
        {
            var model = new AvatarFaceTests.RecordingModel();
            var controller = new AvatarController(model, ExpressionMapping.Default);
            var frame = new TrackingFrame();
            SyntheticMotion.WriteArms(frame.Body, Vector3.down, Vector3.down, Vector3.right, Vector3.right, 1.0);
            Hand(frame.RightHand, Quaternion.LookRotation(Vector3.right, Vector3.down), 1.0);

            controller.Update(frame, mirror: false, now: 1.0);
            controller.Update(frame, mirror: false, now: 1.5);

            AssertDirection(model.Rotations[AvatarBone.RightHand] * Vector3.up, Vector3.down, "The palm up.");
            AssertDirection(model.Rotations[AvatarBone.RightLowerArm] * Vector3.up, Vector3.back, "The forearm twisted half way.");
            AssertSame(model.Rotations[AvatarBone.LeftHand], model.Rotations[AvatarBone.LeftLowerArm], "The unseen left hand follows its forearm.");
        }

        private static void Hand(HandPose hand, Quaternion palm, double time)
        {
            hand.Status = TrackingStatus.Tracked;
            hand.Confidence = 0.9f;
            hand.Timestamp = time;
            hand.PalmRotation = palm;
        }

        private static void AssertSame(Quaternion actual, Quaternion expected, string message = null)
        {
            Assert.That(Quaternion.Angle(actual, expected), Is.LessThan(0.05f), $"{message} {actual.eulerAngles} vs {expected.eulerAngles}");
        }

        private static void AssertDirection(Vector3 actual, Vector3 expected, string message = null)
        {
            Assert.That(Vector3.Angle(actual, expected), Is.LessThan(0.5f), $"{message} {actual} vs {expected}");
        }
    }
}
