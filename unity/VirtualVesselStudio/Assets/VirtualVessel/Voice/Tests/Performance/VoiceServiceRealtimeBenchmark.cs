using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEngine.TestTools;
using VirtualVessel.Audio;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.UI.Voice;
using VirtualVessel.Voice.Rvc;

namespace VirtualVessel.Voice.Tests
{
    /// <summary>
    /// The real service and models with speech fed at real time, under the load of the application: frames
    /// of 20 to 36 ms, as the application logged, and another model on the GPU every frame. It counts what
    /// the mix would count as running dry, which an idle Editor never showed.
    /// </summary>
    public sealed class VoiceServiceRealtimeBenchmark
    {
        private const double MinFrameMs = 20.0;
        private const double MaxFrameMs = 36.0;
        private const int Seconds = 20;

        [UnityTest]
        public IEnumerator Runs_AtRealTime_UnderLoad([Values(100, 250, 540)] int windowFrames)
        {
            string work = Environment.GetEnvironmentVariable("VV_VOICE_WORK");
            if (string.IsNullOrEmpty(work))
            {
                Assert.Ignore("VV_VOICE_WORK is not set.");
            }

            string folder = Path.Combine(work, "sentis");
            if (!File.Exists(Path.Combine(folder, VoiceService.GeneratorFile(windowFrames))))
            {
                Assert.Ignore($"No models for a window of {windowFrames} frames.");
            }

            float[] speech = WavFile.Read(File.ReadAllBytes(Environment.GetEnvironmentVariable("VV_VOICE_INPUT") ?? Path.Combine(work, "data", "source", "000.wav")), out int rate);
            speech = WavFile.ToRate(speech, rate, RvcStream.DeviceRate);
            var stage = new WavStage(speech);
            var loop = new ManualFrameLoop();
            using var service = new VoiceService(stage, loop, folder, null);
            service.WindowFrames = windowFrames;
            service.Semitones = 9f;
            service.InitializeAsync(CancellationToken.None).Wait();
            service.SetEnabled(true);
            while (service.State.Value == VoiceConversionState.Loading)
            {
                loop.Tick();
                yield return null;
            }

            Assert.That(service.State.Value, Is.EqualTo(VoiceConversionState.Running), service.Failure?.ToString());

            // Other GPU work every frame, standing in for the stream video, tracking, and the avatar.
            using var load = new Worker(ModelLoader.Load(Path.Combine(folder, "hubert_16000.sentis")), BackendType.GPUCompute);
            using var loadInput = new Tensor<float>(new TensorShape(1, 16000));
            var random = new System.Random(7);
            stage.Start();
            var frameMs = new List<double>();
            var workMs = new List<double>();
            var timings = new List<RvcTimings>();
            var clock = Stopwatch.StartNew();
            long seen = 0;
            while (clock.Elapsed.TotalSeconds < Seconds)
            {
                double frameStart = clock.Elapsed.TotalMilliseconds;
                load.Schedule(loadInput);
                loop.Tick();
                frameMs.Add(clock.Elapsed.TotalMilliseconds - frameStart);
                if (service.Stats.Blocks != seen)
                {
                    seen = service.Stats.Blocks;
                    workMs.Add(service.Stats.WorkMs);
                    timings.Add(service.LastTimings);
                }

                double length = MinFrameMs + random.NextDouble() * (MaxFrameMs - MinFrameMs);
                double left = frameStart + length - clock.Elapsed.TotalMilliseconds;
                if (left > 1.0)
                {
                    Thread.Sleep((int)left);
                }

                yield return null;
            }

            service.SetEnabled(false);
            var report = new StringBuilder($"Under load, window {windowFrames * 10} ms: {seen} blocks, {service.Stats.Dropped} dropped, {stage.Underruns} underruns\n");
            RvcStreamBenchmark.AppendStage(report, "voice in a frame", frameMs);
            RvcStreamBenchmark.AppendStage(report, "block work", workMs.Skip(3));
            RvcStreamBenchmark.AppendStage(report, "main prepare", timings.Skip(3).Select(t => t.Prepare));
            RvcStreamBenchmark.AppendStage(report, "main features", timings.Skip(3).Select(t => t.Features));
            RvcStreamBenchmark.AppendStage(report, "main pitch", timings.Skip(3).Select(t => t.Pitch));
            RvcStreamBenchmark.AppendStage(report, "main generator", timings.Skip(3).Select(t => t.Generator));
            RvcStreamBenchmark.AppendStage(report, "main finish", timings.Skip(3).Select(t => t.Finish));
            RvcStreamBenchmark.AppendStage(report, "wait features", timings.Skip(3).Select(t => t.FeaturesWait));
            RvcStreamBenchmark.AppendStage(report, "wait pitch", timings.Skip(3).Select(t => t.PitchWait));
            RvcStreamBenchmark.AppendStage(report, "wait generator", timings.Skip(3).Select(t => t.GeneratorWait));
            File.AppendAllText(Path.Combine(work, "out", "benchmark.txt"), report + "\n");
            RvcStreamBenchmark.WriteWav(Path.Combine(work, "out", $"unity_load_{windowFrames}.wav"), stage.Output.ToArray());
            Assert.That(seen, Is.GreaterThan(Seconds * 5));
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

        // Stands in for the microphone at real time, and for the mix, with its rules: it starts playing
        // once 20 ms waits, takes 10 ms at a time, and when it runs dry it drops the rest and waits again.
        private sealed class WavStage : IAudioStage
        {
            private const int Prime = 960;
            private const int MixBlock = 480;

            private readonly float[] _speech;
            private readonly Stopwatch _clock = new Stopwatch();
            private long _position;
            private long _written;
            private long _played;
            private long _playStart = -1;

            public WavStage(float[] speech)
            {
                _speech = speech;
            }

            public bool IsAvailable => true;

            public bool IsCapturing => true;

            public long Underruns { get; private set; }

            public List<float> Output { get; } = new List<float>();

            public void Start()
            {
                _clock.Start();
            }

            public void SetEnabled(bool enabled)
            {
            }

            public void Denoise(float[] samples, int count)
            {
            }

            public AudioStageDelay Delay => default;

            public int Read(float[] frames, int capacity)
            {
                Play();
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
                Play();
                for (int i = 0; i < count; i++)
                {
                    Output.Add(frames[i * 2]);
                }

                _written += count;
                Play();
            }

            private long Now => _clock.ElapsedTicks * RvcStream.DeviceRate / Stopwatch.Frequency;

            private void Play()
            {
                if (_playStart < 0)
                {
                    if (_written - _played >= Prime)
                    {
                        _playStart = Now;
                    }

                    return;
                }

                long blocks = (Now - _playStart) / MixBlock;
                long due = _played + blocks * MixBlock;
                if (due > _written)
                {
                    Underruns++;
                    _played = _written;
                    _playStart = -1;
                    return;
                }

                _played = due;
                _playStart += blocks * MixBlock;
            }
        }
    }
}
