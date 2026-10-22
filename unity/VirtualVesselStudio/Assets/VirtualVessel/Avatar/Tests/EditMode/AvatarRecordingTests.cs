using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.ProjectData.DataRoot;
using VirtualVessel.Tracking;
using VirtualVessel.Tracking.Recording;

namespace VirtualVessel.Avatar.Tests
{
    /// <summary>
    /// The upper body on the developer's own recordings (tracking detailed design 12), which stay outside
    /// the repository: every rotation is a number, and a summary per recording goes to the log to compare
    /// methods, such as the depth estimation of step 5 (tracking detailed design 8.4).
    /// </summary>
    [Explicit("Reads the developer's tracking recordings in the Data Root; run by name.")]
    [Category("Explicit")]
    public sealed class AvatarRecordingTests
    {
        // An arm turning further than this between two observations (about 33 ms) is a jump, not a movement.
        private const float JumpDegrees = 30f;

        [Test]
        public void TheUpperBody_FromEachRecording_IsSoundAndSummarized()
        {
            string folder = Path.Combine(DataRootResolver.ForCurrentUser().Resolve(null).DataRoot.GetDirectory(DataRootDirectory.Cache), TrackingRecordings.FolderName);
            IReadOnlyList<TrackingRecordingInfo> recordings = TrackingRecordings.List(folder).Where(info => info.Readable).ToArray();
            if (recordings.Count == 0)
            {
                Assert.Ignore($"No recordings in {folder}.");
            }

            foreach (TrackingRecordingInfo info in recordings)
            {
                List<TrackingFrame> frames = TrackingRecordingFile.ReadAll(Path.Combine(folder, info.Id)).Frames;
                var target = new BodyTarget();
                var previous = new BodyTarget();
                var motion = new BodyMotion(hasUpperChest: true);
                int index = 0;
                var upper = new List<float>();
                var fore = new List<float>();
                int usable = 0, torso = 0, left = 0, right = 0, forearm = 0, jumps = 0;
                var visible = new int[3];
                bool first = true;
                foreach (TrackingFrame frame in frames)
                {
                    BodyPose body = frame.Body;
                    if (!body.IsUsable)
                    {
                        continue;
                    }

                    usable++;
                    visible[0] += body.JointConfidence[(int)BodyJoint.LeftShoulder] >= PoseRetargeting.MinVisibility ? 1 : 0;
                    visible[1] += body.JointConfidence[(int)BodyJoint.LeftElbow] >= PoseRetargeting.MinVisibility ? 1 : 0;
                    visible[2] += body.JointConfidence[(int)BodyJoint.LeftWrist] >= PoseRetargeting.MinVisibility ? 1 : 0;
                    PoseRetargeting.Retarget(body, mirror: true, hasUpperChest: true, AvatarRestPose.DefaultArmDownDegrees, target);
                    foreach (Quaternion rotation in target.Rotations)
                    {
                        Assert.That(float.IsNaN(rotation.x) || float.IsNaN(rotation.w), Is.False, $"{info.Id}: a rotation is not a number.");
                    }

                    torso += target.TorsoSeen ? 1 : 0;
                    left += target.LeftArmSeen ? 1 : 0;
                    right += target.RightArmSeen ? 1 : 0;
                    forearm += target.RightForearmSeen ? 1 : 0;
                    if (target.RightArmSeen)
                    {
                        // The avatar's right arm is the person's left one in a mirror.
                        upper.Add(Vector3.Distance(body[BodyJoint.LeftShoulder], body[BodyJoint.LeftElbow]));
                        fore.Add(Vector3.Distance(body[BodyJoint.LeftElbow], body[BodyJoint.LeftWrist]));
                    }

                    // Jumps are counted on what the avatar shows, through the motion that keeps a hidden
                    // wrist's twist. Each observation gets a second of its own and is reached fully, so
                    // only the observations differ between two counts, not the interpolation.
                    body.Timestamp = ++index;
                    motion.Update(body, true, index);
                    motion.Update(body, true, index + 0.5);
                    if (!first && Quaternion.Angle(motion.Current[UpperBodyBone.RightUpperArm], previous[UpperBodyBone.RightUpperArm]) > JumpDegrees)
                    {
                        jumps++;
                    }

                    previous.CopyFrom(motion.Current);
                    first = false;
                }

                Debug.Log($"[Body] {info.Id} \"{info.Description}\": {frames.Count} observations, body {usable}, torso {Share(torso, usable)}, "
                    + $"arms L {Share(left, usable)} R {Share(right, usable)} (R forearm {Share(forearm, usable)}); right arm lengths: upper {Spread(upper)}, forearm {Spread(fore)}; "
                    + $"right upper-arm jumps over {JumpDegrees:F0}° per observation: {jumps}; its joints visible: shoulder {Share(visible[0], usable)}, elbow {Share(visible[1], usable)}, wrist {Share(visible[2], usable)}");
            }
        }

        private static string Share(int count, int total)
        {
            return total == 0 ? "-" : $"{100.0 * count / total:F0}%";
        }

        /// <summary>The median and the spread (relative standard deviation) of lengths: a steady depth keeps bones one length.</summary>
        private static string Spread(List<float> lengths)
        {
            if (lengths.Count == 0)
            {
                return "-";
            }

            float mean = lengths.Average();
            float deviation = Mathf.Sqrt(lengths.Sum(length => (length - mean) * (length - mean)) / lengths.Count);
            float median = lengths.OrderBy(length => length).ElementAt(lengths.Count / 2);
            return $"median {median * 100f:F1} cm, ±{100f * deviation / mean:F0}%";
        }
    }
}
