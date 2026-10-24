using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VirtualVessel.Avatar.Vrm;
using VirtualVessel.ProjectData.DataRoot;
using VirtualVessel.Tracking;
using VirtualVessel.Tracking.Processing;
using VirtualVessel.Tracking.Recording;

namespace VirtualVessel.Avatar.Tests
{
    /// <summary>
    /// Loading real VRM models with UniVRM (avatar detailed design 14). The models are the user's
    /// samples outside the repository (avatar detailed design 17), so the tests run only by name. They
    /// are looked for in the folder named by VV_SAMPLES_DIR, else in "samples" next to the repository.
    /// </summary>
    [Explicit("Needs the sample models outside the repository; run by name.")]
    [Category("Explicit")]
    public sealed class VrmLoaderTests
    {
        private const double TimeoutSeconds = 60;

        private static IEnumerable<string> Samples()
        {
            string folder = SampleFolder();
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.vrm").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal) : new[] { "none" };
        }

        [UnityTest]
        public IEnumerator Load_HasTheRequiredBonesAndExpressions_AndReadsTheMeta([ValueSource(nameof(Samples))] string sample)
        {
            var load = new LoadResult();
            yield return Load(sample, load);
            using (IAvatarModel model = load.Model)
            {
                TestContext.WriteLine($"{sample}: '{model.Info.Name}' VRM {model.Info.FormatVersion}, {load.Seconds:F2} s, longest frame {load.LongestFrameMilliseconds:F0} ms, authors {string.Join(", ", model.Info.License.Authors)}, permission {model.Info.License.AvatarPermission}, thumbnail {model.Info.Thumbnail?.Extension ?? "none"}.");
                Assert.That(AvatarBones.MissingRequired(model.Rig.Has), Is.Empty);
                Assert.That(model.Rig.BoneLength(AvatarBone.LeftUpperArm), Is.InRange(0.1f, 0.6f), "Meters, from the upper arm to the forearm.");
                Assert.That(model.Expressions.Has(AvatarExpression.Blink), Is.True);
                Assert.That(model.Expressions.Has(AvatarExpression.Aa), Is.True);
                Assert.That(model.Info.Format, Is.EqualTo("vrm"));
                Assert.That(model.Info.FormatVersion, Is.EqualTo("1.0").Or.EqualTo("0.x"));
                Assert.That(model.Info.Converted, Is.EqualTo(model.Info.FormatVersion == "0.x"));
                Assert.That(model.Info.License.AvatarPermission, Is.Not.Null);
                Assert.That(model.Root.GetComponentsInChildren<Renderer>().All(r => !r.enabled), Is.True, "Loaded hidden until placed.");
            }
        }

        [UnityTest]
        public IEnumerator Rig_RotatesBonesInTheRootSpace_WhateverTheModelsBoneAxes([ValueSource(nameof(Samples))] string sample)
        {
            var load = new LoadResult();
            yield return Load(sample, load);
            using (IAvatarModel model = load.Model)
            {
                Animator animator = model.Root.GetComponent<Animator>();
                Transform shoulder = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                Transform hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                Vector3 before = (hand.position - shoulder.position).normalized;

                // The avatar faces +Z, so its left arm points to -X in the reference pose. Turning it
                // 90 degrees around Z lowers it.
                model.Rig.SetRotation(AvatarBone.LeftUpperArm, Quaternion.AngleAxis(90f, Vector3.forward));
                model.Apply();
                Vector3 after = (hand.position - shoulder.position).normalized;

                Assert.That(Vector3.Angle(before, Vector3.left), Is.LessThan(15f), "T pose.");
                Assert.That(Vector3.Angle(after, Vector3.down), Is.LessThan(15f), "Arm down.");

                model.Rig.ResetPose();
                model.Apply();
                Assert.That(Vector3.Angle((hand.position - shoulder.position).normalized, Vector3.left), Is.LessThan(15f), "Back to the T pose.");
            }
        }

        [UnityTest]
        public IEnumerator Expressions_MoveTheFace([ValueSource(nameof(Samples))] string sample)
        {
            var load = new LoadResult();
            yield return Load(sample, load);
            using (IAvatarModel model = load.Model)
            {
                SkinnedMeshRenderer[] renderers = model.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                float before = TotalBlendShapeWeight(renderers);
                model.Expressions.SetWeight(AvatarExpression.Aa, 1f);
                model.Apply();

                Assert.That(TotalBlendShapeWeight(renderers), Is.GreaterThan(before));
            }
        }

        [UnityTest]
        public IEnumerator Face_TurnsTheHead_MovesTheFace_AndAppliesWithinItsBudget([ValueSource(nameof(Samples))] string sample)
        {
            var load = new LoadResult();
            yield return Load(sample, load);
            using (IAvatarModel model = load.Model)
            {
                Transform head = model.Root.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
                Quaternion rest = head.rotation;
                SkinnedMeshRenderer[] renderers = model.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                float before = TotalBlendShapeWeight(renderers);

                // The person turns 30 degrees to their right, closes both eyes, and opens the mouth.
                var frame = new TrackingFrame();
                frame.Face.Status = TrackingStatus.Tracked;
                frame.Face.Timestamp = 1.0;
                frame.Face.HeadRotation = Quaternion.AngleAxis(30f, Vector3.up);
                frame.Face[FaceShape.EyeBlinkLeft] = 0.9f;
                frame.Face[FaceShape.EyeBlinkRight] = 0.9f;
                frame.Face[FaceShape.JawOpen] = 0.8f;
                var controller = new AvatarController(model, ExpressionMapping.Default);
                controller.Update(frame, mirror: false, now: 1.0);
                controller.Update(frame, mirror: false, now: 1.5);
                model.Apply();

                // The avatar faces +Z with its right at +X, so turning to its own right is +30 degrees around Y.
                float error = Quaternion.Angle(head.rotation * Quaternion.Inverse(rest), Quaternion.AngleAxis(30f, Vector3.up));
                Assert.That(error, Is.LessThan(2f), "The head turned to the avatar's own right.");
                Assert.That(TotalBlendShapeWeight(renderers), Is.GreaterThan(before), "The eyes and mouth moved.");
                Assert.That(controller.Expressions.Profile, Is.SameAs(FacePartProfile.VRoid), "The VRoid samples are moved part by part.");

                // Looking to the side moves the eyes by bones or by expressions, whichever the model uses.
                // Expressions go first: a model's blink usually blocks the gaze (overrideLookAt).
                foreach (AvatarExpression expression in (AvatarExpression[])Enum.GetValues(typeof(AvatarExpression)))
                {
                    model.Expressions.SetWeight(expression, 0f);
                }

                model.Expressions.SetLook(0f, 0f);
                model.Apply();
                Transform eye = ((VrmAvatarModel)model).ModelBone(HumanBodyBones.LeftEye);
                Quaternion eyeAhead = eye != null ? eye.rotation : Quaternion.identity;
                float faceAhead = TotalBlendShapeWeight(renderers);
                model.Expressions.SetLook(1f, 0f);
                model.Apply();
                bool eyeTurned = eye != null && Quaternion.Angle(eyeAhead, eye.rotation) > 3f;
                Assert.That(eyeTurned || !Mathf.Approximately(TotalBlendShapeWeight(renderers), faceAhead), Is.True, "The eyes look to the side.");

                // Per-frame cost of the face and of UniVRM's update, secondary motion included (avatar detailed design 15).
                const int Frames = 300;
                var times = new double[Frames];
                var watch = new Stopwatch();
                for (int i = 0; i < Frames; i++)
                {
                    // 30 observations a second at 60 frames: a new one every other frame.
                    frame.Face.Timestamp = 2.0 + ((i / 2) / 30.0);
                    frame.Face.HeadRotation = Quaternion.AngleAxis(20f * Mathf.Sin(i * 0.1f), Vector3.up);
                    frame.Face[FaceShape.JawOpen] = 0.5f + (0.5f * Mathf.Sin(i * 0.3f));
                    watch.Restart();
                    controller.Update(frame, mirror: true, now: 2.0 + (i / 60.0));
                    model.Apply();
                    times[i] = watch.Elapsed.TotalMilliseconds;
                }

                Array.Sort(times);
                double median = times[Frames / 2];
                UnityEngine.Debug.Log($"[Avatar/Apply] {sample}: median {median:F3} ms, 95th {times[Frames * 95 / 100]:F3} ms, max {times[Frames - 1]:F3} ms");
                Assert.That(median, Is.LessThan(1.0), "Far over the 0.3 ms target; see avatar detailed design 15.");
            }
        }

        [UnityTest]
        public IEnumerator Body_PointsTheModelsOwnArms_AndAppliesWithinItsBudget([ValueSource(nameof(Samples))] string sample)
        {
            var load = new LoadResult();
            yield return Load(sample, load);
            using (IAvatarModel model = load.Model)
            {
                var vrm = (VrmAvatarModel)model;

                // The person reaches forward with the right arm and holds the left one out with the forearm up.
                var frame = new TrackingFrame();
                SyntheticMotion.WriteArms(frame.Body, Vector3.left, Vector3.up, Vector3.forward, Vector3.forward, 1.0);
                var controller = new AvatarController(model, ExpressionMapping.Default);
                controller.Update(frame, mirror: false, now: 1.0);
                controller.Update(frame, mirror: false, now: 1.5);
                model.Apply();

                // UniVRM carries the rig's rotations onto the model's own bones; their directions are what shows.
                Assert.That(Vector3.Angle(Direction(vrm, model.Root, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm), Vector3.forward), Is.LessThan(5f), "The right arm reaches forward.");
                Assert.That(Vector3.Angle(Direction(vrm, model.Root, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm), Vector3.left), Is.LessThan(5f), "The left upper arm is out sideways.");
                Assert.That(Vector3.Angle(Direction(vrm, model.Root, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand), Vector3.up), Is.LessThan(5f), "The left forearm points up.");

                // The right palm turned a quarter, thumb up (reaching forward, the back of the hand outward): the
                // model's own hand turns too, its back from its index and little fingers' bases.
                frame.RightHand.Status = TrackingStatus.Tracked;
                frame.RightHand.Confidence = 0.9f;
                frame.RightHand.PalmRotation = Quaternion.LookRotation(Vector3.forward, Vector3.right);
                frame.RightHand.Timestamp = 2.0;
                controller.Update(frame, mirror: false, now: 2.0);
                controller.Update(frame, mirror: false, now: 2.5);
                model.Apply();
                Vector3 hand = vrm.ModelBone(HumanBodyBones.RightHand).position;
                Vector3 back = model.Root.InverseTransformDirection(Vector3.Cross(
                    vrm.ModelBone(HumanBodyBones.RightIndexProximal).position - hand, vrm.ModelBone(HumanBodyBones.RightLittleProximal).position - hand));
                Assert.That(Vector3.Angle(back, Vector3.right), Is.LessThan(20f), "The back of the model's right hand faces outward.");
                frame.RightHand.Status = TrackingStatus.Lost;

                // Per-frame cost with the body moving as well as the face (avatar detailed design 15).
                const int Frames = 300;
                var times = new double[Frames];
                var watch = new Stopwatch();
                frame.Face.Status = TrackingStatus.Tracked;
                for (int i = 0; i < Frames; i++)
                {
                    // 30 observations a second at 60 frames: a new one every other frame.
                    double time = 2.0 + ((i / 2) / 30.0);
                    SyntheticMotion.WriteBody(frame.Body, SyntheticMotion.RaiseAndWave(time), time);
                    frame.Face.Timestamp = time;
                    frame.Face.HeadRotation = Quaternion.AngleAxis(20f * Mathf.Sin(i * 0.1f), Vector3.up);
                    watch.Restart();
                    controller.Update(frame, mirror: true, now: 2.0 + (i / 60.0));
                    model.Apply();
                    times[i] = watch.Elapsed.TotalMilliseconds;
                }

                Array.Sort(times);
                double median = times[Frames / 2];
                UnityEngine.Debug.Log($"[Avatar/Apply body] {sample}: median {median:F3} ms, 95th {times[Frames * 95 / 100]:F3} ms, max {times[Frames - 1]:F3} ms");
                Assert.That(median, Is.LessThan(1.0), "Far over the 0.3 ms target; see avatar detailed design 15.");
            }
        }

        [UnityTest]
        public IEnumerator Body_KeepsTheArmsOutOfTheHeadAndTorso([ValueSource(nameof(Samples))] string sample)
        {
            var load = new LoadResult();
            yield return Load(sample, load);
            using (IAvatarModel model = load.Model)
            {
                var vrm = (VrmAvatarModel)model;
                AvatarBodyShape shape = model.BodyShape;
                Assert.That(shape, Is.Not.Null, "Measured when loaded.");
                var watch = Stopwatch.StartNew();
                vrm.MeasureBody();
                double measuring = watch.Elapsed.TotalMilliseconds;
                ArmShape arm = shape.Arm(false);
                UnityEngine.Debug.Log($"[Avatar/BodyShape] {sample}: head center {shape.Head.Center * 100f} cm from its bone, half sizes {shape.Head.Radii * 100f} cm; "
                    + $"right arm radii upper {arm.UpperRadius * 100f:F1}, forearm {arm.LowerRadius * 100f:F1}, hand {arm.HandRadius * 100f:F1} cm, hand {arm.HandLength * 100f:F1} cm long; measured in {measuring:F1} ms");
                Assert.That(shape.Head.Radii.x, Is.InRange(0.05f, 0.25f));
                Assert.That(shape.Head.Center.y, Is.GreaterThan(0f), "The head's middle is above its bone.");
                Assert.That(arm.UpperRadius, Is.InRange(0.01f, 0.12f));
                for (int i = 0; i < AvatarBodyShape.TorsoBones.Count; i++)
                {
                    UnityEngine.Debug.Log($"[Avatar/BodyShape] {sample}: {AvatarBodyShape.TorsoBones[i]} " + (shape.Torso(i) is PartShape part ? $"center {part.Center * 100f} cm, half sizes {part.Radii * 100f} cm" : "not measured"));
                }

                // At rest the arms hang beside the torso; how far keeping them out of it moves them.
                var resting = new AvatarController(model, ExpressionMapping.Default);
                resting.Update(null, mirror: false, now: 1.0);
                Quaternion hanging = AvatarRestPose.UpperArm(false, AvatarRestPose.DefaultArmDownDegrees);
                float restTurn = Quaternion.Angle(resting.Body[UpperBodyBone.RightUpperArm], hanging);
                UnityEngine.Debug.Log($"[Avatar/BodyShape] {sample}: the resting arm turned {restTurn:F1}° out of the torso");
                Assert.That(restTurn, Is.LessThan(20f), "A resting arm only leans out against the torso.");

                // The right upper arm raised beside the head and the forearm across it, as in the user's camera.
                var frame = new TrackingFrame();
                SyntheticMotion.WriteArms(frame.Body, Vector3.down, Vector3.down, new Vector3(0.5f, 0.866f, 0f), Vector3.left, 1.0);
                BodyPose body = frame.Body;
                var tracked = new BodyTarget();
                PoseRetargeting.Retarget(body, false, model.Rig.Has(AvatarBone.UpperChest), AvatarRestPose.DefaultArmDownDegrees, tracked);
                Assert.That(ArmClearance.Touches(shape, tracked, Quaternion.identity, Quaternion.identity), Is.True, "As tracked, the forearm crosses this model's head.");

                var controller = new AvatarController(model, ExpressionMapping.Default);
                controller.Update(frame, mirror: false, now: 1.0);
                controller.Update(frame, mirror: false, now: 1.5);
                model.Apply();

                // The model's own bones, where UniVRM put them, clear the head a little smaller than measured.
                Transform root = model.Root;
                Vector3 elbow = root.InverseTransformPoint(vrm.ModelBone(HumanBodyBones.RightLowerArm).position);
                Vector3 wrist = root.InverseTransformPoint(vrm.ModelBone(HumanBodyBones.RightHand).position);
                Vector3 headBone = root.InverseTransformPoint(vrm.ModelBone(HumanBodyBones.Head).position);
                var placed = PlacedPart.Ellipsoid(headBone + (controller.Face.Head * shape.Head.Center), controller.Face.Head, shape.Head.Radii * 0.95f);
                Assert.That(placed.Hits(elbow, wrist, arm.LowerRadius), Is.False, "The forearm is out of the head.");

                // Arms folded: the right upper arm down and a little forward, the forearm across the chest.
                SyntheticMotion.WriteArms(body, Vector3.down, Vector3.down, new Vector3(0f, -0.9f, 0.44f), Vector3.left, 2.0);
                PoseRetargeting.Retarget(body, false, model.Rig.Has(AvatarBone.UpperChest), AvatarRestPose.DefaultArmDownDegrees, tracked);
                controller.Update(frame, mirror: false, now: 2.0);
                controller.Update(frame, mirror: false, now: 2.5);
                Quaternion neck = HeadPose.Neck(controller.Body.TorsoTop, controller.Face.Head);
                bool trackedTouches = ArmClearance.Touches(shape, tracked, neck, controller.Face.Head);
                UnityEngine.Debug.Log($"[Avatar/BodyShape] {sample}: folded arms touch as tracked: {trackedTouches}; upper arm turned {Quaternion.Angle(controller.Body[UpperBodyBone.RightUpperArm], tracked[UpperBodyBone.RightUpperArm]):F1}°, forearm {Quaternion.Angle(controller.Body[UpperBodyBone.RightLowerArm], tracked[UpperBodyBone.RightLowerArm]):F1}°");
                Assert.That(ArmClearance.Touches(shape, controller.Body, neck, controller.Face.Head), Is.False, "Folded arms stay out of the chest.");

                // The same in 2D: the forearm across the chest passes in front of it on the model's own bones.
                SyntheticMotion.WriteArms(body, Vector3.down, Vector3.down, Vector3.down, Vector3.left, 3.0);
                BodyFlattening.Flatten(body);
                controller.Update(frame, mirror: false, now: 3.0, armsInFront: true);
                controller.Update(frame, mirror: false, now: 3.5, armsInFront: true);
                model.Apply();
                float elbowAhead = root.InverseTransformPoint(vrm.ModelBone(HumanBodyBones.RightLowerArm).position).z - root.InverseTransformPoint(vrm.ModelBone(HumanBodyBones.RightUpperArm).position).z;
                UnityEngine.Debug.Log($"[Avatar/BodyShape] {sample}: in 2D the folded elbow is {elbowAhead * 100f:F1} cm in front of the shoulder");
                Assert.That(elbowAhead, Is.GreaterThan(0.03f), "In 2D the arm passes in front of the body.");
            }
        }

        [UnityTest]
        public IEnumerator Body_OnTheRecordings_ClearanceMovesTheArmsSmoothly([ValueSource(nameof(Samples))] string sample)
        {
            string folder = Path.Combine(DataRootResolver.ForCurrentUser().Resolve(null).DataRoot.GetDirectory(DataRootDirectory.Cache), TrackingRecordings.FolderName);
            TrackingRecordingInfo[] recordings = TrackingRecordings.List(folder).Where(info => info.Readable).ToArray();
            if (recordings.Length == 0)
            {
                Assert.Ignore($"No recordings in {folder}.");
            }

            var load = new LoadResult();
            yield return Load(sample, load);
            using (IAvatarModel model = load.Model)
            {
                // Each recording in 3D, and in 2D with the arms kept in front (tracking detailed design 8.6).
                foreach ((TrackingRecordingInfo info, bool flat) in recordings.SelectMany(recording => new[] { (recording, false), (recording, true) }))
                {
                    // As the app runs: corrections, the standard smoothing, then 60 frames a second, mirrored.
                    (TrackingRecordingHeader header, List<TrackingFrame> frames) = TrackingRecordingFile.ReadAll(Path.Combine(folder, info.Id));
                    ObservationCorrections corrections = ObservationCorrections.For(header.Traits, ObservationCorrections.Catalog);
                    var processor = new TrackingProcessor();
                    var corrected = new TrackingFrame();
                    var output = new TrackingFrame();
                    var controller = new AvatarController(model, ExpressionMapping.Default);
                    var tracked = new BodyMotion(model.Rig.Has(AvatarBone.UpperChest));
                    var before = new BodyTarget();
                    var beforeTracked = new BodyTarget();
                    var again = new BodyTarget();
                    float remainingSum = 0f, mostRemaining = 0f;
                    int next = 0, shown = 0, active = 0, added = 0, touching = 0;
                    float mostTurn = 0f, mostAdded = 0f, turnSum = 0f, imageSum = 0f;
                    double mostAddedAt = 0.0;
                    for (double now = frames[0].Time; now <= frames[frames.Count - 1].Time; now += 1.0 / 60.0)
                    {
                        for (; next < frames.Count && frames[next].Time <= now; next++)
                        {
                            corrections.Apply(frames[next], corrected);
                            if (flat)
                            {
                                BodyFlattening.Flatten(corrected.Body);
                            }

                            processor.Process(corrected, frames[next].Time, header.Supported, TrackingRegions.All, SmoothingLevel.Standard, output);
                        }

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
                                mostAddedAt = now - frames[0].Time;
                            }
                        }

                        before.CopyFrom(controller.Body);
                        beforeTracked.CopyFrom(tracked.Current);
                    }

                    UnityEngine.Debug.Log($"[Avatar/Clearance] {sample} \"{info.Description}\" {(flat ? "2D" : "3D")}: {shown} frames; arms turned on {active} ({100.0 * active / shown:F0}%), at most {mostTurn:F0}°, on average {(active > 0 ? turnSum / active : 0f):F1}°, of which on the image {(active > 0 ? imageSum / active : 0f):F1}°; still touching on {touching}, by {(touching > 0 ? remainingSum / touching : 0f):F1}° on average and {mostRemaining:F0}° at most; "
                        + $"frames with a jump clearance added over 5°: {added}, the largest {mostAdded:F0}° at {mostAddedAt:F1} s");
                }
            }
        }

        /// <summary>Where a model's own bone points, toward the next one, in the avatar root's space.</summary>
        private static Vector3 Direction(VrmAvatarModel model, Transform root, HumanBodyBones from, HumanBodyBones to)
        {
            return root.InverseTransformDirection(model.ModelBone(to).position - model.ModelBone(from).position);
        }

        [UnityTest]
        public IEnumerator Dispose_LeavesNoObjectsMeshesOrTextures([ValueSource(nameof(Samples))] string sample)
        {
            // The first load in a session creates UniVRM's shared objects, which stay; count after them.
            var warmUp = new LoadResult();
            yield return Load(sample, warmUp);
            warmUp.Model.Dispose();
            yield return null;
            yield return null;
            int meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length;
            int textures = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
            int materials = Resources.FindObjectsOfTypeAll<Material>().Length;
            int objects = Resources.FindObjectsOfTypeAll<GameObject>().Length;

            var load = new LoadResult();
            yield return Load(sample, load);
            load.Model.Dispose();

            // Destroy takes effect at the end of the frame.
            yield return null;
            yield return null;
            Assert.That(Resources.FindObjectsOfTypeAll<GameObject>().Length, Is.EqualTo(objects));
            Assert.That(Resources.FindObjectsOfTypeAll<Mesh>().Length, Is.EqualTo(meshes));
            Assert.That(Resources.FindObjectsOfTypeAll<Texture2D>().Length, Is.EqualTo(textures));
            Assert.That(Resources.FindObjectsOfTypeAll<Material>().Length, Is.EqualTo(materials));
        }

        [UnityTest]
        public IEnumerator Load_AFileThatIsNotVrm_IsNotVrm()
        {
            string path = Path.Combine(Path.GetTempPath(), "vv-not-vrm-" + Guid.NewGuid().ToString("N") + ".vrm");
            File.WriteAllBytes(path, new byte[] { 0x67, 0x6C, 0x54, 0x46, 2, 0, 0, 0, 12, 0, 0, 0 });
            try
            {
                Task<IAvatarModel> task = new VrmAvatarLoader().LoadAsync(path, CancellationToken.None);
                yield return WaitFor(task);
                Assert.That(task.IsFaulted, Is.True);
                Assert.That(task.Exception.InnerException, Is.InstanceOf<AvatarLoadException>());
                Assert.That(((AvatarLoadException)task.Exception.InnerException).Problem, Is.EqualTo(AvatarProblem.NotVrm));
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string SampleFolder()
        {
            string configured = Environment.GetEnvironmentVariable("VV_SAMPLES_DIR");
            if (!string.IsNullOrEmpty(configured))
            {
                return configured;
            }

            // The Unity project is unity/VirtualVesselStudio in the repository.
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "samples"));
        }

        private static IEnumerator Load(string sample, LoadResult result)
        {
            string path = Path.Combine(SampleFolder(), sample);
            Assume.That(File.Exists(path), Is.True, $"No sample models in {SampleFolder()}.");
            var watch = Stopwatch.StartNew();
            Task<IAvatarModel> task = new VrmAvatarLoader().LoadAsync(path, CancellationToken.None);
            double last = 0;
            while (!task.IsCompleted && watch.Elapsed.TotalSeconds < TimeoutSeconds)
            {
                yield return null;
                double now = watch.Elapsed.TotalMilliseconds;
                result.LongestFrameMilliseconds = Math.Max(result.LongestFrameMilliseconds, now - last);
                last = now;
            }

            Assert.That(task.IsCompleted, Is.True, "Loading timed out.");
            if (task.IsFaulted)
            {
                throw task.Exception.InnerException;
            }

            result.Seconds = watch.Elapsed.TotalSeconds;
            result.Model = task.Result;
        }

        private static IEnumerator WaitFor(Task task)
        {
            var watch = Stopwatch.StartNew();
            while (!task.IsCompleted && watch.Elapsed.TotalSeconds < TimeoutSeconds)
            {
                yield return null;
            }
        }

        private static float TotalBlendShapeWeight(SkinnedMeshRenderer[] renderers)
        {
            float total = 0;
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Mesh mesh = renderer.sharedMesh;
                for (int i = 0; mesh != null && i < mesh.blendShapeCount; i++)
                {
                    total += renderer.GetBlendShapeWeight(i);
                }
            }

            return total;
        }

        private sealed class LoadResult
        {
            public IAvatarModel Model { get; set; }

            public double Seconds { get; set; }

            public double LongestFrameMilliseconds { get; set; }
        }
    }
}
