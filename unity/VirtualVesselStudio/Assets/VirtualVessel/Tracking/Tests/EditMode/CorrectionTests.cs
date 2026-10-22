using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Tracking.Processing;
using VirtualVessel.Tracking.Recording;

namespace VirtualVessel.Tracking.Tests
{
    /// <summary>Corrections of raw observations by the kind of input (tracking detailed design 8.4).</summary>
    public sealed class CorrectionTests
    {
        private static readonly Vector3 s_shoulder = new Vector3(0.18f, 0.45f, 0f);

        [Test]
        public void AnArmPointingAtTheCamera_IsStraightened_WithTheHandKept()
        {
            // Thrust forward, bent about 35 degrees at the elbow, as MediaPipe saw the user's arms.
            Vector3 elbow = s_shoulder + new Vector3(0f, -0.08f, 0.26f);
            Vector3 wrist = elbow + new Vector3(0f, 0.08f, 0.24f);

            Vector3 straightened = CameraAxisArmStraightening.Straightened(s_shoulder, elbow, wrist, Vector3.forward, CameraAxisArmStraightening.Start, CameraAxisArmStraightening.Full);

            Assert.That(Vector3.Angle(straightened - s_shoulder, wrist - straightened), Is.LessThan(0.5f), "Straight.");
            Assert.That(Vector3.Angle(elbow - s_shoulder, wrist - elbow), Is.GreaterThan(30f), "It was bent.");
        }

        [Test]
        public void AnArmNotPointingAtTheCamera_KeepsItsBend()
        {
            // Out sideways with the forearm bent forward, and hands clasped in front of the chest.
            Vector3 elbow = s_shoulder + new Vector3(0.28f, 0f, 0f);
            Vector3 wrist = elbow + new Vector3(0f, 0f, 0.25f);
            Assert.That(CameraAxisArmStraightening.Straightened(s_shoulder, elbow, wrist, Vector3.forward, 0.5f, 0.8f), Is.EqualTo(elbow));

            elbow = s_shoulder + new Vector3(0f, -0.25f, 0.12f);
            wrist = elbow + new Vector3(-0.2f, 0.02f, 0.14f);
            Assert.That(CameraAxisArmStraightening.Straightened(s_shoulder, elbow, wrist, Vector3.forward, 0.5f, 0.8f), Is.EqualTo(elbow));
        }

        [Test]
        public void HalfwayToTheCamera_StraightensPartly()
        {
            // Both parts at about 0.65 toward the camera: between the start (0.5) and full (0.8).
            Vector3 elbow = s_shoulder + new Vector3(0f, -0.21f, 0.2f);
            Vector3 wrist = elbow + new Vector3(0f, 0.19f, 0.19f);
            Vector3 straightened = CameraAxisArmStraightening.Straightened(s_shoulder, elbow, wrist, Vector3.forward, 0.5f, 0.8f);

            float before = Vector3.Angle(elbow - s_shoulder, wrist - elbow);
            float after = Vector3.Angle(straightened - s_shoulder, wrist - straightened);
            Assert.That(after, Is.GreaterThan(1f).And.LessThan(before));
        }

        [Test]
        public void Corrections_AreChosenByWhatTheInputObserves()
        {
            ObservationCorrections camera = ObservationCorrections.For(ObservationTraits.MonocularDepth, ObservationCorrections.Catalog);
            Assert.That(camera.Corrections.OfType<CameraAxisArmStraightening>().Count(), Is.EqualTo(1));

            // Sensors on the body (mocopi, for example) have no camera axis to correct for.
            Assert.That(ObservationCorrections.For(ObservationTraits.None, ObservationCorrections.Catalog).Any, Is.False);
            Assert.That(ObservationCorrections.For(ObservationTraits.MonocularDepth, null).Any, Is.False);
        }

        [Test]
        public void Tracking_CorrectsTheCameraButRecordsWhatItObserved()
        {
            Vector3 bentElbow = AssertCorrected(ObservationTraits.MonocularDepth, out Vector3 recordedElbow, out Vector3 current, out Vector3 wrist);
            Assert.That(Vector3.Angle(current - s_shoulder, wrist - current), Is.LessThan(3f), "The arm is straightened.");
            Assert.That(recordedElbow, Is.EqualTo(bentElbow), "Recordings keep the raw observation, so a replay is corrected again.");

            AssertCorrected(ObservationTraits.None, out _, out current, out wrist);
            Assert.That(Vector3.Angle(current - s_shoulder, wrist - current), Is.GreaterThan(30f), "Another kind of input keeps what it observed.");
        }

        /// <summary>Runs tracking on a forward arm bent 35 degrees from a provider of <paramref name="traits"/>; returns the raw elbow.</summary>
        private static Vector3 AssertCorrected(ObservationTraits traits, out Vector3 recordedElbow, out Vector3 currentElbow, out Vector3 wrist)
        {
            var clock = new TrackingServiceTests.SecondsClock();
            var loop = new FrameLoop();
            TrackingFrame[] frames = SyntheticMotion.RaiseAndWaveFrames(1, 30, 0f, seed: 0);
            Vector3 elbow = s_shoulder + new Vector3(0f, -0.08f, 0.26f);
            Vector3 hand = elbow + new Vector3(0f, 0.08f, 0.24f);
            foreach (TrackingFrame frame in frames)
            {
                frame.Body[BodyJoint.RightShoulder] = s_shoulder;
                frame.Body[BodyJoint.RightElbow] = elbow;
                frame.Body[BodyJoint.RightWrist] = hand;
            }

            var provider = new TrackingReplayProvider(frames, TrackingRegions.Body, () => clock.Seconds, loop: true, traits);
            using var service = new TrackingService(provider, null, loop, clock, new TrackingSettings(), null, null, null);
            service.InitializeAsync(CancellationToken.None).Wait();
            Vector3 recorded = Vector3.zero;
            service.Observed = raw => recorded = raw.Body[BodyJoint.RightElbow];
            service.Start();
            for (int i = 0; i < 40; i++)
            {
                clock.Seconds += 1.0 / 60;
                loop.Tick(null);
            }

            recordedElbow = recorded;
            currentElbow = service.Current.Body[BodyJoint.RightElbow];
            wrist = service.Current.Body[BodyJoint.RightWrist];
            return elbow;
        }
    }
}
