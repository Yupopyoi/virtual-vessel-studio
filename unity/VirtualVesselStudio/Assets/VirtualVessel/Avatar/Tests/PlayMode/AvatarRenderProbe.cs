using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VirtualVessel.ProjectData.DataRoot;
using VirtualVessel.Tracking;
using VirtualVessel.Tracking.Recording;
using Object = UnityEngine.Object;

namespace VirtualVessel.Avatar.Tests
{
    /// <summary>
    /// What the render probe draws (avatar detailed design 14.1), read from environment variables so that
    /// tools/unity/render-probe.ps1 can set them.
    /// </summary>
    internal sealed class RenderProbeOptions
    {
        public const string RecordingVariable = "VV_RENDER_RECORDING";
        public const string TimesVariable = "VV_RENDER_TIMES";
        public const string SampleVariable = "VV_RENDER_SAMPLE";
        public const string LevelVariable = "VV_RENDER_LEVEL";
        public const string DepthVariable = "VV_RENDER_DEPTH";
        public const string OutputVariable = "VV_RENDER_OUT";
        public const string DefaultSample = "otoha-chan.vrm";

        /// <summary>Without times, a picture every this many seconds.</summary>
        public const double DefaultInterval = 2.0;

        public string Recording { get; private set; }

        /// <summary>Seconds from the recording's first observation; null for every <see cref="DefaultInterval"/>.</summary>
        public double[] Times { get; private set; }

        public string Sample { get; private set; }

        public int Smoothing { get; private set; }

        public bool Flat { get; private set; }

        public string Output { get; private set; }

        /// <summary>
        /// The options from <paramref name="variable"/>: the recording (a file in
        /// <paramref name="recordingsFolder"/> or a full path), the times (comma-separated seconds), the
        /// sample model, the smoothing level, "2d" for the 2D body, and the output folder. Pure.
        /// </summary>
        public static RenderProbeOptions From(Func<string, string> variable, string recordingsFolder, string defaultOutput)
        {
            string recording = variable(RecordingVariable);
            string times = variable(TimesVariable);
            string level = variable(LevelVariable);
            return new RenderProbeOptions
            {
                Recording = string.IsNullOrWhiteSpace(recording) ? null
                    : Path.IsPathRooted(recording) ? recording
                    : Path.Combine(recordingsFolder, recording),
                Times = string.IsNullOrWhiteSpace(times) ? null
                    : times.Split(',').Select(t => double.Parse(t.Trim(), CultureInfo.InvariantCulture)).OrderBy(t => t).ToArray(),
                Sample = string.IsNullOrWhiteSpace(variable(SampleVariable)) ? DefaultSample : variable(SampleVariable).Trim(),
                Smoothing = string.IsNullOrWhiteSpace(level) ? SmoothingLevel.Standard : SmoothingLevel.Clamp(int.Parse(level.Trim(), CultureInfo.InvariantCulture)),
                Flat = string.Equals(variable(DepthVariable)?.Trim(), "2d", StringComparison.OrdinalIgnoreCase),
                Output = string.IsNullOrWhiteSpace(variable(OutputVariable)) ? defaultOutput : variable(OutputVariable).Trim(),
            };
        }

        /// <summary>The times to draw, in seconds from the start, for a recording <paramref name="length"/> seconds long. Pure.</summary>
        public IReadOnlyList<double> TimesFor(double length)
        {
            if (Times != null)
            {
                return Times.Where(t => t >= 0 && t <= length).ToArray();
            }

            var every = new List<double>();
            for (double t = DefaultInterval; t <= length; t += DefaultInterval)
            {
                every.Add(t);
            }

            return every;
        }
    }

    /// <summary>
    /// The render probe (avatar detailed design 14.1): replays a developer's tracking recording on a sample
    /// model as the app runs it, and at chosen times draws the avatar from the stream camera and each hand up
    /// close, from the front and from the palm's side, and writes the finger angles. For checking motion,
    /// such as a fist's thumb, without the user recording a screen video each time.
    /// </summary>
    /// <remarks>Run it with tools/unity/render-probe.ps1. Explicit: it needs the developer's recordings.</remarks>
    [Explicit("Draws a developer's recording; run with tools/unity/render-probe.ps1.")]
    [Category("Explicit")]
    public sealed class AvatarRenderProbe
    {
        private const int Width = 1280;
        private const int Height = 720;

        // The stream camera of the app (Stage's default camera point), and its field of view.
        private static readonly Vector3 s_streamCamera = new Vector3(0f, 1.25f, -1.9f);
        private const float StreamFieldOfView = 30f;

        // How far the close cameras are from the hand, in meters.
        private const float CloseDistance = 0.25f;

        [UnityTest]
        public IEnumerator Draw()
        {
            string recordings = Path.Combine(DataRootResolver.ForCurrentUser().Resolve(null).DataRoot.GetDirectory(DataRootDirectory.Cache), TrackingRecordings.FolderName);
            RenderProbeOptions options = RenderProbeOptions.From(Environment.GetEnvironmentVariable, recordings, Path.Combine(Path.GetTempPath(), "VirtualVesselRender"));
            if (options.Recording == null || !File.Exists(options.Recording))
            {
                Assert.Ignore($"No recording to draw: set {RenderProbeOptions.RecordingVariable} to a file in {recordings} or a full path.");
            }

            Directory.CreateDirectory(options.Output);
            var load = new SampleLoad();
            yield return VrmSamples.Load(options.Sample, load);
            var light = new GameObject("Render probe light");
            var cameraObject = new GameObject("Render probe camera");
            var target = new RenderTexture(Width, Height, 24);
            var values = new StringBuilder("seconds,side," + string.Join(",", FingerColumns()) + "\n");
            try
            {
                using (IAvatarModel model = load.Model)
                {
                    foreach (Renderer renderer in model.Root.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.enabled = true;
                    }

                    // As in the app: the avatar faces the stream camera.
                    model.Root.rotation = Quaternion.Euler(0f, 180f, 0f);
                    light.transform.rotation = Quaternion.Euler(35f, 15f, 0f);
                    light.AddComponent<Light>().type = LightType.Directional;
                    Camera camera = cameraObject.AddComponent<Camera>();
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(0.15f, 0.12f, 0.25f);
                    camera.targetTexture = target;
                    Animator animator = model.Root.GetComponentInChildren<Animator>();

                    var replay = new RecordingReplay(options.Recording, options.Smoothing, options.Flat);
                    var controller = new AvatarController(model, ExpressionMapping.Default);
                    IReadOnlyList<double> times = options.TimesFor(replay.Length);
                    int next = 0;
                    foreach (double now in replay.Frames())
                    {
                        controller.Update(replay.Output, mirror: true, now, armsInFront: options.Flat);
                        if (next >= times.Count || now - replay.Start < times[next])
                        {
                            continue;
                        }

                        double at = times[next++];
                        model.Apply();
                        yield return null;
                        string stamp = at.ToString("000.0", CultureInfo.InvariantCulture);
                        Shoot(camera, target, s_streamCamera, s_streamCamera + Vector3.forward, Vector3.up, StreamFieldOfView, Path.Combine(options.Output, $"{stamp}-stream.png"));
                        foreach (bool left in new[] { false, true })
                        {
                            string side = left ? "left" : "right";
                            HandViews(animator, left, out Vector3 centre, out Vector3 palm, out Vector3 along);
                            Shoot(camera, target, centre + (Vector3.back * CloseDistance), centre, Vector3.up, StreamFieldOfView, Path.Combine(options.Output, $"{stamp}-{side}-front.png"));
                            Shoot(camera, target, centre + (palm * CloseDistance), centre, along, StreamFieldOfView, Path.Combine(options.Output, $"{stamp}-{side}-palm.png"));
                            values.Append(stamp).Append(',').Append(side).Append(',').Append(FingerValues(left ? controller.LeftFingers : controller.RightFingers)).Append('\n');
                        }

                        if (next >= times.Count)
                        {
                            break;
                        }
                    }

                    File.WriteAllText(Path.Combine(options.Output, "fingers.csv"), values.ToString());
                    Debug.Log($"[RenderProbe] {times.Count} moments of {Path.GetFileName(options.Recording)} on {options.Sample} in {options.Output}");
                }
            }
            finally
            {
                target.Release();
                Object.Destroy(target);
                Object.Destroy(cameraObject);
                Object.Destroy(light);
            }
        }

        /// <summary>
        /// The avatar's own <paramref name="left"/> or right hand: the middle of its palm, the way out of the
        /// palm, and the way the fingers point, from the model's bones (as <see cref="FingerRest"/> does).
        /// </summary>
        private static void HandViews(Animator animator, bool left, out Vector3 centre, out Vector3 palm, out Vector3 along)
        {
            Vector3 P(HumanBodyBones bone) => animator.GetBoneTransform(bone).position;
            Vector3 wrist = P(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Vector3 middle = P(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            Vector3 thumbward = P(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal) - P(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            along = (middle - wrist).normalized;
            Vector3 back = (left ? Vector3.Cross(along, thumbward) : Vector3.Cross(thumbward, along)).normalized;
            palm = -back;
            centre = (wrist + middle) * 0.5f;
        }

        private static void Shoot(Camera camera, RenderTexture target, Vector3 from, Vector3 to, Vector3 up, float fieldOfView, string path)
        {
            camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from, up));
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.02f;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.Destroy(texture);
        }

        private static IEnumerable<string> FingerColumns()
        {
            for (Finger finger = Finger.Thumb; finger <= Finger.Little; finger++)
            {
                for (int joint = 0; joint < FingerPose.JointCount; joint++)
                {
                    yield return $"{finger}{joint}".ToLowerInvariant();
                }

                yield return $"{finger}Spread".ToLowerInvariant();
            }
        }

        private static string FingerValues(FingerPose pose)
        {
            var columns = new List<string>();
            for (Finger finger = Finger.Thumb; finger <= Finger.Little; finger++)
            {
                for (int joint = 0; joint < FingerPose.JointCount; joint++)
                {
                    columns.Add(pose.Bend(finger, joint).ToString("0.0", CultureInfo.InvariantCulture));
                }

                columns.Add(pose.Spread(finger).ToString("0.0", CultureInfo.InvariantCulture));
            }

            return string.Join(",", columns);
        }
    }
}
