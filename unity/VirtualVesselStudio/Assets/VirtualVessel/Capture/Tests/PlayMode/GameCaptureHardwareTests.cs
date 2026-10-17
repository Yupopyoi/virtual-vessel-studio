using System;
using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VirtualVessel.Capture.Game;
using VirtualVessel.Core.Time;

namespace VirtualVessel.Capture.Tests
{
    /// <summary>
    /// Checks with real camera devices (capture detailed design 14). Explicit, so they run only when
    /// named: they need a capture board whose game device is on. VV_GAME_CAPTURE_DEVICE chooses the
    /// board by name; without it the first camera is opened.
    /// </summary>
    [Explicit("Needs a capture board with a video input.")]
    public sealed class GameCaptureHardwareTests
    {
        private const string DeviceVariable = "VV_GAME_CAPTURE_DEVICE";

        [UnityTest]
        public IEnumerator ARealBoard_DeliversVideo()
        {
            var clock = new StopwatchMonotonicClock();
            var settings = new CaptureSettings { GameDeviceId = Environment.GetEnvironmentVariable(DeviceVariable) };
            var source = new GameCaptureSource(new UnityCameraDevices(), settings, clock, null, null);
            try
            {
                source.Start();

                // Wait past the first full second of frames, so that the frame rate is measured too.
                float deadline = Time.realtimeSinceStartup + 10f;
                while (Time.realtimeSinceStartup < deadline && source.Info.Value.FrameRate == 0 && source.State.Value != CaptureState.Error)
                {
                    yield return null;
                    source.Tick(clock.GetTimestamp());
                }

                CaptureSourceInfo info = source.Info.Value;
                Assert.That(source.State.Value, Is.EqualTo(CaptureState.Capturing), $"Problem: {source.Problem.Value}");
                Assert.That(source.Texture.Value, Is.Not.Null);
                UnityEngine.Debug.Log($"GameCapture hardware check: {info.Width}x{info.Height} at {info.FrameRate} fps from '{info.DeviceName}'.");
            }
            finally
            {
                source.Dispose();
            }
        }

        /// <summary>
        /// How long listing the cameras takes. An unavailable source lists them every two seconds on
        /// the main thread (capture detailed design 12); this does not open any camera.
        /// </summary>
        [Test]
        public void ListingCameras_Timing()
        {
            const int Count = 10;
            var devices = new UnityCameraDevices();

            // The first call may load the platform's camera backend.
            var first = Stopwatch.StartNew();
            int found = devices.List().Count;
            first.Stop();

            var watch = Stopwatch.StartNew();
            for (int i = 0; i < Count; i++)
            {
                found = devices.List().Count;
            }

            watch.Stop();
            UnityEngine.Debug.Log($"Listing {found} cameras: first {first.Elapsed.TotalMilliseconds:F2} ms, then {watch.Elapsed.TotalMilliseconds / Count:F2} ms on average.");
        }
    }
}
