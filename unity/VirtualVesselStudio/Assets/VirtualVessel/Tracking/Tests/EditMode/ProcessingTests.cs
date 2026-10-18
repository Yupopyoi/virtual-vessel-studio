using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Tracking.Processing;
using VirtualVessel.Tracking.Recording;
using VirtualVessel.Tracking.Threading;

namespace VirtualVessel.Tracking.Tests
{
    public sealed class OutlierGateTests
    {
        [Test]
        public void APlausibleMove_IsAccepted()
        {
            Assert.That(default(OutlierGate).Accept(Vector3.zero, new Vector3(0.1f, 0, 0), 1.0 / 30, 8f, out _), Is.True, "3 m/s.");
        }

        [Test]
        public void AJump_IsDroppedTwice_ThenAccepted()
        {
            OutlierGate gate = default;
            var far = new Vector3(1f, 0, 0);

            Assert.That(gate.Accept(Vector3.zero, far, 1.0 / 30, 8f, out gate), Is.False, "30 m/s.");
            Assert.That(gate.Accept(Vector3.zero, far, 2.0 / 30, 8f, out gate), Is.False);
            Assert.That(gate.Accept(Vector3.zero, far, 3.0 / 30, 8f, out gate), Is.True, "Still there: the move was real.");
            Assert.That(gate.Accept(far, far * 3, 1.0 / 30, 8f, out _), Is.False, "Counting started over.");
        }
    }

    public sealed class TrackingStateMachineTests
    {
        private static readonly TrackingThresholds s_thresholds = TrackingThresholds.Default;

        private static PartTracking Observe(PartTracking state, double time, float confidence = 0.9f)
        {
            return TrackingStateMachine.Next(state, true, true, true, confidence, time, time, s_thresholds);
        }

        private static PartTracking Wait(PartTracking state, double now)
        {
            return TrackingStateMachine.Next(state, true, true, false, 0f, 0, now, s_thresholds);
        }

        [Test]
        public void ItBecomesTracked_OnTheSecondObservationInARow()
        {
            PartTracking state = Observe(PartTracking.Initial, 0.0);
            Assert.That(state.Status, Is.EqualTo(TrackingStatus.Lost));
            state = Observe(state, 1.0 / 30);
            Assert.That(state.Status, Is.EqualTo(TrackingStatus.Tracked));
        }

        [Test]
        public void LowConfidence_IsReportedAsSuch()
        {
            PartTracking state = Observe(Observe(PartTracking.Initial, 0.0), 1.0 / 30, 0.3f);
            Assert.That(state.Status, Is.EqualTo(TrackingStatus.LowConfidence));
        }

        [Test]
        public void ItIsLost_OnlyAfterTheTimeout()
        {
            PartTracking state = Observe(Observe(PartTracking.Initial, 0.0), 0.1);

            Assert.That(Wait(state, 0.25).Status, Is.EqualTo(TrackingStatus.Tracked), "A frame or two missing does not flicker.");
            PartTracking lost = Wait(state, 0.31);
            Assert.That(lost.Status, Is.EqualTo(TrackingStatus.Lost));
            Assert.That(lost.LastObserved, Is.EqualTo(0.1), "Remembers since when.");
        }

        [Test]
        public void DisabledAndNotSupported_WinOverObservations()
        {
            Assert.That(TrackingStateMachine.Next(PartTracking.Initial, true, false, true, 1f, 0, 0, s_thresholds).Status, Is.EqualTo(TrackingStatus.Disabled));
            Assert.That(TrackingStateMachine.Next(PartTracking.Initial, false, true, true, 1f, 0, 0, s_thresholds).Status, Is.EqualTo(TrackingStatus.NotSupported));
        }
    }

    public sealed class TrackingProcessorTests
    {
        private const double Interval = 1.0 / 30;

        private static TrackingFrame Observation(double time, float rightArm = 0f)
        {
            var frame = new TrackingFrame { Time = time };
            SyntheticMotion.WriteBody(frame.Body, rightArm, time);
            frame.Face.Status = TrackingStatus.Tracked;
            frame.Face.Confidence = 0.9f;
            frame.Face.Timestamp = time;
            frame.LeftHand.Status = TrackingStatus.Lost;
            frame.RightHand.Status = TrackingStatus.Lost;
            return frame;
        }

        private static void Run(TrackingProcessor processor, TrackingFrame output, int from, int to, TrackingRegions enabled = TrackingRegions.All)
        {
            for (int i = from; i < to; i++)
            {
                processor.Process(Observation(i * Interval), i * Interval, TrackingRegions.All, enabled, SmoothingPreset.Standard, output);
            }
        }

        [Test]
        public void ObservedParts_AreTrackedAndSmoothed_AndMissingOnesStayLost()
        {
            var processor = new TrackingProcessor();
            var output = new TrackingFrame();
            Run(processor, output, 0, 5);

            Assert.That(output.Body.Status, Is.EqualTo(TrackingStatus.Tracked));
            Assert.That(output.Face.Status, Is.EqualTo(TrackingStatus.Tracked));
            Assert.That(output.LeftHand.Status, Is.EqualTo(TrackingStatus.Lost));
            Assert.That(output.Body[BodyJoint.RightShoulder], Is.EqualTo(new Vector3(0.18f, 0.45f, 0f)));
            Assert.That(output.Body.Timestamp, Is.EqualTo(4 * Interval));
        }

        [Test]
        public void WithoutNewObservations_ValuesAreHeld_ThenTheBodyIsLost()
        {
            var processor = new TrackingProcessor();
            var output = new TrackingFrame();
            Run(processor, output, 0, 5);
            Vector3 elbow = output.Body[BodyJoint.RightElbow];

            processor.Process(null, (4 * Interval) + 0.1, TrackingRegions.All, TrackingRegions.All, SmoothingPreset.Standard, output);
            Assert.That(output.Body.Status, Is.EqualTo(TrackingStatus.Tracked));
            processor.Process(null, (4 * Interval) + 0.3, TrackingRegions.All, TrackingRegions.All, SmoothingPreset.Standard, output);
            Assert.That(output.Body.Status, Is.EqualTo(TrackingStatus.Lost));
            Assert.That(output.Body[BodyJoint.RightElbow], Is.EqualTo(elbow), "The last values stay for Avatar to decide.");
            Assert.That(output.Body.Timestamp, Is.EqualTo(4 * Interval), "Since when it is lost.");
        }

        [Test]
        public void AfterALoss_TheNewPoseIsTakenWithoutSmoothingAcrossTheGap()
        {
            var processor = new TrackingProcessor();
            var output = new TrackingFrame();
            Run(processor, output, 0, 5);
            processor.Process(null, 1.0, TrackingRegions.All, TrackingRegions.All, SmoothingPreset.Standard, output);
            Assert.That(output.Body.Status, Is.EqualTo(TrackingStatus.Lost));

            processor.Process(Observation(1.1, rightArm: 90f), 1.1, TrackingRegions.All, TrackingRegions.All, SmoothingPreset.Standard, output);
            processor.Process(Observation(1.1 + Interval, rightArm: 90f), 1.1 + Interval, TrackingRegions.All, TrackingRegions.All, SmoothingPreset.Standard, output);

            Assert.That(output.Body.Status, Is.EqualTo(TrackingStatus.Tracked));
            Assert.That(SyntheticMotion.ArmAngle(output.Body, right: true), Is.EqualTo(90f).Within(0.5f), "Not dragged from the old pose or dropped as an outlier.");
            Assert.That(processor.Outliers, Is.EqualTo(0));
        }

        [Test]
        public void AOneFrameJump_IsDropped()
        {
            var processor = new TrackingProcessor();
            var output = new TrackingFrame();
            Run(processor, output, 0, 5);
            Vector3 wrist = output.Body[BodyJoint.RightWrist];

            TrackingFrame jump = Observation(5 * Interval);
            jump.Body[BodyJoint.RightWrist] += new Vector3(0, 1f, 0);
            processor.Process(jump, 5 * Interval, TrackingRegions.All, TrackingRegions.All, SmoothingPreset.Standard, output);

            Assert.That(output.Body[BodyJoint.RightWrist], Is.EqualTo(wrist));
            Assert.That(processor.Outliers, Is.EqualTo(1));
        }

        [Test]
        public void ADisabledPart_IsDisabled_AndNotUpdated()
        {
            var processor = new TrackingProcessor();
            var output = new TrackingFrame();
            Run(processor, output, 0, 5, TrackingRegions.All & ~TrackingRegions.Face);

            Assert.That(output.Face.Status, Is.EqualTo(TrackingStatus.Disabled));
            Assert.That(output.Body.Status, Is.EqualTo(TrackingStatus.Tracked));
        }

        [Test]
        public void ThroughTheProcessor_AnArmIsSteadyAndQuick()
        {
            // End to end on positions with 1 cm noise, measured as the arm angle the avatar would get.
            TrackingFrame[] frames = SyntheticMotion.RaiseAndWaveFrames(10, 30, 0.01f, seed: 4);
            var processor = new TrackingProcessor();
            var output = new TrackingFrame();
            var truth = new List<float>();
            var raw = new List<float>();
            var smoothed = new List<float>();
            foreach (TrackingFrame frame in frames)
            {
                processor.Process(frame, frame.Time, TrackingRegions.All, TrackingRegions.All, SmoothingPreset.Standard, output);
                truth.Add(SyntheticMotion.RaiseAndWave(frame.Time));
                raw.Add(SyntheticMotion.ArmAngle(frame.Body, right: true));
                smoothed.Add(SyntheticMotion.ArmAngle(output.Body, right: true));
            }

            double rawJitter = TrackingMetrics.Jitter(raw, 15, 90);
            double smoothJitter = TrackingMetrics.Jitter(smoothed, 15, 90);
            int lag = TrackingMetrics.Lag(truth, smoothed, 30);
            TestContext.WriteLine($"Still jitter {rawJitter:F3}° -> {smoothJitter:F3}°, lag {lag * 1000 / 30} ms, outliers {processor.Outliers}");

            Assert.That(smoothJitter, Is.LessThan(rawJitter / 5));
            Assert.That(lag, Is.LessThanOrEqualTo(3));
            Assert.That(processor.Outliers, Is.EqualTo(0), "Noise and a fast raise are not outliers.");
        }
    }

    public sealed class FrameSlotTests
    {
        [Test]
        public void NothingIsTaken_UntilSomethingIsPublished_AndOnlyOnce()
        {
            var slot = new FrameSlot<int[]>(() => new int[1]);

            Assert.That(slot.TryTake(out _), Is.False);
            slot.Writing[0] = 7;
            slot.Publish();
            Assert.That(slot.TryTake(out int[] latest), Is.True);
            Assert.That(latest[0], Is.EqualTo(7));
            Assert.That(slot.TryTake(out _), Is.False);
        }

        [Test]
        public void TheNewestPublished_ReplacesOneNotTaken()
        {
            var slot = new FrameSlot<int[]>(() => new int[1]);
            slot.Writing[0] = 1;
            slot.Publish();
            slot.Writing[0] = 2;
            slot.Publish();

            Assert.That(slot.TryTake(out int[] latest), Is.True);
            Assert.That(latest[0], Is.EqualTo(2));
        }

        [Test]
        public void AcrossThreads_AReaderNeverSeesAHalfWrittenOrOlderValue()
        {
            const int Count = 100000;
            var slot = new FrameSlot<int[]>(() => new int[16]);
            var writer = new Thread(() =>
            {
                for (int n = 1; n <= Count; n++)
                {
                    int[] values = slot.Writing;
                    for (int i = 0; i < values.Length; i++)
                    {
                        values[i] = n;
                    }

                    slot.Publish();
                }
            });

            writer.Start();
            int last = 0;
            int taken = 0;
            while (last < Count)
            {
                if (!slot.TryTake(out int[] values))
                {
                    continue;
                }

                taken++;
                int first = values[0];
                for (int i = 1; i < values.Length; i++)
                {
                    Assert.That(values[i], Is.EqualTo(first), "Torn value.");
                }

                Assert.That(first, Is.GreaterThan(last), "Went backwards.");
                last = first;
            }

            writer.Join();
            Assert.That(taken, Is.GreaterThan(0));
        }
    }

    public sealed class TrackingMetricsTests
    {
        [Test]
        public void Jitter_IsTheRmsOfTheSteps()
        {
            Assert.That(TrackingMetrics.Jitter(new float[] { 0, 1, 0, 1, 0 }, 0, 5), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(TrackingMetrics.Jitter(new float[] { 5, 5, 5 }, 0, 3), Is.EqualTo(0.0));
        }

        [Test]
        public void Lag_FindsTheShift()
        {
            var reference = new float[60];
            var output = new float[60];
            for (int i = 0; i < 60; i++)
            {
                reference[i] = Mathf.Sin(i * 0.2f);
                output[i] = Mathf.Sin((i - 4) * 0.2f);
            }

            Assert.That(TrackingMetrics.Lag(reference, output, 10), Is.EqualTo(4));
        }

        [Test]
        public void Variation_And_CountOutside()
        {
            Assert.That(TrackingMetrics.Variation(new float[] { 2, 2, 2 }), Is.EqualTo(0.0));
            Assert.That(TrackingMetrics.Variation(new float[] { 1, 3 }), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(TrackingMetrics.CountOutside(new float[] { -1, 0, 90, 91 }, 0, 90), Is.EqualTo(2));
        }
    }

    public sealed class TrackingReplayProviderTests
    {
        [Test]
        public void FramesBecomeAvailable_AsTheirTimeComes_OnTheCallersClock()
        {
            TrackingFrame[] frames = SyntheticMotion.RaiseAndWaveFrames(1, 10, 0f, seed: 0);
            double now = 100.0;
            var provider = new TrackingReplayProvider(frames, TrackingRegions.All, () => now, loop: false);
            var into = new TrackingFrame();

            provider.Start(TrackingRegions.All);
            Assert.That(provider.State.Value, Is.EqualTo(TrackingState.Running));
            provider.Tick();
            Assert.That(provider.TryTakeLatest(into), Is.True);
            Assert.That(into.Body.Timestamp, Is.EqualTo(100.0), "Moved to the caller's clock.");
            Assert.That(provider.TryTakeLatest(into), Is.False, "Taken once.");

            now = 100.35;
            provider.Tick();
            Assert.That(provider.TryTakeLatest(into), Is.True);
            Assert.That(into.Body.Timestamp, Is.EqualTo(100.3).Within(1e-9), "The latest due frame; older ones are skipped.");
        }

        [Test]
        public void ALoop_StartsOverAfterTheLastFrame()
        {
            TrackingFrame[] frames = SyntheticMotion.RaiseAndWaveFrames(1, 10, 0f, seed: 0);
            double now = 0;
            var provider = new TrackingReplayProvider(frames, TrackingRegions.All, () => now, loop: true);
            var into = new TrackingFrame();
            provider.Start(TrackingRegions.All);

            now = 1.05;
            provider.Tick();
            Assert.That(provider.TryTakeLatest(into), Is.True);
            Assert.That(into.Body.Timestamp, Is.EqualTo(1.0).Within(1e-9), "The first frame of the second round.");
        }
    }
}
