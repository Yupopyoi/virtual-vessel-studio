using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Avatar;
using VirtualVessel.Core.Time;
using VirtualVessel.Tracking;
using VirtualVessel.Tracking.Recording;
using VirtualVessel.UI.Screens;
using Object = UnityEngine.Object;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The skeleton comparison Card on the Diagnostics screen (UI foundation detailed design 7.7).</summary>
    public sealed class SkeletonPanelTests
    {
        private static readonly string[] s_names = { "LE", "LF", "LB", "RE", "RF", "RB", "TT", "TL" };

        private string _directory;
        private GameObject _host;
        private UiRoot _root;
        private LiveSetupPanelTests.FakeTracking _tracking;
        private AvatarCardPanelTests.FakeAvatars _avatars;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _tracking = new LiveSetupPanelTests.FakeTracking();
            _avatars = new AvatarCardPanelTests.FakeAvatars();
            _host = new GameObject(nameof(SkeletonPanelTests));
            _host.AddComponent<UIDocument>();
            _root = _host.AddComponent<UiRoot>();
            _root.SetAssets(UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset"));
            _root.Build(new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false, screens: new ScreenServices { Tracking = _tracking, Avatars = _avatars }));
            _root.Shell.DeveloperMode = true;
            _root.Shell.Open("diagnostics");
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_host);
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [UnityTest]
        public IEnumerator TheTrackedBody_AndTheAvatars_AreDrawnAndMeasured()
        {
            SkeletonCard card = _root.Content.Q<SkeletonCard>();
            Assert.That(card, Is.Not.Null, "Placed on the Diagnostics screen.");
            Assert.That(card.IsRefreshing, Is.True, "Refreshing while the screen is shown.");
            Assert.That(card.Front.SegmentCount, Is.EqualTo(0), "Nothing to draw without a body.");

            Body(_tracking.Current.Body, Vector3.left, Vector3.right);
            var avatar = new BodyTarget();
            _avatars.Body = avatar;
            yield return new WaitForSeconds(0.15f);

            Assert.That(card.Front.SegmentCount, Is.EqualTo(SkeletonProjection.MaxSegments), "The tracked and the avatar's lines.");
            Assert.That(card.Side.SegmentCount, Is.EqualTo(SkeletonProjection.MaxSegments));
            Assert.That(card.Table.RowCount, Is.EqualTo(8));

            _root.Shell.Open("live");
            yield return null;
            Assert.That(card.IsRefreshing, Is.False, "Stops while hidden.");
        }

        [Test]
        public void TheAngles_ComparedRowByRow()
        {
            // Tracked: both arms out sideways and straight. The avatar: its right arm reaches forward.
            var body = new BodyPose();
            Body(body, Vector3.left, Vector3.right);
            var avatar = new BodyTarget();
            avatar[UpperBodyBone.RightUpperArm] = Quaternion.FromToRotation(Vector3.right, Vector3.forward);
            avatar[UpperBodyBone.RightLowerArm] = avatar[UpperBodyBone.RightUpperArm];

            string[][] rows = SkeletonCard.Rows(body, avatar, mirror: false, s_names);

            Assert.That(rows[3], Is.EqualTo(new[] { "RE", "0°", "0°" }), "Both level.");
            Assert.That(rows[4], Is.EqualTo(new[] { "RF", "0°", "90°" }), "The avatar's arm turned forward; tracking's not.");
            Assert.That(rows[5], Is.EqualTo(new[] { "RB", "0°", "0°" }));
            Assert.That(rows[6][1], Is.EqualTo("0°"), "A level torso.");
            Assert.That(SkeletonCard.Rows(null, null, false, s_names)[0], Is.EqualTo(new[] { "LE", "-", "-" }), "Nothing known.");
        }

        [Test]
        public void ALineInMeters_FitsTheBox_UpBeingUp()
        {
            Vector2 hips = SkeletonView.ToBox(Vector2.zero, 360f, 280f);
            Vector2 head = SkeletonView.ToBox(new Vector2(0f, 0.6f), 360f, 280f);
            Assert.That(hips.x, Is.EqualTo(180f).Within(0.01f), "Centered across.");
            Assert.That(head.y, Is.LessThan(hips.y), "Up on screen.");
            Assert.That(SkeletonProjection.Project(new Vector3(0.1f, 0.2f, 0.3f), SkeletonPlane.Side), Is.EqualTo(new Vector2(0.3f, 0.2f)), "The side shows the front to the right.");
        }

        [Test]
        public void TheFace_IsFound_OrTheWholeImageIsCovered()
        {
            var body = new BodyPose();
            Body(body, Vector3.down, Vector3.down);
            body.ImagePositions[(int)BodyJoint.LeftShoulder] = new Vector2(0.6f, 0.6f);
            body.ImagePositions[(int)BodyJoint.RightShoulder] = new Vector2(0.4f, 0.6f);
            body.ImagePositions[(int)BodyJoint.LeftEar] = new Vector2(0.55f, 0.3f);
            body.ImagePositions[(int)BodyJoint.RightEar] = new Vector2(0.45f, 0.3f);
            body.ImagePositions[(int)BodyJoint.Nose] = new Vector2(0.5f, 0.32f);

            Assert.That(FaceCover.TryLocate(body, 1000f, 500f, out Vector2 center, out Vector2 radii), Is.True);
            Assert.That(center, Is.EqualTo(new Vector2(500f, 150f)), "Between the ears, in pixels.");
            Assert.That(radii, Is.EqualTo(new Vector2(200f * FaceCover.HalfWidthPerShoulder, 200f * FaceCover.HalfHeightPerShoulder)), "From the shoulder width, which stays when the head turns.");

            body.JointConfidence[(int)BodyJoint.LeftEar] = 0.1f;
            FaceCover.TryLocate(body, 1000f, 500f, out center, out _);
            Assert.That(center, Is.EqualTo(new Vector2(500f, 160f)), "On the nose when an ear is hidden.");

            body.JointConfidence[(int)BodyJoint.Nose] = 0.1f;
            Assert.That(FaceCover.TryLocate(body, 1000f, 500f, out _, out _), Is.False, "Not known: the whole image is covered.");
            body.JointConfidence[(int)BodyJoint.Nose] = 0.9f;
            body.Status = TrackingStatus.Lost;
            Assert.That(FaceCover.TryLocate(body, 1000f, 500f, out _, out _), Is.False);
            Assert.That(FaceCover.TryLocate(null, 1000f, 500f, out _, out _), Is.False);
        }

        private static void Body(BodyPose body, Vector3 leftArm, Vector3 rightArm)
        {
            SyntheticMotion.WriteArms(body, leftArm, leftArm, rightArm, rightArm, 0.0);
        }
    }
}
