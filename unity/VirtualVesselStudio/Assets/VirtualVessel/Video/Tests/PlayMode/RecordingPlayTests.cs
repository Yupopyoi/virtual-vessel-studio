using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;
using VirtualVessel.UI.Recording;
using VirtualVessel.Video.Recording;
using Debug = UnityEngine.Debug;

namespace VirtualVessel.Video.Tests
{
    /// <summary>
    /// The stream video recorded to a file with the real readback and the native encoder (recording
    /// detailed design 14). Needs the encoder plugin (tools/native/build-encode.ps1) and an H.264 encoder.
    /// </summary>
    public sealed class RecordingPlayTests
    {
        private const string PluginPath = "VirtualVessel/Video/Plugins/x86_64/VirtualVessel.Encode.Native.dll";

        private GameObject _cameraObject;
        private FrameLoop _loop;
        private VideoService _video;
        private RecordingService _recording;
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            if (!File.Exists(Path.Combine(UnityEngine.Application.dataPath, PluginPath)))
            {
                Assert.Ignore("The encoder plugin is not built; run tools/native/build-encode.ps1.");
            }

            _folder = Path.Combine(Path.GetTempPath(), "vv-recording-play-" + Guid.NewGuid().ToString("N"));
            _cameraObject = new GameObject("TestMainCamera");
            Camera camera = _cameraObject.AddComponent<Camera>();
            _loop = new FrameLoop();
            var clock = new StopwatchMonotonicClock();
            _video = new VideoService(new VideoSettings { Width = 640, Height = 360, BackgroundColor = Color.blue }, camera, _loop, clock, null, null);
            _video.InitializeAsync(CancellationToken.None).Wait();
            _recording = new RecordingService(
                new RecordingSettings { Width = 640, Height = 360, RecordingFolder = _folder },
                null,
                _video.Size,
                _loop,
                clock,
                RecordingEnvironment.Native(_video.Video, clock),
                null,
                null);
            _recording.InitializeAsync(CancellationToken.None).Wait();
        }

        [TearDown]
        public void TearDown()
        {
            _recording?.ShutdownAsync(CancellationToken.None).Wait();
            _recording?.Dispose();
            _video?.Dispose();
            if (_cameraObject != null)
            {
                UnityEngine.Object.Destroy(_cameraObject);
            }

            if (_folder != null && Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        [UnityTest]
        public IEnumerator TwoSeconds_AreRecordedToAPlayableFile([Values(RecordingCodec.Av1, RecordingCodec.H264)] RecordingCodec codec)
        {
            if (codec == RecordingCodec.Av1 && !_recording.Options.Value.Av1Available)
            {
                Assert.Ignore("This GPU has no AV1 encoder.");
            }

            _recording.SetOptions(_recording.Options.Value.WithCodec(codec));
            _recording.Start();
            yield return Until(RecordingState.Recording);
            var recorded = Stopwatch.StartNew();
            while (recorded.Elapsed < TimeSpan.FromSeconds(2))
            {
                _loop.Tick(e => throw e);
                yield return null;
            }

            _recording.Stop();
            yield return Until(RecordingState.Idle);

            string file = _recording.LastFile;
            Assert.That(file, Is.Not.Null.And.StartsWith(_folder));
            Assert.That(new FileInfo(file).Length, Is.GreaterThan(1000));

            // ffprobe, where it is installed, reads the file as a player would.
            (string name, int frames, double seconds)? probed = Probe(file);
            if (probed is (string name, int frames, double seconds))
            {
                Debug.Log($"Recorded {name}: {frames} frames over {seconds:0.00} s.");
                Assert.That(name, Is.EqualTo(codec == RecordingCodec.Av1 ? "av1" : "h264"));
                Assert.That(seconds, Is.EqualTo(2.0).Within(0.3));
                Assert.That(frames, Is.EqualTo(60).Within(8), "30 fps over two seconds.");
            }
        }

        private IEnumerator Until(RecordingState state)
        {
            var waited = Stopwatch.StartNew();
            while (_recording.State.Value != state)
            {
                Assert.That(_recording.State.Value, Is.Not.EqualTo(RecordingState.Failed), $"Problem {_recording.Problem.Value}.");
                Assert.That(waited.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)), $"Still {_recording.State.Value}.");
                _loop.Tick(e => throw e);
                yield return null;
            }
        }

        private static (string Codec, int Frames, double Seconds)? Probe(string file)
        {
            try
            {
                var start = new ProcessStartInfo("ffprobe", $"-v error -count_frames -select_streams v:0 -show_entries stream=codec_name,nb_read_frames:format=duration -of csv=p=0 \"{file}\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                using Process process = Process.Start(start);
                string[] lines = process.StandardOutput.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                process.WaitForExit(10_000);
                string[] stream = lines[0].Split(',');
                return (stream[0].Trim(), int.Parse(stream[1].Trim(), CultureInfo.InvariantCulture), double.Parse(lines[1].Trim(), CultureInfo.InvariantCulture));
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception || exception is FormatException || exception is IndexOutOfRangeException)
            {
                return null;
            }
        }
    }
}
