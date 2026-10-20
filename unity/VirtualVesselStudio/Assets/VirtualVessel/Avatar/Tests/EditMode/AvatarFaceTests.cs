using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Tracking;

namespace VirtualVessel.Avatar.Tests
{
    /// <summary>The face on the avatar: expressions, gaze, the head's turn, and timing (avatar detailed design 7.1, 7.3, 7.4, 8.4).</summary>
    public sealed class AvatarFaceTests
    {
        private const float Tolerance = 1e-4f;

        [Test]
        public void Curve_IsZeroBelow_FullAbove_AndSmoothBetween()
        {
            var curve = new ExpressionCurve(0.2f, 0.6f, 0.5f);

            Assert.That(curve.Evaluate(0.1f), Is.EqualTo(0f));
            Assert.That(curve.Evaluate(0.4f), Is.EqualTo(0.25f).Within(Tolerance), "Half way gives half the gain.");
            Assert.That(curve.Evaluate(0.9f), Is.EqualTo(0.5f));
            Assert.That(curve.Evaluate(0.25f), Is.LessThan(0.125f * 0.5f), "Slow to start, so noise near the lower end hardly shows.");
            Assert.Throws<ArgumentException>(() => new ExpressionCurve(0.5f, 0.5f));
        }

        [TestCase(1f, 0.8f, true, 0.9f, 0.9f, true)]
        [TestCase(1f, 0.2f, true, 1f, 0.2f, false)]
        [TestCase(1f, 0.8f, false, 1f, 0.8f, false)]
        public void Eyes_CloseTogether_UnlessOneClearlyWinks(float left, float right, bool sync, float expectedLeft, float expectedRight, bool together)
        {
            (float l, float r, bool t) = ExpressionMapping.Eyes(left, right, sync, 0.5f);

            Assert.That(l, Is.EqualTo(expectedLeft).Within(Tolerance));
            Assert.That(r, Is.EqualTo(expectedRight).Within(Tolerance));
            Assert.That(t, Is.EqualTo(together));
        }

        [Test]
        public void ClosedEyes_Blink_AndAWink_ClosesTheMirroredEye()
        {
            var face = new FacePose();
            var target = new FaceTarget();
            face[FaceShape.EyeBlinkLeft] = 0.9f;
            face[FaceShape.EyeBlinkRight] = 0.85f;
            ExpressionMapping.Default.Evaluate(face, mirror: true, target);
            Assert.That(target[AvatarExpression.Blink], Is.EqualTo(1f).Within(Tolerance));
            Assert.That(target[AvatarExpression.BlinkLeft], Is.EqualTo(0f));

            face[FaceShape.EyeBlinkRight] = 0f;
            ExpressionMapping.Default.Evaluate(face, mirror: false, target);
            Assert.That(target[AvatarExpression.BlinkLeft], Is.EqualTo(1f).Within(Tolerance), "Unmirrored, the person's left eye is the avatar's left.");
            Assert.That(target[AvatarExpression.BlinkRight], Is.EqualTo(0f));

            ExpressionMapping.Default.Evaluate(face, mirror: true, target);
            Assert.That(target[AvatarExpression.BlinkRight], Is.EqualTo(1f).Within(Tolerance), "Mirrored, it is the avatar's right, as in a mirror.");
            Assert.That(target[AvatarExpression.BlinkLeft], Is.EqualTo(0f));
        }

        [Test]
        public void TheMouthShapes_ShareOneOpening()
        {
            var face = new FacePose();
            var target = new FaceTarget();
            face[FaceShape.JawOpen] = 0.8f;
            ExpressionMapping.Default.Evaluate(face, false, target);
            Assert.That(target[AvatarExpression.Aa], Is.EqualTo(1f).Within(Tolerance), "An open jaw alone is \"a\".");

            face[FaceShape.MouthFunnel] = 1f;
            face[FaceShape.MouthPucker] = 1f;
            face[FaceShape.MouthStretchLeft] = 1f;
            face[FaceShape.MouthStretchRight] = 1f;
            face[FaceShape.MouthSmileLeft] = 1f;
            face[FaceShape.MouthSmileRight] = 1f;
            ExpressionMapping.Default.Evaluate(face, false, target);
            float total = target[AvatarExpression.Aa] + target[AvatarExpression.Ih] + target[AvatarExpression.Ou] + target[AvatarExpression.Ee] + target[AvatarExpression.Oh];
            Assert.That(total, Is.EqualTo(1f).Within(Tolerance), "Every shape at once is still one fully open mouth.");
            Assert.That(target[AvatarExpression.Oh], Is.GreaterThan(target[AvatarExpression.Aa]), "A rounded open mouth is \"o\".");
        }

        [Test]
        public void AFeeling_NeedsAllItsSigns_AndStaysWeak()
        {
            var face = new FacePose();
            var target = new FaceTarget();
            face[FaceShape.BrowInnerUp] = 1f;
            ExpressionMapping.Default.Evaluate(face, false, target);
            Assert.That(target[AvatarExpression.Surprised], Is.EqualTo(0f), "Raised brows alone are not surprise.");
            Assert.That(target[AvatarExpression.Sad], Is.EqualTo(0f), "Nor sadness.");

            face[FaceShape.EyeWideLeft] = 1f;
            face[FaceShape.EyeWideRight] = 1f;
            ExpressionMapping.Default.Evaluate(face, false, target);
            Assert.That(target[AvatarExpression.Surprised], Is.EqualTo(0.6f).Within(Tolerance));

            face[FaceShape.MouthSmileLeft] = 1f;
            face[FaceShape.MouthSmileRight] = 1f;
            ExpressionMapping.Default.Evaluate(face, false, target);
            Assert.That(target[AvatarExpression.Happy], Is.EqualTo(0.6f).Within(Tolerance));
        }

        [Test]
        public void TheEyesLook_TheSameWay_MirroredLikeTheHead_AndNotDownWhileClosing()
        {
            var face = new FacePose();
            var target = new FaceTarget();

            // Looking to the person's right: the left eye looks in, the right eye looks out.
            face[FaceShape.EyeLookInLeft] = 0.5f;
            face[FaceShape.EyeLookOutRight] = 0.5f;
            ExpressionMapping.Default.Evaluate(face, false, target);
            Assert.That(target.LookYaw, Is.EqualTo(12f).Within(Tolerance));
            ExpressionMapping.Default.Evaluate(face, true, target);
            Assert.That(target.LookYaw, Is.EqualTo(-12f).Within(Tolerance));

            face[FaceShape.EyeLookDownLeft] = 0.5f;
            face[FaceShape.EyeLookDownRight] = 0.5f;
            ExpressionMapping.Default.Evaluate(face, false, target);
            Assert.That(target.LookPitch, Is.EqualTo(-10f).Within(Tolerance));
            face[FaceShape.EyeBlinkLeft] = 0.9f;
            face[FaceShape.EyeBlinkRight] = 0.9f;
            ExpressionMapping.Default.Evaluate(face, false, target);
            Assert.That(target.LookPitch, Is.EqualTo(0f).Within(Tolerance), "A closed lid is not a look down.");
        }

        [Test]
        public void TheHead_TurnsTheOtherWayInAMirror_NodsTheSame_AndStopsAtItsLimit()
        {
            Quaternion right = Quaternion.AngleAxis(30f, Vector3.up);
            Quaternion nod = Quaternion.AngleAxis(20f, Vector3.right);
            Quaternion tilt = Quaternion.AngleAxis(15f, Vector3.forward);

            AssertSame(HeadPose.FromTracking(right, false), right);
            AssertSame(HeadPose.FromTracking(right, true), Quaternion.AngleAxis(-30f, Vector3.up));
            AssertSame(HeadPose.FromTracking(nod, true), nod);
            AssertSame(HeadPose.FromTracking(tilt, true), Quaternion.AngleAxis(-15f, Vector3.forward));
            AssertSame(HeadPose.FromTracking(Quaternion.AngleAxis(120f, Vector3.up), false), Quaternion.AngleAxis(HeadPose.MaxDegrees, Vector3.up));
            AssertSame(HeadPose.FromTracking(Quaternion.AngleAxis(-120f, Vector3.up), false), Quaternion.AngleAxis(-HeadPose.MaxDegrees, Vector3.up));
            AssertSame(HeadPose.Neck(right), Quaternion.AngleAxis(12f, Vector3.up)); // The neck takes 40% of the turn.
        }

        [Test]
        public void Presence_HoldsALostFace_ThenGoesBack_AndComesBackQuickly()
        {
            Assert.That(FaceMotion.NextPresence(0f, true, 0, 0.1f), Is.EqualTo(0.5f).Within(Tolerance), "Back in 0.2 s.");
            Assert.That(FaceMotion.NextPresence(1f, false, 0.4, 0.1f), Is.EqualTo(1f), "Held for 0.5 s.");
            Assert.That(FaceMotion.NextPresence(1f, false, 0.6, 0.1f), Is.EqualTo(0.9f).Within(Tolerance), "Then gone in 1 s.");
            Assert.That(FaceMotion.NextPresence(0.05f, false, 2, 0.1f), Is.EqualTo(0f));
            Assert.That(FaceMotion.Progress(10.0, 10.0, 0.033), Is.EqualTo(0f));
            Assert.That(FaceMotion.Progress(10.0165, 10.0, 0.033), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(FaceMotion.Progress(10.5, 10.0, 0.033), Is.EqualTo(1f), "A late observation holds the last one.");
        }

        [Test]
        public void Motion_FadesInAFoundFace_MovesBetweenObservations_AndGoesBackWhenLost()
        {
            var motion = new FaceMotion(ExpressionMapping.Default);
            var face = new FacePose();
            face.Status = TrackingStatus.Tracked;
            face.Confidence = 1f;
            face[FaceShape.JawOpen] = 1f;
            face.HeadRotation = Quaternion.AngleAxis(20f, Vector3.up);
            face.Timestamp = 1.0;

            motion.Update(face, false, 1.0);
            Assert.That(motion.Current[AvatarExpression.Aa], Is.EqualTo(0f), "Nothing shown until it fades in.");
            motion.Update(face, false, 1.1);
            Assert.That(motion.Current[AvatarExpression.Aa], Is.EqualTo(0.5f).Within(Tolerance));
            motion.Update(face, false, 1.2);
            Assert.That(motion.Current[AvatarExpression.Aa], Is.EqualTo(1f).Within(Tolerance));
            AssertSame(motion.Current.Head, Quaternion.AngleAxis(20f, Vector3.up));

            // The next observation, 0.1 s later, is reached over that interval.
            face[FaceShape.JawOpen] = 0f;
            face.Timestamp = 1.1;
            motion.Update(face, false, 1.25);
            Assert.That(motion.Current[AvatarExpression.Aa], Is.EqualTo(1f).Within(Tolerance));
            motion.Update(face, false, 1.30);
            Assert.That(motion.Current[AvatarExpression.Aa], Is.EqualTo(0.5f).Within(Tolerance));
            motion.Update(face, false, 1.35);
            Assert.That(motion.Current[AvatarExpression.Aa], Is.EqualTo(0f).Within(Tolerance));

            // Lost: held for 0.5 s after the last observation, then back to neutral over 1 s.
            face[FaceShape.JawOpen] = 1f;
            face.Timestamp = 1.4;
            motion.Update(face, false, 1.4);
            motion.Update(face, false, 1.6);
            face.Status = TrackingStatus.Lost;
            motion.Update(face, false, 1.8);
            Assert.That(motion.Presence, Is.EqualTo(1f));
            Assert.That(motion.Current[AvatarExpression.Aa], Is.EqualTo(1f).Within(Tolerance));
            motion.Update(face, false, 2.4);
            Assert.That(motion.Presence, Is.EqualTo(0.4f).Within(Tolerance));
            motion.Update(face, false, 3.0);
            Assert.That(motion.Current[AvatarExpression.Aa], Is.EqualTo(0f));
            AssertSame(motion.Current.Head, Quaternion.identity);
        }

        [Test]
        public void WithoutTracking_TheFaceIsNeutral()
        {
            var motion = new FaceMotion(ExpressionMapping.Default);

            motion.Update(null, true, 5.0);
            motion.Update(null, true, 5.1);

            Assert.That(motion.Presence, Is.EqualTo(0f));
            Assert.That(motion.Current.Weights, Has.All.EqualTo(0f));
        }

        [Test]
        public void Blinks_MoveToWhatTheModelHas()
        {
            var weights = new float[FaceTarget.ExpressionCount];
            var into = new float[FaceTarget.ExpressionCount];
            weights[(int)AvatarExpression.Blink] = 0.8f;
            Substitute(weights, into, hasBoth: false, hasEach: true);
            Assert.That(into[(int)AvatarExpression.BlinkLeft], Is.EqualTo(0.8f));
            Assert.That(into[(int)AvatarExpression.BlinkRight], Is.EqualTo(0.8f));
            Assert.That(into[(int)AvatarExpression.Blink], Is.EqualTo(0f));

            Array.Clear(weights, 0, weights.Length);
            weights[(int)AvatarExpression.BlinkLeft] = 1f;
            weights[(int)AvatarExpression.BlinkRight] = 0.1f;
            Substitute(weights, into, hasBoth: true, hasEach: false);
            Assert.That(into[(int)AvatarExpression.Blink], Is.EqualTo(0.1f), "A wink does not close both eyes.");
            Assert.That(into[(int)AvatarExpression.BlinkLeft], Is.EqualTo(0f));

            Substitute(weights, into, hasBoth: true, hasEach: true);
            Assert.That(into[(int)AvatarExpression.BlinkLeft], Is.EqualTo(1f), "A model with both kinds gets the values as they are.");
        }

        [Test]
        public void TheController_TurnsTheNeckAndHead_AndSetsTheExpressionsAndGaze()
        {
            var model = new RecordingModel();
            var controller = new AvatarController(model, ExpressionMapping.Default);
            var frame = new TrackingFrame();
            frame.Face.Status = TrackingStatus.Tracked;
            frame.Face.Timestamp = 1.0;
            frame.Face.HeadRotation = Quaternion.AngleAxis(30f, Vector3.up);
            frame.Face[FaceShape.JawOpen] = 1f;
            frame.Face[FaceShape.EyeLookInLeft] = 0.5f;
            frame.Face[FaceShape.EyeLookOutRight] = 0.5f;

            controller.Update(frame, mirror: true, now: 1.0);
            controller.Update(frame, mirror: true, now: 1.5);

            AssertSame(model.Rotations[AvatarBone.Head], Quaternion.AngleAxis(-30f, Vector3.up));
            AssertSame(model.Rotations[AvatarBone.Neck], Quaternion.AngleAxis(-12f, Vector3.up));
            Assert.That(model.Weights[AvatarExpression.Aa], Is.EqualTo(1f).Within(Tolerance));
            Assert.That(model.Yaw, Is.EqualTo(-12f).Within(Tolerance));
            Assert.That(controller.Expressions.Signal[(int)AvatarExpression.Aa], Is.EqualTo(1f).Within(Tolerance), "What was set is the Expression Signal.");
        }

        private static void Substitute(float[] weights, float[] into, bool hasBoth, bool hasEach)
        {
            var has = new bool[FaceTarget.ExpressionCount];
            has[(int)AvatarExpression.Blink] = hasBoth;
            has[(int)AvatarExpression.BlinkLeft] = hasEach;
            has[(int)AvatarExpression.BlinkRight] = hasEach;
            ExpressionController.Substitute(weights, has, into);
        }

        private static void AssertSame(Quaternion actual, Quaternion expected)
        {
            Assert.That(Quaternion.Angle(actual, expected), Is.LessThan(0.01f), $"{actual.eulerAngles} vs {expected.eulerAngles}");
        }

        private sealed class RecordingModel : IAvatarModel, IAvatarRig, IAvatarExpressions
        {
            public Dictionary<AvatarBone, Quaternion> Rotations { get; } = new Dictionary<AvatarBone, Quaternion>();

            public Dictionary<AvatarExpression, float> Weights { get; } = new Dictionary<AvatarExpression, float>();

            public float Yaw { get; private set; }

            public Transform Root => null;

            public IAvatarRig Rig => this;

            public IAvatarExpressions Expressions => this;

            public AvatarModelInfo Info => null;

            public IReadOnlyList<string> CustomNames { get; } = Array.Empty<string>();

            public bool Has(AvatarBone bone)
            {
                return true;
            }

            public float BoneLength(AvatarBone bone)
            {
                return 0.1f;
            }

            public void SetRotation(AvatarBone bone, Quaternion rotation)
            {
                Rotations[bone] = rotation;
            }

            public void SetRootPosition(Vector3 position)
            {
            }

            public void ResetPose()
            {
                Rotations.Clear();
            }

            public bool Has(AvatarExpression expression)
            {
                return true;
            }

            public void SetWeight(AvatarExpression expression, float weight)
            {
                Weights[expression] = weight;
            }

            public void SetLookAt(float yawDegrees, float pitchDegrees)
            {
                Yaw = yawDegrees;
            }

            public void SetVisible(bool visible)
            {
            }

            public void Apply()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
