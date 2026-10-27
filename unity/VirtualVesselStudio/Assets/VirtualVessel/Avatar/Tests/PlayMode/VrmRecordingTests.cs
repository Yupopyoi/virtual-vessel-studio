using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VirtualVessel.ProjectData.DataRoot;
using VirtualVessel.Tracking;
using VirtualVessel.Tracking.Processing;
using VirtualVessel.Tracking.Recording;

namespace VirtualVessel.Avatar.Tests
{
    /// <summary>
    /// The developer's tracking recordings (tracking detailed design 12) played on the real VRM samples
    /// (<see cref="VrmSamples"/>), as the app runs them: how the arms keep out of the body and how steadily
    /// the bones move. Measurements for tuning, so the tests run only by name.
    /// </summary>
    [Explicit("Needs the sample models and the developer's recordings outside the repository; run by name.")]
    [Category("Explicit")]
    public sealed class VrmRecordingTests
    {
        [UnityTest]
        public IEnumerator Body_OnTheRecordings_ClearanceMovesTheArmsSmoothly([ValueSource(typeof(VrmSamples), nameof(VrmSamples.Names))] string sample)
        {
            (string folder, TrackingRecordingInfo[] recordings) = DeveloperRecordings();

            var load = new SampleLoad();
            yield return VrmSamples.Load(sample, load);
            using (IAvatarModel model = load.Model)
            {
                // Each recording in 3D, and in 2D with the arms kept in front (tracking detailed design 8.6).
                foreach ((TrackingRecordingInfo info, bool flat) in recordings.SelectMany(recording => new[] { (recording, false), (recording, true) }))
                {
                    // As the app runs: the standard smoothing, mirrored.
                    var replay = new RecordingReplay(Path.Combine(folder, info.Id), SmoothingLevel.Standard, flat);
                    TrackingFrame output = replay.Output;
                    var controller = new AvatarController(model, ExpressionMapping.Default);
                    var tracked = new BodyMotion(model.Rig.Has(AvatarBone.UpperChest));
                    var before = new BodyTarget();
                    var beforeTracked = new BodyTarget();
                    var again = new BodyTarget();
                    float remainingSum = 0f, mostRemaining = 0f, mostHandStep = 0f, mostUpperStep = 0f, mostLowerStep = 0f;
                    int handJumps = 0;
                    int shown = 0, active = 0, added = 0, touching = 0;
                    float mostTurn = 0f, mostAdded = 0f, turnSum = 0f, imageSum = 0f;
                    double mostAddedAt = 0.0;
                    foreach (double now in replay.Frames())
                    {
                        controller.Update(output, mirror: true, now, armsInFront: flat);
                        tracked.Update(output.Body, true, now);
                        float turn = 0f, image = 0f, step = 0f, trackedStep = 0f;
                        foreach (UpperBodyBone bone in new[] { UpperBodyBone.LeftUpperArm, UpperBodyBone.LeftLowerArm, UpperBodyBone.RightUpperArm, UpperBodyBone.RightLowerArm })
                        {
                            Vector3 rest = bone == UpperBodyBone.LeftUpperArm || bone == UpperBodyBone.LeftLowerArm ? Vector3.left : Vector3.right;
                            turn = Mathf.Max(turn, Vector3.Angle(controller.Body[bone] * rest, tracked.Current[bone] * rest));

                            // How far it moved on the camera's image: the angle between the two seen from the front.
                            Vector2 seen = controller.Body[bone] * rest;
                            Vector2 seenTracked = tracked.Current[bone] * rest;
                            if (seen.sqrMagnitude > 0.01f && seenTracked.sqrMagnitude > 0.01f)
                            {
                                image = Mathf.Max(image, Vector2.Angle(seen, seenTracked));
                            }
                            if (shown > 0)
                            {
                                float moved = Vector3.Angle(controller.Body[bone] * rest, before[bone] * rest);
                                float movedTracked = Vector3.Angle(tracked.Current[bone] * rest, beforeTracked[bone] * rest);
                                if (moved - movedTracked > step - trackedStep)
                                {
                                    step = moved;
                                    trackedStep = movedTracked;
                                }
                            }
                        }

                        if (shown > 0)
                        {
                            // How far a hand turns in a frame: a palm read the other way round flipped it (avatar detailed design 7.7).
                            mostUpperStep = Mathf.Max(mostUpperStep, Mathf.Max(Quaternion.Angle(controller.Body[UpperBodyBone.LeftUpperArm], before[UpperBodyBone.LeftUpperArm]), Quaternion.Angle(controller.Body[UpperBodyBone.RightUpperArm], before[UpperBodyBone.RightUpperArm])));
                            mostLowerStep = Mathf.Max(mostLowerStep, Mathf.Max(Quaternion.Angle(controller.Body[UpperBodyBone.LeftLowerArm], before[UpperBodyBone.LeftLowerArm]), Quaternion.Angle(controller.Body[UpperBodyBone.RightLowerArm], before[UpperBodyBone.RightLowerArm])));
                            foreach (UpperBodyBone hand in new[] { UpperBodyBone.LeftHand, UpperBodyBone.RightHand })
                            {
                                float handStep = Quaternion.Angle(controller.Body[hand], before[hand]);
                                mostHandStep = Mathf.Max(mostHandStep, handStep);
                                handJumps += handStep > 20f ? 1 : 0;
                            }
                        }

                        shown++;
                        Quaternion neck = HeadPose.Neck(controller.Body.TorsoTop, controller.Face.Head);
                        if (ArmClearance.Touches(model.BodyShape, controller.Body, neck, controller.Face.Head))
                        {
                            // How deep: how much more an arm would have to turn to come out.
                            touching++;
                            ArmClearance.Apply(model.BodyShape, controller.Body, neck, controller.Face.Head, again);
                            float remaining = 0f;
                            for (int i = 0; i < BodyTarget.BoneCount; i++)
                            {
                                remaining = Mathf.Max(remaining, Quaternion.Angle(again.Rotations[i], controller.Body.Rotations[i]));
                            }

                            remainingSum += remaining;
                            mostRemaining = Mathf.Max(mostRemaining, remaining);
                        }

                        if (turn > 1f)
                        {
                            active++;
                            turnSum += turn;
                            imageSum += image;
                        }

                        mostTurn = Mathf.Max(mostTurn, turn);

                        // A frame where clearance moves an arm more than 5° beyond what tracking moved it: a jump it adds.
                        if (step - trackedStep > 5f)
                        {
                            added++;
                            if (step - trackedStep > mostAdded)
                            {
                                mostAdded = step - trackedStep;
                                mostAddedAt = now - replay.Start;
                            }
                        }

                        before.CopyFrom(controller.Body);
                        beforeTracked.CopyFrom(tracked.Current);
                    }

                    UnityEngine.Debug.Log($"[Avatar/Clearance] {sample} \"{info.Description}\" {(flat ? "2D" : "3D")}: {shown} frames; arms turned on {active} ({100.0 * active / shown:F0}%), at most {mostTurn:F0}°, on average {(active > 0 ? turnSum / active : 0f):F1}°, of which on the image {(active > 0 ? imageSum / active : 0f):F1}°; still touching on {touching}, by {(touching > 0 ? remainingSum / touching : 0f):F1}° on average and {mostRemaining:F0}° at most; "
                        + $"frames with a jump clearance added over 5°: {added}, the largest {mostAdded:F0}° at {mostAddedAt:F1} s; hands turning over 20° in a frame: {handJumps}, at most {mostHandStep:F0}°; at most in a frame, upper arms {mostUpperStep:F0}°, forearms {mostLowerStep:F0}°");
                }
            }
        }

        /// <summary>
        /// How steadily and naturally each bone moves on the developer's recordings, as the app runs them for
        /// the user (3D, the steadiest level the user chose, mirrored, 60 frames a second): per bone the most
        /// it turns in a frame, how often past 15° (900° a second), and how much its turn changes from frame
        /// to frame (shake); and the worst moments, with how much tracking alone moved there, to tell what
        /// the processing after it added.
        /// </summary>
        [UnityTest]
        public IEnumerator Body_OnTheRecordings_MovesSteadily([ValueSource(typeof(VrmSamples), nameof(VrmSamples.Names))] string sample)
        {
            (string folder, TrackingRecordingInfo[] recordings) = DeveloperRecordings();

            UpperBodyBone[] bones = { UpperBodyBone.UpperChest, UpperBodyBone.LeftUpperArm, UpperBodyBone.RightUpperArm, UpperBodyBone.LeftLowerArm, UpperBodyBone.RightLowerArm, UpperBodyBone.LeftHand, UpperBodyBone.RightHand };
            var load = new SampleLoad();
            yield return VrmSamples.Load(sample, load);
            using (IAvatarModel model = load.Model)
            {
                foreach (TrackingRecordingInfo info in recordings)
                {
                    var replay = new RecordingReplay(Path.Combine(folder, info.Id), SmoothingLevel.Min, flat: false);
                    TrackingFrame output = replay.Output;
                    var controller = new AvatarController(model, ExpressionMapping.Default);
                    var tracked = new BodyMotion(model.Rig.Has(AvatarBone.UpperChest));
                    var shownHistory = new[] { new BodyTarget(), new BodyTarget() };
                    var trackedBefore = new BodyTarget();
                    var steps = bones.Select(_ => new List<float>()).ToArray();
                    var shakes = bones.Select(_ => new List<float>()).ToArray();
                    var events = new List<(double Time, UpperBodyBone Bone, float Step, float Tracked)>();
                    var notes = new Dictionary<double, string>();
                    bool[] seenBefore = new bool[4];
                    Quaternion headBefore = Quaternion.identity;
                    var headSteps = new List<float>();

                    // The most any finger angle changes in a frame, and how much its change changes (shake).
                    var fingerBefore = new[] { new FingerPose(), new FingerPose() };
                    var fingerSteps = new List<float>();
                    var fingerShakes = new List<float>();
                    var fingerStepBefore = new float[2 * FingerPose.FingerCount * (FingerPose.JointCount + 1)];
                    int shown = 0;
                    foreach (double now in replay.Frames())
                    {
                        controller.Update(output, mirror: true, now);
                        tracked.Update(output.Body, true, now);
                        if (shown > 0)
                        {
                            headSteps.Add(Quaternion.Angle(controller.Face.Head, headBefore));
                            FingerPose[] fingers = { controller.LeftFingers, controller.RightFingers };
                            float most = 0f, shake = 0f;
                            for (int h = 0; h < 2; h++)
                            {
                                for (int v = 0; v < fingers[h].Values.Length; v++)
                                {
                                    float change = fingers[h].Values[v] - fingerBefore[h].Values[v];
                                    most = Mathf.Max(most, Mathf.Abs(change));
                                    shake = Mathf.Max(shake, Mathf.Abs(change - fingerStepBefore[(h * fingers[h].Values.Length) + v]));
                                    fingerStepBefore[(h * fingers[h].Values.Length) + v] = change;
                                }
                            }

                            fingerSteps.Add(most);
                            fingerShakes.Add(shake);
                        }

                        for (int b = 0; b < bones.Length && shown > 0; b++)
                        {
                            UpperBodyBone bone = bones[b];

                            // A hand follows its forearm as tracked; it is compared with the forearm.
                            UpperBodyBone trackedBone = bone == UpperBodyBone.LeftHand ? UpperBodyBone.LeftLowerArm : bone == UpperBodyBone.RightHand ? UpperBodyBone.RightLowerArm : bone;
                            float step = Quaternion.Angle(controller.Body[bone], shownHistory[0][bone]);
                            float trackedStep = Quaternion.Angle(tracked.Current[trackedBone], trackedBefore[trackedBone]);
                            steps[b].Add(step);
                            if (shown > 1)
                            {
                                // How much the turn changed: the turn this frame against the one before.
                                Quaternion turn = controller.Body[bone] * Quaternion.Inverse(shownHistory[0][bone]);
                                Quaternion turnBefore = shownHistory[0][bone] * Quaternion.Inverse(shownHistory[1][bone]);
                                shakes[b].Add(Quaternion.Angle(turn, turnBefore));
                            }

                            if (step > 15f)
                            {
                                events.Add((now - replay.Start, bone, step, trackedStep));

                                // What changed there: where the bone points against its twist, and what tracking saw.
                                Vector3 rest = bone == UpperBodyBone.LeftUpperArm || bone == UpperBodyBone.LeftLowerArm || bone == UpperBodyBone.LeftHand ? Vector3.left : Vector3.right;
                                float pointed = Vector3.Angle(controller.Body[bone] * rest, shownHistory[0][bone] * rest);
                                BodyTarget t = tracked.Current;
                                notes[now - replay.Start] = $"points {pointed:F0}° of it; seen L arm {t.LeftArm.Seen}/{seenBefore[0]} forearm {t.LeftArm.ForearmSeen}/{seenBefore[1]}, R arm {t.RightArm.Seen}/{seenBefore[2]} forearm {t.RightArm.ForearmSeen}/{seenBefore[3]}; presence L {tracked.LeftArmPresence.Arm:F2} R {tracked.RightArmPresence.Arm:F2}";
                            }
                        }

                        seenBefore[0] = tracked.Current.LeftArm.Seen;
                        seenBefore[1] = tracked.Current.LeftArm.ForearmSeen;
                        seenBefore[2] = tracked.Current.RightArm.Seen;
                        seenBefore[3] = tracked.Current.RightArm.ForearmSeen;
                        shownHistory[1].CopyFrom(shownHistory[0]);
                        shownHistory[0].CopyFrom(controller.Body);
                        trackedBefore.CopyFrom(tracked.Current);
                        headBefore = controller.Face.Head;
                        fingerBefore[0].CopyFrom(controller.LeftFingers);
                        fingerBefore[1].CopyFrom(controller.RightFingers);
                        shown++;
                    }

                    var summary = new System.Text.StringBuilder();
                    for (int b = 0; b < bones.Length; b++)
                    {
                        List<float> sorted = steps[b].OrderBy(x => x).ToList();
                        summary.Append($"{bones[b]} most {sorted[sorted.Count - 1]:F0}° 99% {sorted[(int)(sorted.Count * 0.99)]:F1}° over 15° {sorted.Count(x => x > 15f)}, shake {shakes[b].Average():F2}°; ");
                    }

                    summary.Append($"head most {headSteps.Max():F0}° over 15° {headSteps.Count(x => x > 15f)}; ");
                    List<float> sortedFingers = fingerSteps.OrderBy(x => x).ToList();
                    summary.Append($"fingers: the largest change in a frame median {sortedFingers[sortedFingers.Count / 2]:F1}° 99% {sortedFingers[(int)(sortedFingers.Count * 0.99)]:F1}° most {sortedFingers[sortedFingers.Count - 1]:F0}° over 15° {sortedFingers.Count(x => x > 15f)}, shake median {fingerShakes.OrderBy(x => x).ElementAt(fingerShakes.Count / 2):F2}°");
                    UnityEngine.Debug.Log($"[Avatar/Steady] {sample} \"{info.Description}\": {summary}");

                    // The worst moments, one per quarter second.
                    double lastAt = double.NegativeInfinity;
                    foreach ((double time, UpperBodyBone bone, float step, float trackedStep) in events.OrderByDescending(e => e.Step).Where(_ => true).ToList())
                    {
                        if (events.Any(e => e.Time < time && e.Time > time - 0.25 && e.Step >= step) || Math.Abs(time - lastAt) < 0.25)
                        {
                            continue;
                        }

                        lastAt = time;
                        UnityEngine.Debug.Log($"[Avatar/Steady] {sample} \"{info.Description}\" at {time:F2} s: {bone} turned {step:F0}° in a frame; tracking alone {trackedStep:F0}°; {(notes.TryGetValue(time, out string note) ? note : string.Empty)}");
                    }
                }
            }
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
    }
}
