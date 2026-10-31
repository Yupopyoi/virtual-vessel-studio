using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Credentials;
using VirtualVessel.UI.Streaming;
using VirtualVessel.Video;
using VirtualVessel.Video.Recording;
using Debug = UnityEngine.Debug;

namespace VirtualVessel.Streaming.Tests
{
    /// <summary>
    /// The whole path on this PC, without a server on the internet: the real shader and plugin, publishing to a
    /// local ffmpeg in listen mode, and the frames that arrived read back. Ignored without the plugin or ffmpeg.
    /// </summary>
    public sealed class StreamingPlayTests
    {
        private const int Width = 640;
        private const int Height = 360;
        private const int Port = 19355;

        [UnityTest]
        public IEnumerator AStream_ReachesTheServer_TheRightWayUp([Values(StreamingCodec.H264, StreamingCodec.Av1)] StreamingCodec codec)
        {
            if (Run("ffmpeg", "-hide_banner -version", 5000) != 0)
            {
                Assert.Ignore("ffmpeg is not on the PATH.");
            }

            string file = Path.Combine(Path.GetTempPath(), $"vv_streaming_{Guid.NewGuid():N}.flv");
            string raw = Path.ChangeExtension(file, ".rgb");
            Process listener = Start("ffmpeg", $"-hide_banner -loglevel error -y -listen 1 -timeout 20 -i rtmp://127.0.0.1:{Port}/live/test-key -c copy -f flv \"{file}\"");
            var target = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);

            // Texture2D rows go from the bottom: red is at the top of the picture.
            image.SetPixels32(Enumerable.Range(0, Width * Height).Select(i => i / Width >= Height / 2 ? new Color32(255, 0, 0, 255) : new Color32(255, 255, 255, 255)).ToArray());
            image.Apply();
            Graphics.Blit(image, target);

            var video = new ObservableValue<Texture>(target);
            var clock = new StopwatchMonotonicClock();
            var loop = new FrameLoop();
            var credentials = new MemoryCredentials();
            var settings = new StreamSettings { ServerUrl = $"rtmp://127.0.0.1:{Port}/live", VideoCodec = codec == StreamingCodec.Av1 ? "av1" : "h264", VideoBitrate = 2_000_000 };
            StreamingEnvironment environment = StreamingEnvironment.Native(video, clock);
            if (codec == StreamingCodec.Av1 && !environment.Av1Available)
            {
                listener.Kill();
                Assert.Ignore("The GPU has no AV1 encoder.");
            }

            var service = new StreamingService(settings, null, new VideoSize(Width, Height), 30, 160_000, loop, clock, credentials, environment, null, null);
            service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(service.SetStreamKey("test-key"), Is.True);

            yield return new WaitForSeconds(1.5f); // Until ffmpeg listens.
            service.Start();
            double live = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - live < 3)
            {
                loop.Tick(exception => throw exception);
                yield return null;
            }

            StreamingState reached = service.State.Value;
            StreamingStats stats = service.Stats;
            service.Stop();
            while (service.State.Value == StreamingState.Stopping)
            {
                loop.Tick(exception => throw exception);
                yield return null;
            }

            service.Dispose();
            listener.WaitForExit(10_000);
            if (!listener.HasExited)
            {
                listener.Kill();
            }

            UnityEngine.Object.Destroy(image);
            target.Release();
            UnityEngine.Object.Destroy(target);
            if (service.Problem.Value == StreamingProblem.EncoderFailed)
            {
                Assert.Ignore("The encoder is not available on this PC.");
            }

            Debug.Log($"Streamed {codec}: {reached}, {stats.BitsPerSecond / 1000} kbit/s, {stats.DroppedFrames} dropped.");
            Assert.That(reached, Is.EqualTo(StreamingState.Live));
            Assert.That(File.Exists(file), Is.True, "ffmpeg wrote what it received.");

            Assert.That(Run("ffmpeg", $"-hide_banner -loglevel error -y -i \"{file}\" -frames:v 1 -f rawvideo -pix_fmt rgb24 \"{raw}\"", 10_000), Is.EqualTo(0));
            byte[] frame = File.ReadAllBytes(raw);
            File.Delete(file);
            File.Delete(raw);
            Assert.That(frame.Length, Is.EqualTo(Width * Height * 3));
            AssertPixel(frame, 10, 255, 0, 0, "red at the top");
            AssertPixel(frame, Height - 11, 255, 255, 255, "white at the bottom");
        }

        private static void AssertPixel(byte[] frame, int row, byte red, byte green, byte blue, string what)
        {
            int at = ((row * Width) + (Width / 2)) * 3;
            Assert.That(frame[at], Is.EqualTo(red).Within(12), what);
            Assert.That(frame[at + 1], Is.EqualTo(green).Within(12), what);
            Assert.That(frame[at + 2], Is.EqualTo(blue).Within(12), what);
        }

        private static Process Start(string file, string arguments)
        {
            return Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true });
        }

        private static int Run(string file, string arguments, int timeoutMs)
        {
            try
            {
                using (Process process = Start(file, arguments))
                {
                    return process.WaitForExit(timeoutMs) ? process.ExitCode : -1;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return -1;
            }
        }

        private sealed class MemoryCredentials : ICredentialStore
        {
            private readonly Dictionary<string, string> _stored = new Dictionary<string, string>();

            public bool Exists(string credentialId)
            {
                return credentialId != null && _stored.ContainsKey(credentialId);
            }

            public CredentialResult Write(string credentialId, string secret)
            {
                _stored[credentialId] = secret;
                return new CredentialResult(CredentialStatus.Ok);
            }

            public CredentialResult Read(string credentialId, out string secret)
            {
                return _stored.TryGetValue(credentialId, out secret) ? new CredentialResult(CredentialStatus.Ok) : new CredentialResult(CredentialStatus.NotFound);
            }

            public CredentialResult Delete(string credentialId)
            {
                return new CredentialResult(_stored.Remove(credentialId) ? CredentialStatus.Ok : CredentialStatus.NotFound);
            }
        }
    }
}
