using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.ProjectData.DataRoot;
using VirtualVessel.Tracking.Processing;
using VirtualVessel.Tracking.Recording;

namespace VirtualVessel.Tracking.Tests
{
    /// <summary>
    /// How much the body's smoothing delays and steadies the arms on the developer's own recordings
    /// (tracking detailed design 8.2, 12), which stay outside the repository. The summary goes to the log.
    /// </summary>
    [Explicit("Reads the developer's tracking recordings in the Data Root; run by name.")]
    [Category("Explicit")]
    public sealed class RecordedSmoothingTests
    {
        // Up to a second behind, in observations at about 30 a second.
        private const int MaxShift = 30;

        [Test]
        public void TheArms_AtEachSpeed_LagAndShake()
        {
            string folder = Path.Combine(DataRootResolver.ForCurrentUser().Resolve(null).DataRoot.GetDirectory(DataRootDirectory.Cache), TrackingRecordings.FolderName);
            IReadOnlyList<TrackingRecordingInfo> recordings = TrackingRecordings.List(folder).Where(info => info.Readable).ToArray();
            if (recordings.Count == 0)
            {
                Assert.Ignore($"No recordings in {folder}.");
            }

            foreach (TrackingRecordingInfo info in recordings)
            {
                (TrackingRecordingHeader header, List<TrackingFrame> frames) = TrackingRecordingFile.ReadAll(Path.Combine(folder, info.Id));
                ObservationCorrections corrections = ObservationCorrections.For(header.Traits, ObservationCorrections.Catalog);
                foreach (int level in new[] { 1, 3, 5, 7, 9 })
                {
                    var processor = new TrackingProcessor();
                    var corrected = new TrackingFrame();
                    var output = new TrackingFrame();
                    var raw = new List<float>();
                    var smoothed = new List<float>();
                    var addedBend = new List<float>();
                    var recentBends = new[] { new Queue<float>(), new Queue<float>() };
                    foreach (TrackingFrame frame in frames)
                    {
                        corrections.Apply(frame, corrected);
                        processor.Process(corrected, frame.Time, header.Supported, TrackingRegions.All, level, output);
                        if (!corrected.Body.IsUsable || !output.Body.IsUsable)
                        {
                            continue;
                        }

                        raw.Add(Elevation(corrected.Body));
                        smoothed.Add(Elevation(output.Body));
                        for (int a = 0; a < s_arms.Length; a++)
                        {
                            (BodyJoint shoulder, BodyJoint elbow, BodyJoint wrist) = s_arms[a];
                            Queue<float> recent = recentBends[a];
                            if (!corrected.Body.IsSeen(shoulder) || !corrected.Body.IsSeen(elbow) || !corrected.Body.IsSeen(wrist))
                            {
                                recent.Clear();
                                continue;
                            }

                            // The bend the smoothing invents: more than the arm showed at any time it may still
                            // be catching up on, so lag alone does not count.
                            recent.Enqueue(Bend(corrected.Body, shoulder, elbow, wrist));
                            if (recent.Count > MaxShift)
                            {
                                recent.Dequeue();
                            }

                            if (recent.Count == MaxShift)
                            {
                                addedBend.Add(Bend(output.Body, shoulder, elbow, wrist) - recent.Max());
                            }
                        }
                    }

                    if (raw.Count < MaxShift * 2)
                    {
                        continue;
                    }

                    int lag = TrackingMetrics.Lag(raw, smoothed, MaxShift);
                    double interval = (frames[frames.Count - 1].Time - frames[0].Time) / (frames.Count - 1);
                    Debug.Log($"[Smoothing] {info.Id} \"{info.Description}\" level {level}: right upper arm lag {lag} observations ({lag * interval * 1000:F0} ms), "
                        + $"shake raw {TrackingMetrics.Jitter(raw, 0, raw.Count):F2}° smoothed {TrackingMetrics.Jitter(smoothed, 0, smoothed.Count):F2}° per observation; "
                        + $"bend beyond the last second of the arm: {BendSummary(addedBend)}");
                }
            }
        }

        private static readonly (BodyJoint Shoulder, BodyJoint Elbow, BodyJoint Wrist)[] s_arms =
        {
            (BodyJoint.LeftShoulder, BodyJoint.LeftElbow, BodyJoint.LeftWrist),
            (BodyJoint.RightShoulder, BodyJoint.RightElbow, BodyJoint.RightWrist),
        };

        /// <summary>How far the elbow is bent, in degrees: 0 straight.</summary>
        private static float Bend(BodyPose body, BodyJoint shoulder, BodyJoint elbow, BodyJoint wrist)
        {
            return Vector3.Angle(body[elbow] - body[shoulder], body[wrist] - body[elbow]);
        }

        private static string BendSummary(List<float> added)
        {
            if (added.Count == 0)
            {
                return "no arm seen";
            }

            List<float> sorted = added.OrderBy(x => x).ToList();
            return $"{sorted.Count} arm observations, median {sorted[sorted.Count / 2]:F1}°, 95% {sorted[(int)(sorted.Count * 0.95)]:F1}°, most {sorted[sorted.Count - 1]:F0}°, over 15° {sorted.Count(x => x > 15f)}";
        }

        /// <summary>How far the person's right upper arm is raised above level, in degrees.</summary>
        private static float Elevation(BodyPose body)
        {
            Vector3 upper = body[BodyJoint.RightElbow] - body[BodyJoint.RightShoulder];
            return upper.sqrMagnitude < 1e-8f ? 0f : Mathf.Asin(Mathf.Clamp(upper.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        }
    }
}
