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
        // Up to half a second behind, in observations at about 30 a second.
        private const int MaxShift = 15;

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
                foreach (int level in new[] { 3, 5, 7, 9 })
                {
                    var processor = new TrackingProcessor();
                    var corrected = new TrackingFrame();
                    var output = new TrackingFrame();
                    var raw = new List<float>();
                    var smoothed = new List<float>();
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
                    }

                    if (raw.Count < MaxShift * 2)
                    {
                        continue;
                    }

                    int lag = TrackingMetrics.Lag(raw, smoothed, MaxShift);
                    double interval = (frames[frames.Count - 1].Time - frames[0].Time) / (frames.Count - 1);
                    Debug.Log($"[Smoothing] {info.Id} \"{info.Description}\" level {level}: right upper arm lag {lag} observations ({lag * interval * 1000:F0} ms), "
                        + $"shake raw {TrackingMetrics.Jitter(raw, 0, raw.Count):F2}° smoothed {TrackingMetrics.Jitter(smoothed, 0, smoothed.Count):F2}° per observation");
                }
            }
        }

        /// <summary>How far the person's right upper arm is raised above level, in degrees.</summary>
        private static float Elevation(BodyPose body)
        {
            Vector3 upper = body[BodyJoint.RightElbow] - body[BodyJoint.RightShoulder];
            return upper.sqrMagnitude < 1e-8f ? 0f : Mathf.Asin(Mathf.Clamp(upper.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        }
    }
}
