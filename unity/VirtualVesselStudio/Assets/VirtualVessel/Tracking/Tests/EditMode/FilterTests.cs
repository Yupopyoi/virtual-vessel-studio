using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Tracking.Processing;
using VirtualVessel.Tracking.Recording;

namespace VirtualVessel.Tracking.Tests
{
    public sealed class OneEuroFilterTests
    {
        private static readonly OneEuroParameters s_balanced = SmoothingProfile.Balanced.Angle;

        [Test]
        public void TheFirstSample_PassesThrough()
        {
            OneEuroFilter filter = default(OneEuroFilter).Next(42f, 1.0, s_balanced);

            Assert.That(filter.IsStarted, Is.True);
            Assert.That(filter.Value, Is.EqualTo(42f));
        }

        [Test]
        public void ASampleNotNewer_IsIgnored()
        {
            OneEuroFilter filter = default(OneEuroFilter).Next(0f, 1.0, s_balanced);

            Assert.That(filter.Next(100f, 1.0, s_balanced).Value, Is.EqualTo(0f), "Same time.");
            Assert.That(filter.Next(100f, 0.5, s_balanced).Value, Is.EqualTo(0f), "Older.");
        }

        [Test]
        public void NoiseOnAStillValue_IsReducedSeveralTimes()
        {
            var random = new System.Random(3);
            var raw = new List<float>();
            var smoothed = new List<float>();
            OneEuroFilter filter = default;
            for (int i = 0; i < 300; i++)
            {
                float value = 10f + (float)((random.NextDouble() - 0.5) * 4.0);
                filter = filter.Next(value, i / 30.0, s_balanced);
                raw.Add(value);
                smoothed.Add(filter.Value);
            }

            double before = TrackingMetrics.Jitter(raw, 30, 300);
            double after = TrackingMetrics.Jitter(smoothed, 30, 300);
            Assert.That(after, Is.LessThan(before / 5), $"Jitter {before:F3} -> {after:F3}");
        }

        [Test]
        public void AFastStep_IsReachedWithinAFewFrames()
        {
            OneEuroFilter filter = default(OneEuroFilter).Next(0f, 0, s_balanced);
            int frames = 0;
            for (int i = 1; i <= 60 && Math.Abs(filter.Value - 90f) > 9f; i++)
            {
                filter = filter.Next(90f, i / 30.0, s_balanced);
                frames = i;
            }

            Assert.That(frames, Is.LessThanOrEqualTo(5), "Within 90% of a 90° step.");
        }

        [Test]
        public void UnevenIntervals_DoNotChangeTheResultOfTheSameMotion()
        {
            // The filter works on sample times, not on counts, so the same ramp sampled at 30 and at
            // 60 per second ends at nearly the same place.
            OneEuroFilter at30 = default;
            OneEuroFilter at60 = default;
            for (int i = 0; i <= 30; i++)
            {
                at30 = at30.Next(i * 2f, i / 30.0, s_balanced);
            }

            for (int i = 0; i <= 60; i++)
            {
                at60 = at60.Next(i * 1f, i / 60.0, s_balanced);
            }

            Assert.That(at60.Value, Is.EqualTo(at30.Value).Within(3f));
        }

        [Test]
        public void APosition_IsFilteredPerAxis()
        {
            PositionFilter filter = default(PositionFilter).Next(new Vector3(1, 2, 3), 0, s_balanced);

            Assert.That(filter.Value, Is.EqualTo(new Vector3(1, 2, 3)));
            filter = filter.Next(new Vector3(1, 2, 3), 1.0 / 30, s_balanced);
            Assert.That(filter.Value, Is.EqualTo(new Vector3(1, 2, 3)));
        }

        [Test]
        public void AStraightArm_SwungFast_StaysStraight_WhenItsJointsAreSmoothedTogether()
        {
            // Shoulder, elbow, wrist of a straight arm swinging down from sideways at 180° a second.
            OneEuroParameters parameters = SmoothingProfile.For(SmoothingLevel.Min).Joints;
            var linked = new LinkedPositionFilter(3);
            var apart = new PositionFilter[3];
            var raw = new Vector3[3];
            bool[] all = { true, true, true };
            float mostLinked = 0f, mostApart = 0f;
            for (int i = 0; i <= 30; i++)
            {
                double time = i / 30.0;
                Vector3 along = Quaternion.AngleAxis(-180f * (float)time, Vector3.forward) * Vector3.right;
                raw[0] = Vector3.zero;
                raw[1] = along * 0.25f;
                raw[2] = along * 0.5f;
                linked.Next(raw, all, all, time, parameters);
                for (int j = 0; j < 3; j++)
                {
                    apart[j] = apart[j].Next(raw[j], time, parameters);
                }

                mostLinked = Mathf.Max(mostLinked, Bend(linked.Value(0), linked.Value(1), linked.Value(2)));
                mostApart = Mathf.Max(mostApart, Bend(apart[0].Value, apart[1].Value, apart[2].Value));
            }

            Assert.That(mostLinked, Is.LessThan(0.5f), "Smoothed together, the arm stays straight.");
            Assert.That(mostApart, Is.GreaterThan(10f), "Smoothed apart, the slower elbow lags behind the wrist.");
        }

        [Test]
        public void LinkedJoints_TakeTheCutoffOfTheFastestLead_AndKeepThoseNotTaken()
        {
            OneEuroParameters parameters = SmoothingProfile.Balanced.Position;
            Vector3[] velocities = { new Vector3(0.1f, 0f, 0f), new Vector3(0f, 2f, 0f), new Vector3(0f, 0f, 9f) };
            Assert.That(LinkedPositionFilter.Cutoff(velocities, new[] { true, true, false }, parameters), Is.EqualTo(parameters.MinCutoff + (parameters.Beta * 2f)).Within(1e-5f), "A joint that does not lead, such as a guess out of view, does not quicken the rest.");

            var filter = new LinkedPositionFilter(2);
            filter.Next(new[] { Vector3.one, Vector3.one }, new[] { true, false }, new[] { true, true }, 0.0, parameters);
            Assert.That(filter.Value(0), Is.EqualTo(Vector3.one), "The first sample passes through.");
            Assert.That(filter.IsStarted(1), Is.False, "A sample not taken is not started.");
            filter.Next(new[] { Vector3.zero, Vector3.one }, new[] { false, true }, new[] { true, true }, 1.0 / 30, parameters);
            Assert.That(filter.Value(0), Is.EqualTo(Vector3.one), "A dropped sample keeps the value.");
            filter.Next(new[] { Vector3.zero, Vector3.zero }, new[] { true, true }, new[] { true, true }, 1.0 / 30, parameters);
            Assert.That(filter.Value(0), Is.EqualTo(Vector3.one), "A sample not newer is ignored.");
        }

        private static float Bend(Vector3 shoulder, Vector3 elbow, Vector3 wrist)
        {
            return Vector3.Angle(elbow - shoulder, wrist - elbow);
        }

        [Test]
        public void AHandsShape_IsNeverSmoothedSlowerThanItsFloor_AtAnyLevel()
        {
            // Fingers move too little to quicken the filter, so at the steady levels a hand's shape barely followed.
            for (int level = SmoothingLevel.Min; level <= SmoothingLevel.Max; level++)
            {
                SmoothingProfile profile = SmoothingProfile.For(level);
                Assert.That(profile.Hands.MinCutoff, Is.GreaterThanOrEqualTo(SmoothingProfile.MinHandCutoff), $"Level {level}.");
                Assert.That(profile.Hands.Beta, Is.EqualTo(profile.Joints.Beta));
            }

            Assert.That(SmoothingProfile.For(SmoothingLevel.Min).Joints.MinCutoff, Is.LessThan(SmoothingProfile.MinHandCutoff), "The body stays as steady as before.");
        }

        [Test]
        public void ARotationAcross180Degrees_TakesTheShortWay()
        {
            RotationFilter filter = default(RotationFilter).Next(Quaternion.Euler(0, 175, 0), 0, s_balanced);
            filter = filter.Next(Quaternion.Euler(0, -175, 0), 1.0 / 30, s_balanced);

            float fromStart = Quaternion.Angle(filter.Value, Quaternion.Euler(0, 175, 0));
            float toTarget = Quaternion.Angle(filter.Value, Quaternion.Euler(0, -175, 0));
            Assert.That(fromStart + toTarget, Is.EqualTo(10f).Within(0.1f), "Between the two on the 10° arc, not around the 350° one.");
        }

        [Test]
        public void TheSameRotationWithTheOtherSign_DoesNotMoveIt()
        {
            Quaternion rotation = Quaternion.Euler(10, 20, 30);
            var negated = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
            RotationFilter filter = default(RotationFilter).Next(rotation, 0, s_balanced).Next(negated, 1.0 / 30, s_balanced);

            Assert.That(Quaternion.Angle(filter.Value, rotation), Is.LessThan(0.01f));
        }
    }

    /// <summary>
    /// The product smoothing against the prototype's (Voom: Euler angles averaged over 30 frames) on the
    /// same synthetic motion (tracking detailed design 8, 14). The numbers are printed for the record.
    /// </summary>
    public sealed class OneEuroComparisonTests
    {
        private const double Rate = 30;
        private const int Samples = 300;
        private const int StillEnd = 90;            // 3 s still
        private const int HoldStart = 120;          // held from 4 s ...
        private const int HoldEnd = 180;            // ... to 6 s
        private const int WaveStart = 195;

        [Test]
        public void OneEuro_IsAsSteadyAsTheAverage_WithMuchLessLag()
        {
            // Averaged over seeds: one noise draw can favor either method by a third.
            const int Seeds = 10;
            double averageStill = 0, oneEuroStill = 0, averageHold = 0, oneEuroHold = 0;
            double averageWave = 0, oneEuroWave = 0;
            int averageLag = 0, oneEuroLag = 0;
            for (int seed = 0; seed < Seeds; seed++)
            {
                (List<float> truth, List<float> raw) = NoisyMotion(seed);
                List<float> average = MovingAverage(raw, 30);
                List<float> oneEuro = RotationFiltered(raw, SmoothingProfile.Balanced.Angle);
                averageStill += TrackingMetrics.Jitter(average, 15, StillEnd) / Seeds;
                oneEuroStill += TrackingMetrics.Jitter(oneEuro, 15, StillEnd) / Seeds;
                averageHold += TrackingMetrics.Jitter(average, HoldStart, HoldEnd) / Seeds;
                oneEuroHold += TrackingMetrics.Jitter(oneEuro, HoldStart, HoldEnd) / Seeds;
                averageWave += TrackingMetrics.Error(truth, average, WaveStart, Samples) / Seeds;
                oneEuroWave += TrackingMetrics.Error(truth, oneEuro, WaveStart, Samples) / Seeds;
                averageLag = Math.Max(averageLag, TrackingMetrics.Lag(truth, average, 30));
                oneEuroLag = Math.Max(oneEuroLag, TrackingMetrics.Lag(truth, oneEuro, 30));
            }

            TestContext.WriteLine($"average: still jitter {averageStill:F3}°, hold jitter {averageHold:F3}°, lag {averageLag * 1000 / Rate:F0} ms, wave error {averageWave:F2}°");
            TestContext.WriteLine($"oneEuro: still jitter {oneEuroStill:F3}°, hold jitter {oneEuroHold:F3}°, lag {oneEuroLag * 1000 / Rate:F0} ms, wave error {oneEuroWave:F2}°");

            Assert.That(oneEuroStill, Is.LessThanOrEqualTo(averageStill * 1.05), "Still: as steady.");
            Assert.That(oneEuroLag, Is.LessThanOrEqualTo(3), "At most 100 ms behind.");
            Assert.That(oneEuroLag, Is.LessThan(averageLag / 4));
            Assert.That(oneEuroWave, Is.LessThan(averageWave / 2), "Follows a slow wave.");
            Assert.That(oneEuroHold, Is.LessThan(averageHold), "Settled after a fast move while the average is still catching up.");
        }

        [Test]
        public void TheLevels_KeepTheMeasuredPresets_AndStepEvenlyInBetween()
        {
            Assert.That(SmoothingProfile.Balanced.Angle.MinCutoff, Is.EqualTo(0.1f));
            Assert.That(SmoothingProfile.Steady.Angle.Beta, Is.EqualTo(0.003f));
            Assert.That(SmoothingProfile.Quick.Face.MinCutoff, Is.EqualTo(2f));
            Assert.That(SmoothingProfile.For(10).Angle.MinCutoff, Is.EqualTo(0.1f / Mathf.Sqrt(2f)).Within(1e-5f), "Half the ratio from the balanced set to the steady one.");
            Assert.That(SmoothingProfile.For(SmoothingLevel.Standard).Angle.MinCutoff, Is.EqualTo(0.0125f).Within(1e-5f), "Three times that ratio past the balanced set.");
            Assert.That(SmoothingProfile.For(SmoothingLevel.Standard).Face.MinCutoff, Is.EqualTo(SmoothingProfile.Steady.Face.MinCutoff).Within(1e-5f), "The face no slower than the steady set.");
            Assert.That(SmoothingProfile.For(0).Angle.MinCutoff, Is.EqualTo(SmoothingProfile.For(SmoothingLevel.Min).Angle.MinCutoff), "Kept within the levels.");
            Assert.That(SmoothingProfile.For(99).Angle.MinCutoff, Is.EqualTo(SmoothingProfile.For(SmoothingLevel.Max).Angle.MinCutoff));
            for (int level = SmoothingLevel.Min + 1; level <= SmoothingLevel.Max; level++)
            {
                SmoothingProfile lower = SmoothingProfile.For(level - 1);
                SmoothingProfile higher = SmoothingProfile.For(level);
                Assert.That(higher.Angle.MinCutoff, Is.GreaterThan(lower.Angle.MinCutoff), $"Level {level} follows faster.");
                Assert.That(higher.Position.Beta, Is.GreaterThan(lower.Position.Beta));
                Assert.That(higher.Face.MinCutoff, Is.GreaterThanOrEqualTo(lower.Face.MinCutoff));
            }
        }

        [Test]
        public void EachLevel_IsLessSteady_ButLessBehind_ThanTheOneBelow()
        {
            const int Seeds = 10;
            var jitter = new double[SmoothingLevel.Max + 1];
            var lag = new double[SmoothingLevel.Max + 1];
            for (int level = SmoothingLevel.Min; level <= SmoothingLevel.Max; level++)
            {
                for (int seed = 0; seed < Seeds; seed++)
                {
                    (List<float> truth, List<float> raw) = NoisyMotion(seed);
                    List<float> filtered = RotationFiltered(raw, SmoothingProfile.For(level).Angle);
                    jitter[level] += TrackingMetrics.Jitter(filtered, 15, StillEnd) / Seeds;
                    lag[level] += TrackingMetrics.Lag(truth, filtered, 30) * 1000.0 / Rate / Seeds;
                }

                TestContext.WriteLine($"level {level}: still jitter {jitter[level]:F3}°, lag {lag[level]:F0} ms");
            }

            for (int level = SmoothingLevel.Min + 1; level <= SmoothingLevel.Max; level++)
            {
                Assert.That(jitter[level], Is.GreaterThan(jitter[level - 1]), $"Level {level} moves more while still.");
                Assert.That(lag[level], Is.LessThanOrEqualTo(lag[level - 1]), $"Level {level} is not further behind.");
            }
        }

        [Test]
        public void ThePresets_TradeSteadinessForLag()
        {
            (List<float> truth, List<float> raw) = NoisyMotion(2);

            List<float> smooth = RotationFiltered(raw, SmoothingProfile.Steady.Angle);
            List<float> responsive = RotationFiltered(raw, SmoothingProfile.Quick.Angle);

            Assert.That(TrackingMetrics.Jitter(smooth, 15, StillEnd), Is.LessThan(TrackingMetrics.Jitter(responsive, 15, StillEnd)));
            Assert.That(TrackingMetrics.Error(truth, responsive, WaveStart, Samples), Is.LessThan(TrackingMetrics.Error(truth, smooth, WaveStart, Samples)));
        }

        private static (List<float> Truth, List<float> Raw) NoisyMotion(int seed)
        {
            var random = new System.Random(seed);
            var truth = new List<float>();
            var raw = new List<float>();
            for (int i = 0; i < Samples; i++)
            {
                float value = SyntheticMotion.RaiseAndWave(i / Rate);
                truth.Add(value);
                raw.Add(value + (Gaussian(random) * 2f));
            }

            return (truth, raw);
        }

        private static List<float> RotationFiltered(List<float> degrees, OneEuroParameters parameters)
        {
            var output = new List<float>();
            RotationFilter filter = default;
            for (int i = 0; i < degrees.Count; i++)
            {
                filter = filter.Next(Quaternion.AngleAxis(degrees[i], Vector3.forward), i / Rate, parameters);
                filter.Value.ToAngleAxis(out float angle, out Vector3 axis);
                output.Add(axis.z < 0 ? -angle : angle);
            }

            return output;
        }

        /// <summary>The prototype's smoothing: the mean of the last <paramref name="window"/> values.</summary>
        private static List<float> MovingAverage(List<float> values, int window)
        {
            var output = new List<float>();
            for (int i = 0; i < values.Count; i++)
            {
                int start = Math.Max(0, i - window + 1);
                float sum = 0;
                for (int j = start; j <= i; j++)
                {
                    sum += values[j];
                }

                output.Add(sum / (i - start + 1));
            }

            return output;
        }

        private static float Gaussian(System.Random random)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = random.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }
    }
}
