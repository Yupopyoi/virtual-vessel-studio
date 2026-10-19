using System;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Tracking.MediaPipe;
using VirtualVessel.Tracking.Recording;

namespace VirtualVessel.Tracking.Tests
{
    /// <summary>
    /// MediaPipe's coordinates and names to the common form (tracking detailed design 6). The inputs are
    /// what MediaPipe reports for known poses: world x to the right of the image, y down, z away from
    /// the camera, and joints named by the person's own side.
    /// </summary>
    public sealed class MediaPipeConversionTests
    {
        private static Vector3[] PoseWorld()
        {
            var world = new Vector3[MediaPipeConversion.PoseLandmarkCount];

            // A person facing the camera: their right shoulder (12) is on the image's left.
            world[0] = new Vector3(0f, -0.62f, -0.1f);
            world[7] = new Vector3(0.08f, -0.62f, 0f);
            world[8] = new Vector3(-0.08f, -0.62f, 0f);
            world[11] = new Vector3(0.18f, -0.45f, 0f);
            world[12] = new Vector3(-0.18f, -0.45f, 0f);
            world[13] = new Vector3(0.18f, -0.17f, 0f);

            // The right arm raised level to the side, the hand reaching 0.3 m toward the camera.
            world[14] = new Vector3(-0.46f, -0.45f, 0f);
            world[16] = new Vector3(-0.66f, -0.45f, -0.3f);
            world[15] = new Vector3(0.18f, 0.08f, 0f);
            world[23] = new Vector3(0.1f, 0f, 0f);
            world[24] = new Vector3(-0.1f, 0f, 0f);
            return world;
        }

        private static BodyPose ConvertedBody(float visibility = 0.9f)
        {
            var image = new Vector2[MediaPipeConversion.PoseLandmarkCount];
            image[11] = new Vector2(0.6f, 0.4f);
            image[12] = new Vector2(0.4f, 0.4f);
            var visible = new float[MediaPipeConversion.PoseLandmarkCount];
            for (int i = 0; i < visible.Length; i++)
            {
                visible[i] = visibility;
            }

            var body = new BodyPose();
            MediaPipeConversion.WriteBody(PoseWorld(), image, visible, 12.5, body);
            return body;
        }

        [Test]
        public void ThePersonsRight_IsPlusX_Up_IsPlusY_AndTowardTheCamera_IsPlusZ()
        {
            BodyPose body = ConvertedBody();

            Assert.That(body[BodyJoint.RightShoulder], Is.EqualTo(new Vector3(0.18f, 0.45f, 0f)));
            Assert.That(body[BodyJoint.LeftShoulder].x, Is.LessThan(0f));
            Assert.That(body[BodyJoint.RightWrist].z, Is.EqualTo(0.3f).Within(1e-6f), "Closer to the camera is the person's front.");
            Assert.That(SyntheticMotion.ArmAngle(body, right: true), Is.EqualTo(90f).Within(0.01f), "The right arm is level to the right.");
            Assert.That(SyntheticMotion.ArmAngle(body, right: false), Is.EqualTo(0f).Within(0.01f), "The left arm hangs down.");
        }

        [Test]
        public void Midpoints_StandForTheJointsMediaPipeHasNoPointFor()
        {
            BodyPose body = ConvertedBody();

            Assert.That(body[BodyJoint.Neck], Is.EqualTo(new Vector3(0f, 0.45f, 0f)));
            Assert.That(body[BodyJoint.Hips], Is.EqualTo(Vector3.zero));
            Assert.That(body[BodyJoint.Head], Is.EqualTo(new Vector3(0f, 0.62f, 0f)));
            Assert.That(body.ImagePositions[(int)BodyJoint.Neck], Is.EqualTo(new Vector2(0.5f, 0.4f)));
        }

        [Test]
        public void TheBody_IsTrackedAtTheImageTime_WithTheUpperBodysVisibility()
        {
            BodyPose body = ConvertedBody(visibility: 0.4f);

            Assert.That(body.Status, Is.EqualTo(TrackingStatus.Tracked));
            Assert.That(body.Timestamp, Is.EqualTo(12.5));
            Assert.That(body.Confidence, Is.EqualTo(0.4f).Within(1e-6f));
        }

        [TestCase("eyeBlinkLeft", FaceShape.EyeBlinkLeft)]
        [TestCase("jawOpen", FaceShape.JawOpen)]
        [TestCase("mouthSmileRight", FaceShape.MouthSmileRight)]
        public void BlendshapeNames_AreFaceShapes(string name, FaceShape shape)
        {
            Assert.That(MediaPipeConversion.TryParseShape(name, out FaceShape parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(shape));
        }

        [TestCase("_neutral")]
        [TestCase("somethingNew")]
        [TestCase("12")]
        [TestCase("")]
        public void OtherNames_AreNotShapes(string name)
        {
            Assert.That(MediaPipeConversion.TryParseShape(name, out _), Is.False);
        }

        [Test]
        public void OtherSide_SwapsLeftAndRight_AndKeepsTheRest()
        {
            Assert.That(MediaPipeConversion.OtherSide(FaceShape.EyeBlinkLeft), Is.EqualTo(FaceShape.EyeBlinkRight));
            Assert.That(MediaPipeConversion.OtherSide(FaceShape.MouthSmileRight), Is.EqualTo(FaceShape.MouthSmileLeft));
            Assert.That(MediaPipeConversion.OtherSide(FaceShape.JawOpen), Is.EqualTo(FaceShape.JawOpen));
            foreach (FaceShape shape in Enum.GetValues(typeof(FaceShape)))
            {
                Assert.That(MediaPipeConversion.OtherSide(MediaPipeConversion.OtherSide(shape)), Is.EqualTo(shape), shape.ToString());
            }
        }

        [Test]
        public void AFaceLookingIntoTheCamera_HasNoHeadRotation()
        {
            Assert.That(Quaternion.Angle(MediaPipeConversion.HeadRotation(Matrix4x4.identity), Quaternion.identity), Is.LessThan(0.01f));
        }

        [Test]
        public void TurningToTheRight_AndLookingUp_ComeOutInPersonSpace()
        {
            // In the plugin's camera space, a face turning to the person's right turns toward the
            // image's left; looking up tilts it toward +y.
            Matrix4x4 turnedRight = Matrix4x4.Rotate(Quaternion.AngleAxis(30f, Vector3.up));
            Matrix4x4 lookingUp = Matrix4x4.Rotate(Quaternion.AngleAxis(20f, Vector3.right));

            Vector3 right = MediaPipeConversion.HeadRotation(turnedRight) * Vector3.forward;
            Vector3 up = MediaPipeConversion.HeadRotation(lookingUp) * Vector3.forward;
            Assert.That(right.x, Is.GreaterThan(0.4f), "Toward the person's right.");
            Assert.That(up.y, Is.GreaterThan(0.3f), "Up.");
        }

        [Test]
        public void TheHeadPosition_IsInMetersInPersonSpace()
        {
            Matrix4x4 matrix = Matrix4x4.Translate(new Vector3(10f, 5f, 60f));

            Vector3 position = MediaPipeConversion.HeadPosition(matrix);
            Assert.That(position.x, Is.EqualTo(-0.1f).Within(1e-6f));
            Assert.That(position.y, Is.EqualTo(0.05f).Within(1e-6f));
            Assert.That(position.z, Is.EqualTo(-0.6f).Within(1e-6f));
        }

        [Test]
        public void LookingIn_TurnsEachEyeTowardTheNose()
        {
            Assert.That(MediaPipeConversion.Gaze(1f, 0f, 0f, 0f, leftEye: true).x, Is.EqualTo(MediaPipeConversion.MaxGazeDegrees), "The left eye looks to the person's right.");
            Assert.That(MediaPipeConversion.Gaze(1f, 0f, 0f, 0f, leftEye: false).x, Is.EqualTo(-MediaPipeConversion.MaxGazeDegrees));
            Assert.That(MediaPipeConversion.Gaze(0f, 0f, 1f, 0f, leftEye: true).y, Is.EqualTo(MediaPipeConversion.MaxGazeDegrees));
        }

        [Test]
        public void FaceValues_AreClampedAndGiveTheGaze()
        {
            var shapes = new float[FacePose.ShapeCount];
            shapes[(int)FaceShape.JawOpen] = 1.2f;
            shapes[(int)FaceShape.EyeLookOutRight] = 1f;
            var face = new FacePose();

            MediaPipeConversion.WriteFace(shapes, FacePose.AllShapesExcept(FaceShape.TongueOut), Matrix4x4.identity, 3.0, face);

            Assert.That(face[FaceShape.JawOpen], Is.EqualTo(1f));
            Assert.That(face.RightEyeGaze.x, Is.EqualTo(MediaPipeConversion.MaxGazeDegrees), "The right eye looking out looks to the person's right.");
            Assert.That(face.Supports(FaceShape.TongueOut), Is.False);
            Assert.That(face.Status, Is.EqualTo(TrackingStatus.Tracked));
        }

        [Test]
        public void APalmDownWithFingersForward_HasNoRotation_OnEitherHand()
        {
            // The right hand's thumb side is toward the person's left (-X) when the palm faces down.
            Quaternion right = MediaPipeConversion.PalmRotation(Vector3.zero, new Vector3(-0.03f, 0f, 0.08f), new Vector3(0f, 0f, 0.09f), new Vector3(0.04f, 0f, 0.07f), left: false);
            Quaternion left = MediaPipeConversion.PalmRotation(Vector3.zero, new Vector3(0.03f, 0f, 0.08f), new Vector3(0f, 0f, 0.09f), new Vector3(-0.04f, 0f, 0.07f), left: true);

            Assert.That(Quaternion.Angle(right, Quaternion.identity), Is.LessThan(1f));
            Assert.That(Quaternion.Angle(left, Quaternion.identity), Is.LessThan(1f));
        }

        [Test]
        public void AHand_IsMovedToTheBodysWrist()
        {
            var world = new Vector3[MediaPipeConversion.HandLandmarkCount];
            var image = new Vector2[MediaPipeConversion.HandLandmarkCount];
            world[0] = new Vector3(0.01f, 0.02f, 0.03f);
            world[8] = new Vector3(0.01f, -0.08f, 0.03f);
            var hand = new HandPose();

            MediaPipeConversion.WriteHand(world, image, 0.9f, new Vector3(0.5f, 0.4f, 0.2f), left: false, 7.0, hand);

            Assert.That(hand[HandJoint.Wrist], Is.EqualTo(new Vector3(0.5f, 0.4f, 0.2f)));
            Assert.That(hand[HandJoint.IndexTip].y, Is.EqualTo(0.5f).Within(1e-6f), "The fingertip 0.1 m above the wrist (MediaPipe's y is down).");
            Assert.That(hand.Timestamp, Is.EqualTo(7.0));
        }

        [Test]
        public void AHand_GoesToTheNearerBodyWrist_NotByHandedness()
        {
            var leftWrist = new Vector2(0.7f, 0.6f);
            var rightWrist = new Vector2(0.3f, 0.6f);

            Assert.That(MediaPipeConversion.IsLeft(new Vector2(0.68f, 0.55f), true, leftWrist, rightWrist, "Left"), Is.True);
            Assert.That(MediaPipeConversion.IsLeft(new Vector2(0.32f, 0.55f), true, leftWrist, rightWrist, "Right"), Is.False);
        }

        [Test]
        public void WithoutTheBody_HandednessDecides_AsForAMirroredImage()
        {
            Assert.That(MediaPipeConversion.IsLeft(Vector2.zero, false, Vector2.zero, Vector2.zero, "Right"), Is.True);
            Assert.That(MediaPipeConversion.IsLeft(Vector2.zero, false, Vector2.zero, Vector2.zero, "Left"), Is.False);
        }

        [Test]
        public void AHandOrFaceFarFromTheBody_IsNotThePersons()
        {
            var bodyWrist = new Vector2(0.3f, 0.6f);

            Assert.That(MediaPipeConversion.IsNearBody(new Vector2(0.32f, 0.62f), bodyWrist, bodySeen: true), Is.True);
            Assert.That(MediaPipeConversion.IsNearBody(new Vector2(0.8f, 0.2f), bodyWrist, bodySeen: true), Is.False, "Something behind the person.");
            Assert.That(MediaPipeConversion.IsNearBody(new Vector2(0.8f, 0.2f), bodyWrist, bodySeen: false), Is.True, "Nothing to compare with.");
        }

        [Test]
        public void TheFaceNearestTheBodysNose_IsChosen()
        {
            var noses = new[] { new Vector2(0.9f, 0.1f), new Vector2(0.52f, 0.31f) };

            Assert.That(MediaPipeConversion.ChooseFace(noses, 2, new Vector2(0.5f, 0.3f), bodySeen: true), Is.EqualTo(1), "Not the poster in the corner.");
            Assert.That(MediaPipeConversion.ChooseFace(noses, 1, new Vector2(0.5f, 0.3f), bodySeen: true), Is.EqualTo(-1), "The only face is far from the body.");
            Assert.That(MediaPipeConversion.ChooseFace(noses, 2, default, bodySeen: false), Is.EqualTo(0), "Without the body, the most confident.");
            Assert.That(MediaPipeConversion.ChooseFace(noses, 0, default, bodySeen: false), Is.EqualTo(-1));
        }

        [Test]
        public void TwoHands_NeverGoToTheSameSide()
        {
            var leftWrist = new Vector2(0.7f, 0.6f);
            var rightWrist = new Vector2(0.3f, 0.6f);

            // Both are nearer the left wrist; the pairing puts the nearer one there and the other on the right.
            Assert.That(MediaPipeConversion.FirstIsLeft(new Vector2(0.6f, 0.6f), new Vector2(0.75f, 0.6f), true, leftWrist, rightWrist, null), Is.False);
            Assert.That(MediaPipeConversion.FirstIsLeft(new Vector2(0.72f, 0.6f), new Vector2(0.5f, 0.6f), true, leftWrist, rightWrist, null), Is.True);
        }
    }
}
