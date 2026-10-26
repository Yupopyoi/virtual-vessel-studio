using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.ProjectData.DataRoot;
using VirtualVessel.Tracking;
using VirtualVessel.Tracking.Processing;
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
            (string folder, TrackingRecordingInfo[] recordings) = DeveloperRecordings();

            foreach (TrackingRecordingInfo info in recordings)
            {
                (TrackingRecordingHeader header, List<TrackingFrame> frames) = TrackingRecordingFile.ReadAll(Path.Combine(folder, info.Id));

                // The same corrections tracking applies to this kind of input (tracking detailed design 8.4).
                ObservationCorrections corrections = ObservationCorrections.For(header.Traits, ObservationCorrections.Catalog);
                var corrected = new TrackingFrame();
                var bends = new List<float>();
                var target = new BodyTarget();
                var previous = new BodyTarget();
                var motion = new BodyMotion(hasUpperChest: true);
                int index = 0;
                var upper = new List<float>();
                var fore = new List<float>();
                int usable = 0, torso = 0, left = 0, right = 0, forearm = 0, jumps = 0, turns = 0;
                var visible = new int[3];
                bool first = true;
                foreach (TrackingFrame frame in frames)
                {
                    corrections.Apply(frame, corrected);
                    BodyPose body = corrected.Body;
                    if (!body.IsUsable)
                    {
                        continue;
                    }

                    usable++;
                    visible[0] += body.IsSeen(BodyJoint.LeftShoulder) ? 1 : 0;
                    visible[1] += body.IsSeen(BodyJoint.LeftElbow) ? 1 : 0;
                    visible[2] += body.IsSeen(BodyJoint.LeftWrist) ? 1 : 0;
                    PoseRetargeting.Retarget(body, mirror: true, hasUpperChest: true, AvatarRestPose.DefaultArmDownDegrees, target);
                    foreach (Quaternion rotation in target.Rotations)
                    {
                        Assert.That(float.IsNaN(rotation.x) || float.IsNaN(rotation.w), Is.False, $"{info.Id}: a rotation is not a number.");
                    }

                    torso += target.TorsoSeen ? 1 : 0;
                    left += target.LeftArm.Seen ? 1 : 0;
                    right += target.RightArm.Seen ? 1 : 0;
                    forearm += target.RightArm.ForearmSeen ? 1 : 0;
                    if (target.RightArm.Seen)
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

                        // A jump in where the arm points, rather than in its twist about itself.
                        if (Vector3.Angle(motion.Current[UpperBodyBone.RightUpperArm] * Vector3.right, previous[UpperBodyBone.RightUpperArm] * Vector3.right) > JumpDegrees)
                        {
                            turns++;
                        }
                    }

                    if (motion.Current.RightArm.Seen && motion.Current.RightArm.ForearmSeen)
                    {
                        bends.Add(Vector3.Angle(motion.Current[UpperBodyBone.RightUpperArm] * Vector3.right, motion.Current[UpperBodyBone.RightLowerArm] * Vector3.right));
                    }

                    previous.CopyFrom(motion.Current);
                    first = false;
                }

                Debug.Log($"[Body] {info.Id} \"{info.Description}\": {frames.Count} observations, body {usable}, torso {Share(torso, usable)}, "
                    + $"arms L {Share(left, usable)} R {Share(right, usable)} (R forearm {Share(forearm, usable)}); right elbow bend median {Median(bends):F0}°; corrections {corrections.Corrections.Count}; right arm lengths: upper {Spread(upper)}, forearm {Spread(fore)}; "
                    + $"right upper-arm jumps over {JumpDegrees:F0}° per observation: {jumps} ({turns} in direction, the rest in twist); its joints visible: shoulder {Share(visible[0], usable)}, elbow {Share(visible[1], usable)}, wrist {Share(visible[2], usable)}");
            }
        }

        [Test]
        public void TheElbow_OnEachRecording_BendsWhereAnArmCan()
        {
            // How far the bend plane twists the upper arm from where the torso holds it (0: the forearm
            // folds forward), on bent arms whose bend plane sets the twist; with the forearm's depth as
            // tracked and turned the other way about the elbow (the image unchanged); and how often the
            // person's hand, whose palm can set the forearm's twist, is seen (avatar detailed design 7.1).
            (string folder, TrackingRecordingInfo[] recordings) = DeveloperRecordings();

            foreach (TrackingRecordingInfo info in recordings)
            {
                (TrackingRecordingHeader header, List<TrackingFrame> frames) = TrackingRecordingFile.ReadAll(Path.Combine(folder, info.Id));
                ObservationCorrections corrections = ObservationCorrections.For(header.Traits, ObservationCorrections.Catalog);
                var corrected = new TrackingFrame();
                foreach (ArmSide side in new[] { ArmSide.Left, ArmSide.Right })
                {
                    var twists = new List<float>();
                    var flippedTwists = new List<float>();
                    var forearmTwists = new List<float>();
                    int leftOut = 0;
                    int hands = 0, seen = 0;
                    foreach (TrackingFrame frame in frames)
                    {
                        corrections.Apply(frame, corrected);
                        BodyPose body = corrected.Body;
                        if (!body.IsUsable)
                        {
                            continue;
                        }

                        // In a mirror the avatar's left arm is the person's right one.
                        HandPose hand = side.IsLeft ? corrected.RightHand : corrected.LeftHand;
                        seen++;
                        hands += hand.IsUsable ? 1 : 0;
                        (Vector3 shoulder, Vector3 elbow, Vector3 wrist) = PoseRetargeting.ArmJoints(body, side, mirror: true);
                        Quaternion torso = PoseRetargeting.LimitTorso(PoseRetargeting.Torso(
                            PoseRetargeting.Joint(body, BodyJoint.LeftShoulder, true), PoseRetargeting.Joint(body, BodyJoint.RightShoulder, true),
                            PoseRetargeting.Joint(body, BodyJoint.Neck, true), PoseRetargeting.Joint(body, BodyJoint.Hips, true)));
                        if (HandRetargeting.TryPalm(hand, mirror: true, out Quaternion palm))
                        {
                            // How far the palm twists the arm from neutral, supination positive, and how it is shared (avatar detailed design 7.7).
                            (_, Quaternion lower) = PoseRetargeting.Arm(side.IsLeft, shoulder, elbow, wrist, torso);
                            float palmTwist = HandRetargeting.Twist(side.IsLeft, lower, palm);
                            (_, float impossible) = HandRetargeting.Split(palmTwist);
                            forearmTwists.Add(palmTwist);
                            leftOut += Mathf.Abs(impossible) > 10f ? 1 : 0;
                        }

                        float? twist = Twist(side.IsLeft, shoulder, elbow, wrist, torso);
                        if (twist is float value)
                        {
                            twists.Add(value);
                            Vector3 flipped = new Vector3(wrist.x, wrist.y, elbow.z - (wrist.z - elbow.z));
                            flippedTwists.Add(Twist(side.IsLeft, shoulder, elbow, flipped, torso) ?? value);
                        }
                    }

                    int beyond = twists.Count(t => Mathf.Abs(t) > 90f);
                    int fixedByFlip = twists.Where((t, i) => Mathf.Abs(t) > 90f && Mathf.Abs(flippedTwists[i]) <= 90f).Count();
                    Debug.Log($"[Elbow] {info.Id} \"{info.Description}\" avatar {(side.IsLeft ? "left" : "right")}: bent on {twists.Count} of {seen}; twist median {Median(twists.Select(Mathf.Abs).ToList()):F0}°, "
                        + $"beyond ±90° on {beyond} ({Share(beyond, twists.Count)}), beyond ±135° on {twists.Count(t => Mathf.Abs(t) > 135f)}; with the forearm's depth turned over, {fixedByFlip} of those come within ±90°; "
                        + $"twist outward/inward: {Share(twists.Count(t => t < -45f), twists.Count)} below -45°, {Share(twists.Count(t => t > 45f), twists.Count)} above 45°; hand seen {Share(hands, seen)}; "
                        + $"palm twist from neutral (supination +) median {Median(forearmTwists):F0}°, 10%..90% {Percentile(forearmTwists, 0.1f):F0}..{Percentile(forearmTwists, 0.9f):F0}°; "
                        + $"past the forearm's reach {Share(forearmTwists.Count(t => Mathf.Abs(t) > HandRetargeting.ForearmReachDegrees), forearmTwists.Count)}, over 10° left out {Share(leftOut, forearmTwists.Count)}");
                }
            }
        }

        /// <summary>The upper arm's twist the bend sets, from the torso's swing, in degrees about the upper arm; null when nearly straight.</summary>
        private static float? Twist(bool left, Vector3 shoulder, Vector3 elbow, Vector3 wrist, Quaternion torso)
        {
            Vector3 upper = (elbow - shoulder).normalized;
            Vector3 fore = (wrist - elbow).normalized;
            Vector3 bend = Vector3.Cross(upper, fore);
            if (bend.magnitude < 0.5f)
            {
                return null;
            }

            Vector3 swung = Quaternion.FromToRotation(torso * PoseRetargeting.RestDirection(left), upper) * (torso * PoseRetargeting.RestBendNormal(left));
            return Vector3.SignedAngle(Vector3.ProjectOnPlane(swung, upper), bend.normalized, upper);
        }

        private static float Percentile(List<float> values, float share)
        {
            return values.Count == 0 ? float.NaN : values.OrderBy(value => value).ElementAt(Mathf.Min(values.Count - 1, (int)(share * values.Count)));
        }

        /// <summary>The readable recordings in the developer's recordings folder; the test is ignored without any.</summary>
        private static (string Folder, TrackingRecordingInfo[] Recordings) DeveloperRecordings()
        {
            string folder = Path.Combine(DataRootResolver.ForCurrentUser().Resolve(null).DataRoot.GetDirectory(DataRootDirectory.Cache), TrackingRecordings.FolderName);
            TrackingRecordingInfo[] recordings = TrackingRecordings.List(folder).Where(info => info.Readable).ToArray();
            if (recordings.Length == 0)
            {
                Assert.Ignore($"No recordings in {folder}.");
            }

            return (folder, recordings);
        }

        private static float Median(List<float> values)
        {
            return values.Count == 0 ? float.NaN : values.OrderBy(value => value).ElementAt(values.Count / 2);
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
