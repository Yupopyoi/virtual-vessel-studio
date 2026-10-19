using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VirtualVessel.Capture.SubScreen;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Performance;

namespace VirtualVessel.Capture.Tests
{
    /// <summary>
    /// Checks with the real native plugin and a real monitor (capture detailed design 14). Explicit,
    /// so they run only when named: they need the plugin built with tools/native/build-capture.ps1.
    /// The captured image stays in memory and is thrown away; only sizes and times are logged.
    /// </summary>
    [Explicit("Needs the native plugin and a monitor.")]
    [Category("Explicit")]
    public sealed class SubScreenCaptureHardwareTests
    {
        [UnityTest]
        public IEnumerator ARealMonitor_IsCapturedThroughThePlugin()
        {
            var clock = new StopwatchMonotonicClock();
            var metrics = new PerformanceMetricsService(new PerformanceSettings { DetailedEnabled = true }, clock, new UtcSystemClock(), log: null);
            var source = new SubScreenCaptureSource(new DesktopDuplicationNative(), new CaptureSettings(), clock, metrics, null);
            try
            {
                source.RefreshDevices();
                Assert.That(source.Devices, Is.Not.Empty, "The plugin lists the monitors.");

                source.Start();
                float deadline = Time.realtimeSinceStartup + 5f;
                while (Time.realtimeSinceStartup < deadline && source.State.Value != CaptureState.Capturing && source.State.Value != CaptureState.Error)
                {
                    yield return null;
                    source.Tick(clock.GetTimestamp());
                }

                Assert.That(source.State.Value, Is.EqualTo(CaptureState.Capturing), $"Problem: {source.Problem.Value}");
                CaptureSourceInfo info = source.Info.Value;
                Assert.That(source.Texture.Value.width, Is.EqualTo(info.Width));
                MetricSnapshot upload = metrics.GetSnapshot().Metrics.Single(m => m.Module == "Capture" && m.Name == "subscreen.Upload");
                UnityEngine.Debug.Log($"SubScreenCapture hardware check: '{info.DeviceName}' {info.Width}x{info.Height}, {upload.SampleCount} uploads, last {upload.Last:F2} ms.");
            }
            finally
            {
                var closing = Stopwatch.StartNew();
                source.Dispose();
                UnityEngine.Debug.Log($"SubScreenCapture hardware check: closing took {closing.Elapsed.TotalMilliseconds:F0} ms.");
            }
        }

        /// <summary>
        /// The main-thread cost of uploading one frame, as SubScreenCaptureSource does it: copying
        /// native memory into a BGRA32 texture and applying it (capture detailed design 15).
        /// </summary>
        [TestCase(1920, 1080)]
        [TestCase(3840, 2160)]
        public void Upload_Timing(int width, int height)
        {
            const int Count = 60;
            int size = width * height * 4;
            IntPtr pixels = Marshal.AllocHGlobal(size);
            var texture = new Texture2D(width, height, TextureFormat.BGRA32, mipChain: false, linear: false);
            try
            {
                var bytes = new byte[size];
                Marshal.Copy(bytes, 0, pixels, size);

                // The first upload creates the GPU resource; leave it out.
                texture.LoadRawTextureData(pixels, size);
                texture.Apply(updateMipmaps: false);

                var times = new double[Count];
                for (int i = 0; i < Count; i++)
                {
                    var watch = Stopwatch.StartNew();
                    texture.LoadRawTextureData(pixels, size);
                    texture.Apply(updateMipmaps: false);
                    times[i] = watch.Elapsed.TotalMilliseconds;
                }

                Array.Sort(times);
                double average = 0;
                foreach (double time in times)
                {
                    average += time / Count;
                }

                UnityEngine.Debug.Log($"Upload {width}x{height}: average {average:F2} ms, median {times[Count / 2]:F2} ms, max {times[Count - 1]:F2} ms over {Count} uploads.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
                Marshal.FreeHGlobal(pixels);
            }
        }
    }
}
