using NUnit.Framework;
using UnityEngine;

namespace VirtualVessel.Avatar.Tests
{
    /// <summary>The arm bones turning no faster than their limits, where they point and their twist apart (avatar detailed design 7.8).</summary>
    public sealed class SteadyMotionTests
    {
        [Test]
        public void ATurnWithinTheLimits_IsTakenWhole()
        {
            Quaternion target = Quaternion.AngleAxis(5f, Vector3.forward) * Quaternion.AngleAxis(3f, Vector3.right);

            Quaternion limited = SteadyMotion.Limit(Quaternion.identity, target, Vector3.right, 12f, 6f);

            Assert.That(Quaternion.Angle(limited, target), Is.LessThan(0.01f));
        }

        [Test]
        public void WherePointing_AndTheTwist_AreEachHeldToTheirLimit()
        {
            // The right arm raised 90° and twisted half a turn at once.
            Quaternion target = Quaternion.AngleAxis(180f, Vector3.up) * Quaternion.AngleAxis(90f, Vector3.forward);
            Quaternion raised = Quaternion.AngleAxis(90f, Vector3.forward);
            Quaternion twisted = Quaternion.AngleAxis(170f, Vector3.right);

            Quaternion swung = SteadyMotion.Limit(Quaternion.identity, raised, Vector3.right, 12f, 6f);
            Quaternion rolled = SteadyMotion.Limit(Quaternion.identity, twisted, Vector3.right, 12f, 6f);

            Assert.That(Vector3.Angle(swung * Vector3.right, Vector3.right), Is.EqualTo(12f).Within(0.01f), "Where it points: 12°.");
            Assert.That(Quaternion.Angle(rolled, Quaternion.identity), Is.EqualTo(6f).Within(0.01f), "Its twist: 6°.");
            Assert.That(Vector3.Angle(rolled * Vector3.right, Vector3.right), Is.LessThan(0.01f), "A twist alone does not move where it points.");
            Assert.That(Quaternion.Angle(SteadyMotion.Limit(Quaternion.identity, target, Vector3.right, 12f, 6f), Quaternion.identity), Is.LessThanOrEqualTo(18.01f));
        }

        [Test]
        public void TheMotion_HoldsTheArmsBack_ButNotTheTorso()
        {
            var motion = new SteadyMotion();
            var target = new BodyTarget();
            motion.Update(target, 1.0);

            target[UpperBodyBone.RightUpperArm] = Quaternion.AngleAxis(-90f, Vector3.forward);
            target[UpperBodyBone.UpperChest] = Quaternion.AngleAxis(20f, Vector3.forward);
            motion.Update(target, 1.0 + (1.0 / 60.0));

            Assert.That(Quaternion.Angle(motion.Current[UpperBodyBone.RightUpperArm], Quaternion.identity), Is.EqualTo(SteadyMotion.MaxSwingDegreesPerSecond / 60f).Within(0.05f), "A sixtieth of a second's turn.");
            Assert.That(Quaternion.Angle(motion.Current[UpperBodyBone.UpperChest], target[UpperBodyBone.UpperChest]), Is.LessThan(0.01f), "The torso at once.");

            for (int i = 2; i < 60; i++)
            {
                motion.Update(target, 1.0 + (i / 60.0));
            }

            Assert.That(Quaternion.Angle(motion.Current[UpperBodyBone.RightUpperArm], target[UpperBodyBone.RightUpperArm]), Is.LessThan(0.01f), "There within its time.");
        }
    }
}
