using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Tracking;

namespace VirtualVessel.Avatar.Tests
{
    /// <summary>The fingers from the tracked hand (avatar detailed design 7.5).</summary>
    public sealed class FingerTests
    {
        private const float Tolerance = 1.5f;

        [Test]
        public void AnOpenHand_IsStraight()
        {
            HandPose hand = Hand(bends: 0f, middleBends: 0f, endBends: 0f, spread: 0f, thumbOpen: 45f, thumbBends: 0f);

            var pose = new FingerPose();
            Assert.That(FingerRetargeting.TryMeasure(hand, left: false, pose), Is.True);

            for (Finger finger = Finger.Index; finger <= Finger.Little; finger++)
            {
                for (int joint = 0; joint < FingerPose.JointCount; joint++)
                {
                    Assert.That(pose.Bend(finger, joint), Is.EqualTo(0f).Within(Tolerance), $"{finger} {joint}");
                }

                Assert.That(pose.Spread(finger), Is.EqualTo(0f).Within(Tolerance), $"{finger} spread");
            }

            Assert.That(pose.Bend(Finger.Thumb, 0), Is.EqualTo(0f), "An open thumb does not fold.");
            Assert.That(pose.Bend(Finger.Thumb, 1), Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void ABentHand_IsShownBentFurtherThanRead_WithinTheLimits()
        {
            // The tracked hand reads its bends small; past what an open hand reads, they are scaled up.
            HandPose hand = Hand(bends: 50f, middleBends: 50f, endBends: 5f, spread: 0f, thumbOpen: 10f, thumbBends: 50f);

            var pose = new FingerPose();
            FingerRetargeting.TryMeasure(hand, left: false, pose);

            Assert.That(pose.Bend(Finger.Middle, 0), Is.EqualTo((50f - FingerRetargeting.OpenBend) * FingerRetargeting.BaseGain).Within(Tolerance * 2f), "Into the palm at the base.");
            float middle = FingerRetargeting.ShownMiddle(Finger.Middle, 50f);
            Assert.That(pose.Bend(Finger.Middle, 1), Is.EqualTo(middle).Within(Tolerance * 2f));
            Assert.That(pose.Bend(Finger.Middle, 2), Is.EqualTo(middle * FingerRetargeting.EndShare).Within(Tolerance * 2f), "The end joint goes with the middle one, whatever it reads.");
            Assert.That(pose.Bend(Finger.Thumb, 0), Is.EqualTo(0f), "A thumb standing out of the fist does not fold.");
            Assert.That(pose.Bend(Finger.Thumb, 2), Is.EqualTo((50f - FingerRetargeting.ThumbOpenBend) * FingerRetargeting.ThumbGain).Within(Tolerance * 2f));

            FingerRetargeting.TryMeasure(Hand(bends: 80f, middleBends: 120f, endBends: 60f, spread: 0f, thumbOpen: 10f, thumbBends: 50f), left: false, pose);
            Assert.That(pose.Bend(Finger.Middle, 0), Is.EqualTo(FingerRetargeting.MaxBaseBend), "Held to the limit.");
            Assert.That(pose.Bend(Finger.Middle, 1), Is.EqualTo(FingerRetargeting.MaxMiddleBend));
            Assert.That(pose.Bend(Finger.Middle, 2), Is.EqualTo(FingerRetargeting.MaxMiddleBend * FingerRetargeting.EndShare).Within(0.01f));
        }

        [Test]
        public void ARaisedThumb_StaysStraight_AndAWrappedOrTuckedOne_FoldsAcrossThePalm()
        {
            // A fist with the thumb up, its end joint reading a little bent, as the user's raised thumbs did.
            HandPose hand = Hand(bends: 80f, middleBends: 90f, endBends: 60f, spread: 0f, thumbOpen: 10f, thumbBends: 18f);
            var pose = new FingerPose();
            FingerRetargeting.TryMeasure(hand, left: false, pose);
            Assert.That(pose.Bend(Finger.Thumb, 0), Is.EqualTo(0f), "Raised: no fold.");
            Assert.That(pose.Bend(Finger.Thumb, 1), Is.EqualTo(0f).Within(Tolerance), "Straight at the middle.");
            Assert.That(pose.Bend(Finger.Thumb, 2), Is.EqualTo(0f).Within(Tolerance), "Straight at the end.");

            // The tip over the middle finger, across the palm.
            hand[HandJoint.ThumbTip] = new Vector3(0.01f, -0.02f, 0.06f);
            FingerRetargeting.TryMeasure(hand, left: false, pose);
            Assert.That(pose.Bend(Finger.Thumb, 0), Is.EqualTo(FingerRetargeting.MaxThumbFold), "Tucked: folded across.");
            Assert.That(pose.Bend(Finger.Thumb, 1), Is.GreaterThanOrEqualTo(FingerRetargeting.ThumbTuckBend));

            FingerRetargeting.TryMeasure(Mirrored(hand), left: true, pose);
            Assert.That(pose.Bend(Finger.Thumb, 0), Is.EqualTo(FingerRetargeting.MaxThumbFold), "The same on the left hand.");

            // Wrapped over the curled fingers, as in rock-paper-scissors: the tip only a tenth of the palm across.
            hand = Hand(bends: 80f, middleBends: 90f, endBends: 60f, spread: 0f, thumbOpen: 10f, thumbBends: 18f);
            hand[HandJoint.ThumbTip] = new Vector3(-0.02f, -0.02f, 0.07f);
            FingerRetargeting.TryMeasure(hand, left: false, pose);
            Assert.That(pose.Bend(Finger.Thumb, 0), Is.GreaterThan(FingerRetargeting.MaxThumbFold * 0.8f), "Wrapped: folded across too.");

            // Laid along the side of the index finger: not across the palm.
            hand[HandJoint.ThumbTip] = new Vector3(-0.044f, -0.01f, 0.07f);
            FingerRetargeting.TryMeasure(hand, left: false, pose);
            Assert.That(pose.Bend(Finger.Thumb, 0), Is.EqualTo(0f), "Beside the index finger: no fold.");
        }

        [Test]
        public void TheMiddleJoint_IsStraightAtAnOpenHandsReading_AndFullyBentAtAFists()
        {
            // A straight middle finger reads about 36° at its middle joint (the user's recordings).
            Assert.That(FingerRetargeting.ShownMiddle(Finger.Middle, 36f), Is.EqualTo(0f));
            Assert.That(FingerRetargeting.ShownMiddle(Finger.Middle, 30f), Is.EqualTo(0f), "Never bent back.");
            Assert.That(FingerRetargeting.ShownMiddle(Finger.Middle, 90f), Is.EqualTo(FingerRetargeting.MaxMiddleBend));
            Assert.That(FingerRetargeting.ShownMiddle(Finger.Index, 18f), Is.EqualTo(0f));
            Assert.That(FingerRetargeting.ShownMiddle(Finger.Little, 72f), Is.EqualTo(FingerRetargeting.MaxMiddleBend));
            Assert.That(FingerRetargeting.ShownMiddle(Finger.Ring, 55f), Is.InRange(40f, 60f), "In proportion between.");
        }

        [Test]
        public void ASpread_IsTowardTheThumb_OnEitherHand_WithinItsLimit()
        {
            var pose = new FingerPose();
            FingerRetargeting.TryMeasure(Hand(0f, 0f, 0f, spread: 10f, thumbOpen: 45f, thumbBends: 0f), left: false, pose);
            Assert.That(pose.Spread(Finger.Index), Is.EqualTo(10f).Within(Tolerance));

            FingerRetargeting.TryMeasure(Mirrored(Hand(0f, 0f, 0f, spread: 10f, thumbOpen: 45f, thumbBends: 0f)), left: true, pose);
            Assert.That(pose.Spread(Finger.Index), Is.EqualTo(10f).Within(Tolerance), "The left hand's thumb is on its other side.");
            Assert.That(pose.Bend(Finger.Index, 0), Is.EqualTo(0f).Within(Tolerance));

            FingerRetargeting.TryMeasure(Hand(0f, 0f, 0f, spread: 40f, thumbOpen: 45f, thumbBends: 0f), left: false, pose);
            Assert.That(pose.Spread(Finger.Index), Is.EqualTo(FingerRetargeting.MaxSpread));
        }

        [Test]
        public void AHandNotSeen_IsNotMeasured()
        {
            var pose = new FingerPose();
            HandPose hand = Hand(30f, 30f, 30f, 0f, 45f, 0f);
            hand.Status = TrackingStatus.Lost;
            Assert.That(FingerRetargeting.TryMeasure(hand, left: false, pose), Is.False);
            Assert.That(FingerRetargeting.TryMeasure(null, left: false, pose), Is.False);
        }

        [Test]
        public void TheFingers_BendIntoThePalm_AndTheThumbAcrossIt_OnEitherHand()
        {
            foreach (ArmSide side in new[] { ArmSide.Left, ArmSide.Right })
            {
                FingerRest rest = FingerRest.Of(null, side);
                Assert.That(Vector3.Angle(rest.Back, Vector3.up), Is.LessThan(1f), "In the reference pose the palm is down.");
                Assert.That(Vector3.Angle(rest.Thumbward, Vector3.forward), Is.LessThan(1f), "The thumb in front.");

                var pose = new FingerPose();
                pose.SetBend(Finger.Index, 0, 90f);
                pose.SetBend(Finger.Thumb, 1, 45f);
                var rotations = new Quaternion[15];
                FingerRetargeting.Rotations(rest, Quaternion.identity, pose, rotations);

                Vector3 index = rotations[(int)Finger.Index * 3] * rest.Direction(Finger.Index, 0);
                Assert.That(Vector3.Angle(index, Vector3.down), Is.LessThan(1f), $"{(side.IsLeft ? "Left" : "Right")}: the index finger points into the palm.");
                Vector3 thumb = rotations[((int)Finger.Thumb * 3) + 1] * rest.Direction(Finger.Thumb, 1);
                Assert.That(Vector3.Dot(thumb, rest.Thumbward), Is.LessThan(Vector3.Dot(rest.Direction(Finger.Thumb, 1), rest.Thumbward)), "The thumb turns across the palm.");
                Assert.That(Vector3.Dot(thumb, Vector3.down), Is.GreaterThan(Vector3.Dot(rest.Direction(Finger.Thumb, 1), Vector3.down)), "And into it.");
                Assert.That(rotations[((int)Finger.Index * 3) + 1], Is.EqualTo(rotations[(int)Finger.Index * 3]), "A joint carries its parent's turn.");

                Quaternion hand = Quaternion.AngleAxis(90f, Vector3.right);
                FingerRetargeting.Rotations(rest, hand, pose, rotations);
                Assert.That(Vector3.Angle(rotations[(int)Finger.Index * 3] * rest.Direction(Finger.Index, 0), hand * Vector3.down), Is.LessThan(1f), "The fingers turn with the hand.");
            }
        }

        [Test]
        public void ALostHand_HoldsItsFingers_ThenClosesThemLoosely()
        {
            var motion = new HandMotion();
            var frame = new TrackingFrame();
            motion.Update(frame, false, 1.0);
            Assert.That(motion.RightFingers.Values, Is.EqualTo(FingerPose.Relaxed.Values), "Before a hand is seen, loosely closed.");

            HandPose fist = Hand(80f, 90f, 60f, 0f, 10f, 40f);
            frame.RightHand.CopyFrom(fist);
            for (int tenths = 10; tenths <= 15; tenths++)
            {
                frame.RightHand.Timestamp = tenths / 10.0;
                motion.Update(frame, false, tenths / 10.0);
            }

            Assert.That(motion.RightPresence, Is.EqualTo(1f));
            Assert.That(motion.RightFingers.Bend(Finger.Middle, 0), Is.EqualTo(FingerRetargeting.MaxBaseBend).Within(Tolerance), "As tracked.");

            frame.RightHand.Status = TrackingStatus.Lost;
            motion.Update(frame, false, 1.8);
            Assert.That(motion.RightFingers.Bend(Finger.Middle, 0), Is.EqualTo(FingerRetargeting.MaxBaseBend).Within(Tolerance), "Held for 0.5 s.");
            for (int tenths = 19; tenths <= 32; tenths++)
            {
                motion.Update(frame, false, tenths / 10.0);
            }

            Assert.That(motion.RightFingers.Values, Is.EqualTo(FingerPose.Relaxed.Values), "Then loosely closed.");
        }

        /// <summary>
        /// The person's right hand, palm down, fingers forward (+Z), the thumb to the left (-X): every
        /// finger bent by the same angles, the index spread toward the thumb and the little finger away by
        /// <paramref name="spread"/>, and the thumb <paramref name="thumbOpen"/> degrees from the index metacarpal.
        /// </summary>
        private static HandPose Hand(float bends, float middleBends, float endBends, float spread, float thumbOpen, float thumbBends)
        {
            var hand = new HandPose { Status = TrackingStatus.Tracked, Confidence = 0.9f, Timestamp = 1.0 };
            hand[HandJoint.Wrist] = Vector3.zero;
            float[] lateral = { -0.025f, -0.008f, 0.008f, 0.025f };
            for (int f = 0; f < 4; f++)
            {
                Vector3 knuckle = new Vector3(lateral[f], 0f, 0.09f);
                Vector3 metacarpal = knuckle.normalized;
                float spreadHere = f == 0 ? spread : f == 3 ? -spread : 0f;

                // Toward the thumb (-X) about the back (+Y); into the palm (-Y) about the side.
                Quaternion spreadTurn = Quaternion.AngleAxis(-spreadHere, Vector3.up);
                int first = (int)HandJoint.IndexMcp + (f * 4);
                hand.Positions[first] = knuckle;
                Vector3 direction = spreadTurn * metacarpal;
                Vector3 at = knuckle;
                float[] angles = { bends, middleBends, endBends };
                float total = 0f;
                for (int joint = 0; joint < 3; joint++)
                {
                    total += angles[joint];
                    Vector3 segment = Quaternion.AngleAxis(total, Vector3.Cross(direction, Vector3.down).normalized) * direction;
                    at += segment * 0.03f;
                    hand.Positions[first + joint + 1] = at;
                }
            }

            // The thumb from its base beside the wrist, turned from the index metacarpal toward the left.
            Vector3 thumbBase = new Vector3(-0.02f, 0f, 0.02f);
            Vector3 indexMetacarpal = hand[HandJoint.IndexMcp].normalized;
            Vector3 thumbDirection = Quaternion.AngleAxis(-thumbOpen, Vector3.up) * indexMetacarpal;
            hand[HandJoint.ThumbCmc] = thumbBase;
            hand[HandJoint.ThumbMcp] = thumbBase + (thumbDirection * 0.04f);
            Vector3 bent = Quaternion.AngleAxis(thumbBends, Vector3.Cross(thumbDirection, Vector3.down).normalized) * thumbDirection;
            hand[HandJoint.ThumbIp] = hand[HandJoint.ThumbMcp] + (thumbDirection * 0.03f);
            hand[HandJoint.ThumbTip] = hand[HandJoint.ThumbIp] + (bent * 0.025f);
            return hand;
        }

        /// <summary>The same hand reflected through X = 0: the person's left hand.</summary>
        private static HandPose Mirrored(HandPose hand)
        {
            for (int i = 0; i < HandPose.JointCount; i++)
            {
                Vector3 p = hand.Positions[i];
                hand.Positions[i] = new Vector3(-p.x, p.y, p.z);
            }

            return hand;
        }
    }
}
