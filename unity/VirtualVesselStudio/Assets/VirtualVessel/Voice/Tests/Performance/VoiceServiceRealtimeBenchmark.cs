using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using VirtualVessel.Audio;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.UI.Voice;
using VirtualVessel.Voice.Rvc;

namespace VirtualVessel.Voice.Tests
{
    /// <summary>
    /// The real service and models with speech fed at real time and frames at 60 Hz, as in the application,
    /// to see what a frame loses to the conversion and how late the audio comes back.
    /// </summary>
    public sealed class VoiceServiceRealtimeBenchmark
    {
        private const double FrameMs = 1000.0 / 60.0;
        private const int Frames = 60 * 20;

        [UnityTest]
        public IEnumerator Runs_AtRealTime_On60HzFrames()
        {
            string work = Environment.GetEnvironmentVariable("VV_VOICE_WORK");
            if (string.IsNullOrEmpty(work))
            {
                Assert.Ignore("VV_VOICE_WORK is not set.");
            }

            float[] speech = RvcStreamBenchmark.ReadWav(Path.Combine(work, "data", "source", "000.wav"));
            var stage = new WavStage(speech);
            var loop = new ManualFrameLoop();
            using var service = new VoiceService(stage, loop, Path.Combine(work, "sentis"), null);
            service.Semitones = 13f;
            service.InitializeAsync(CancellationToken.None).Wait();
            service.SetEnabled(true);

            while (service.State.Value == VoiceConversionState.Loading)
            {
                loop.Tick();
                yield return null;
            }

            Assert.That(service.State.Value, Is.EqualTo(VoiceConversionState.Running));
            stage.Start();
            var frameMs = new List<double>(Frames);
            var workMs = new List<double>();
            var clock = Stopwatch.StartNew();
            long seen = 0;
            for (int frame = 0; frame < Frames; frame++)
            {
                double before = clock.Elapsed.TotalMilliseconds;
                loop.Tick();
                double spent = clock.Elapsed.TotalMilliseconds - before;
                frameMs.Add(spent);
                if (service.Stats.Blocks != seen)
                {
                    seen = service.Stats.Blocks;
                    workMs.Add(service.Stats.WorkMs);
                }

                double due = (frame + 1) * FrameMs;
                double left = due - clock.Elapsed.TotalMilliseconds;
                if (left > 1.0)
                {
                    Thread.Sleep((int)left);
                }

                yield return null;
            }

            Assert.That(service.Failure, Is.Null, service.Failure?.ToString());
            Assert.That(seen, Is.GreaterThan(0), $"No block came back. State {service.State.Value}, busy frames {frameMs.Count}.");
            service.SetEnabled(false);
            var report = new StringBuilder($"Real time at 60 Hz: {seen} blocks, {service.Stats.Dropped} dropped\n");
            RvcStreamBenchmark.AppendStage(report, "frame cost", frameMs);
            RvcStreamBenchmark.AppendStage(report, "block work", workMs.Skip(3));
            RvcStreamBenchmark.AppendStage(report, "behind", stage.Behind.Skip(3));
            File.AppendAllText(Path.Combine(work, "out", "benchmark.txt"), report + "\n");
            RvcStreamBenchmark.WriteWav(Path.Combine(work, "out", "unity_realtime.wav"), stage.Output.ToArray());
            Assert.That(seen, Is.GreaterThan(150));
        }

        private sealed class ManualFrameLoop : IFrameLoop
        {
            private Action _tick;

            public IDisposable Register(Action tick)
            {
                _tick = tick;
                return new Registration(this);
            }

            public void Tick()
            {
                _tick?.Invoke();
            }

            private sealed class Registration : IDisposable
            {
                private readonly ManualFrameLoop _loop;

                public Registration(ManualFrameLoop loop)
                {
                    _loop = loop;
                }

                public void Dispose()
                {
                    _loop._tick = null;
                }
            }
        }

        // Stands in for the microphone at real time, and for the mix: it records what comes back.
        private sealed class WavStage : IAudioStage
        {
            private readonly float[] _speech;
            private readonly Stopwatch _clock = new Stopwatch();
            private long _position;
            private long _written;

            public WavStage(float[] speech)
            {
                _speech = speech;
            }

            public bool IsAvailable => true;

            public bool IsCapturing => true;

            public List<float> Output { get; } = new List<float>();

            /// <summary>How long past each block's last input it was written, in ms.</summary>
            public List<double> Behind { get; } = new List<double>();

            public void Start()
            {
                _clock.Start();
            }

            public void SetEnabled(bool enabled)
            {
            }

            public int Read(float[] frames, int capacity)
            {
                long due = _clock.ElapsedMilliseconds * RvcStream.DeviceRate / 1000;
                int count = (int)Math.Min(capacity, due - _position);
                for (int i = 0; i < count; i++)
                {
                    float sample = _speech[(_position + i) % _speech.Length];
                    frames[i * 2] = sample;
                    frames[i * 2 + 1] = sample;
                }

                _position += count;
                return count;
            }

            public void Write(float[] frames, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    Output.Add(frames[i * 2]);
                }

                _written += count;
                Behind.Add(_clock.Elapsed.TotalMilliseconds - _written * 1000.0 / RvcStream.DeviceRate);
            }
        }
    }
}
