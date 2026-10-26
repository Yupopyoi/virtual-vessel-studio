using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.Tracking.Processing;
using VirtualVessel.Tracking.Recording;

namespace VirtualVessel.Tracking.Tests
{
    /// <summary>The person's front (tracking detailed design 8.5).</summary>
    public sealed class FacingTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Test]
        public void TheTurn_IsReadFromTheShoulders_OrTheHead()
        {
            BodyPose body = Body(30f);
            Assert.That(FacingCorrection.TryBodyYaw(body, out float degrees), Is.True);
            Assert.That(degrees, Is.EqualTo(30f).Within(0.01f), "Turned to the right: the right shoulder goes back.");
            Assert.That(body[BodyJoint.RightShoulder].z, Is.LessThan(0f));

            body.JointConfidence[(int)BodyJoint.LeftShoulder] = 0f;
            Assert.That(FacingCorrection.TryBodyYaw(body, out _), Is.False, "Not without both shoulders.");

            var face = new FacePose { Status = TrackingStatus.Tracked, HeadRotation = Quaternion.AngleAxis(-20f, Vector3.up) };
            Assert.That(FacingCorrection.TryHeadYaw(face, out degrees), Is.True);
            Assert.That(degrees, Is.EqualTo(-20f).Within(0.01f));
            face.Status = TrackingStatus.Lost;
            Assert.That(FacingCorrection.TryHeadYaw(face, out _), Is.False);
        }

        [Test]
        public void TurningBack_FacesTheBodyFront_AndThe2DBodyKeepsItsPlace()
        {
            var frame = new TrackingFrame();
            frame.Body.CopyFrom(Body(30f));
            frame.Face.HeadRotation = Quaternion.AngleAxis(30f, Vector3.up);
            frame.RightHand.PalmRotation = Quaternion.AngleAxis(30f, Vector3.up);
            frame.RightHand[HandJoint.Wrist] = Quaternion.AngleAxis(30f, Vector3.up) * new Vector3(0.3f, 0.1f, 0.2f);
            frame.Face.HeadPosition = new Vector3(0f, 0.1f, 0.6f);

            FacingCorrection.Turn(frame, FacingCorrection.TurnBack(30f), body: true);

            Assert.That(Vector3.Distance(frame.Body[BodyJoint.RightShoulder], new Vector3(0.18f, 0.45f, 0f)), Is.LessThan(1e-4f));
            Assert.That(Quaternion.Angle(frame.Face.HeadRotation, Quaternion.identity), Is.LessThan(0.01f));
            Assert.That(Quaternion.Angle(frame.RightHand.PalmRotation, Quaternion.identity), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(frame.RightHand[HandJoint.Wrist], new Vector3(0.3f, 0.1f, 0.2f)), Is.LessThan(1e-4f));
            Assert.That(frame.Face.HeadPosition, Is.EqualTo(new Vector3(0f, 0.1f, 0.6f)), "Relative to the camera, not turned.");

            BodyPose flat = Body(0f);
            frame.Body.CopyFrom(flat);
            FacingCorrection.Turn(frame, FacingCorrection.TurnBack(30f), body: false);
            Assert.That(frame.Body[BodyJoint.RightShoulder], Is.EqualTo(flat[BodyJoint.RightShoulder]), "The 2D body faces the camera already.");
        }

        [Test]
        public void TheFront_AddsUp_WithinItsLimit()
        {
            Assert.That(FacingCorrection.Next(0f, 25f), Is.EqualTo(25f));
            Assert.That(FacingCorrection.Next(25f, -5f), Is.EqualTo(20f), "Seen in observations already turned back.");
            Assert.That(FacingCorrection.Next(50f, 30f), Is.EqualTo(FacingCorrection.MaxDegrees));
            Assert.That(FacingCorrection.Next(0f, -90f), Is.EqualTo(-FacingCorrection.MaxDegrees));
        }

        [Test]
        public void TakingTheFront_TurnsTheObservationsBack_AndIsSaved()
        {
            // The replayed person faces the camera; with a front 20 degrees to the right, the observations
            // are turned 20 degrees left, and taking the front again finds the camera's.
            var clock = new TrackingServiceTests.SecondsClock();
            var loop = new FrameLoop();
            ConfigFileStore<TrackingSettings> store = TrackingService.CreateStore(_directory, new UtcSystemClock());
            var saves = new ConfigSaveScheduler<TrackingSettings>(store, clock, TimeSpan.FromSeconds(0.5), null);
            TrackingFrame[] frames = SyntheticMotion.RaiseAndWaveFrames(2, 30, 0f, seed: 0);
            var provider = new TrackingReplayProvider(frames, TrackingRegions.Body | TrackingRegions.Face, () => clock.Seconds, loop: true);
            var service = new TrackingService(provider, new TrackingServiceTests.FakeCamera(), loop, clock, new TrackingSettings { FrontDegrees = 20f }, saves, null, null);
            service.InitializeAsync(CancellationToken.None).Wait();
            try
            {
                Assert.That(service.TakeFront(), Is.False, "Nobody is seen before tracking starts.");
                Assert.That(service.Front.Value, Is.EqualTo(20f));

                service.Start();
                for (int i = 0; i < 30; i++)
                {
                    clock.Seconds += 1.0 / 60;
                    loop.Tick(null);
                }

                Assert.That(FacingCorrection.TryBodyYaw(service.Current.Body, out float seen), Is.True);
                Assert.That(seen, Is.EqualTo(-20f).Within(0.5f), "Turned back by the front.");

                Assert.That(service.TakeFront(), Is.True);
                Assert.That(service.Front.Value, Is.EqualTo(0f).Within(0.5f), "Facing the camera is the camera's front.");

                service.ResetFront();
                Assert.That(service.Front.Value, Is.EqualTo(0f));
                service.ShutdownAsync(CancellationToken.None).Wait();
                Assert.That(store.Load().Value.FrontDegrees, Is.EqualTo(0f));
            }
            finally
            {
                service.Dispose();
            }
        }

        /// <summary>Shoulders seen in the image, turned <paramref name="degrees"/> to the right about the hips.</summary>
        private static BodyPose Body(float degrees)
        {
            var body = new BodyPose { Status = TrackingStatus.Tracked, Confidence = 1f };
            Quaternion turn = Quaternion.AngleAxis(degrees, Vector3.up);
            body[BodyJoint.RightShoulder] = turn * new Vector3(0.18f, 0.45f, 0f);
            body[BodyJoint.LeftShoulder] = turn * new Vector3(-0.18f, 0.45f, 0f);
            for (int i = 0; i < BodyPose.JointCount; i++)
            {
                body.JointConfidence[i] = 1f;
                body.ImagePositions[i] = new Vector2(0.5f, 0.5f);
            }

            return body;
        }
    }
}
