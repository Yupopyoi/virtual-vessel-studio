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
using VirtualVessel.Tracking;
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
                SyntheticMotion.WriteBody(frame.Body, 90f, 1.0);
                BodyPose body = frame.Body;
                body[BodyJoint.RightElbow] = body[BodyJoint.RightShoulder] + (Vector3.forward * SyntheticMotion.UpperArmLength);
                body[BodyJoint.RightWrist] = body[BodyJoint.RightElbow] + (Vector3.forward * SyntheticMotion.ForearmLength);
                body[BodyJoint.LeftElbow] = body[BodyJoint.LeftShoulder] + (Vector3.left * SyntheticMotion.UpperArmLength);
                body[BodyJoint.LeftWrist] = body[BodyJoint.LeftElbow] + (Vector3.up * SyntheticMotion.ForearmLength);
                var controller = new AvatarController(model, ExpressionMapping.Default);
                controller.Update(frame, mirror: false, now: 1.0);
                controller.Update(frame, mirror: false, now: 1.5);
                model.Apply();

                // UniVRM carries the rig's rotations onto the model's own bones; their directions are what shows.
                Assert.That(Vector3.Angle(Direction(vrm, model.Root, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm), Vector3.forward), Is.LessThan(5f), "The right arm reaches forward.");
                Assert.That(Vector3.Angle(Direction(vrm, model.Root, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm), Vector3.left), Is.LessThan(5f), "The left upper arm is out sideways.");
                Assert.That(Vector3.Angle(Direction(vrm, model.Root, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand), Vector3.up), Is.LessThan(5f), "The left forearm points up.");

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
