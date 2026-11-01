using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEngine;
using VirtualVessel.Voice.Rvc;

namespace VirtualVessel.Voice.Tests
{
    /// <summary>
    /// Runs speech through the real models block by block and reports each stage's time, then writes the
    /// converted audio to listen to. Models and speech are not in the repository; set VV_VOICE_WORK.
    /// </summary>
    public sealed class RvcStreamBenchmark
    {
        private const int WarmupBlocks = 5;
        private const int HeadGuard = 2;
        private const int TailGuard = 3;

        private static string WorkFolder => Environment.GetEnvironmentVariable("VV_VOICE_WORK");

        private static float Semitones => float.Parse(Environment.GetEnvironmentVariable("VV_VOICE_SEMITONES") ?? "0", System.Globalization.CultureInfo.InvariantCulture);

        [TestCase("b10", 10, BackendType.GPUCompute)]
        [TestCase("b5", 5, BackendType.GPUCompute)]
        public void Convert_ReportsStageTimes(string variant, int blockFrames, BackendType backend)
        {
            if (string.IsNullOrEmpty(WorkFolder))
            {
                Assert.Ignore("VV_VOICE_WORK is not set.");
            }

            string models = Path.Combine(WorkFolder, "sentis");
            string generator = Path.Combine(models, $"gen_{variant}.sentis");
            if (!File.Exists(generator))
            {
                Assert.Ignore($"{generator} is missing.");
            }

            float[] input = ReadWav(Path.Combine(WorkFolder, "data", "source", "000.wav"));
            var shape = new RvcStreamShape(100, blockFrames, 2, 1, HeadGuard, TailGuard, blockFrames + 3 + HeadGuard + TailGuard + 24, 32);
            using var stream = new RvcStream(
                ModelLoader.Load(Path.Combine(models, "hubert_16000.sentis")),
                ModelLoader.Load(Path.Combine(models, "rmvpe_32.sentis")),
                ModelLoader.Load(generator),
                shape,
                backend,
                Semitones);

            int block = stream.BlockSamples;
            int blocks = input.Length / block;
            var output = new float[blocks * block];
            var times = new List<RvcTimings>(blocks);
            for (int i = 0; i < blocks; i++)
            {
                times.Add(stream.Process(new ReadOnlySpan<float>(input, i * block, block), new Span<float>(output, i * block, block)));
            }

            Directory.CreateDirectory(Path.Combine(WorkFolder, "out"));
            WriteWav(Path.Combine(WorkFolder, "out", $"unity_{variant}_{backend}.wav"), output);

            List<RvcTimings> measured = times.Skip(WarmupBlocks).ToList();
            var report = new StringBuilder($"RVC {variant} {backend}: block {block / 48.0:F0} ms, algorithm delay {stream.AlgorithmDelaySamples / 48.0:F1} ms, {measured.Count} blocks\n");
            AppendStage(report, "prepare", measured.Select(t => t.Prepare));
            AppendStage(report, "features", measured.Select(t => t.Features));
            AppendStage(report, "  wait", measured.Select(t => t.FeaturesWait));
            AppendStage(report, "pitch", measured.Select(t => t.Pitch));
            AppendStage(report, "  wait", measured.Select(t => t.PitchWait));
            AppendStage(report, "generator", measured.Select(t => t.Generator));
            AppendStage(report, "  wait", measured.Select(t => t.GeneratorWait));
            AppendStage(report, "finish", measured.Select(t => t.Finish));
            AppendStage(report, "issue", measured.Select(t => t.Issue));
            AppendStage(report, "total", measured.Select(t => t.Total));
            File.AppendAllText(Path.Combine(WorkFolder, "out", "benchmark.txt"), report + "\n");
            Debug.Log(report);
        }

        private static void AppendStage(StringBuilder report, string name, IEnumerable<double> values)
        {
            double[] sorted = values.OrderBy(v => v).ToArray();
            report.AppendLine($"{name,-10} p50 {sorted[sorted.Length / 2],7:F1}  p95 {sorted[sorted.Length * 95 / 100],7:F1}  p99 {sorted[sorted.Length * 99 / 100],7:F1}  max {sorted[^1],7:F1} ms");
        }

        private static float[] ReadWav(string path)
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            reader.ReadBytes(12);
            int rate = 0;
            int bits = 0;
            int format = 0;
            while (true)
            {
                string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
                int size = reader.ReadInt32();
                if (id == "fmt ")
                {
                    format = reader.ReadInt16();
                    reader.ReadInt16();
                    rate = reader.ReadInt32();
                    reader.ReadBytes(6);
                    bits = reader.ReadInt16();
                    reader.ReadBytes(size - 16);
                }
                else if (id == "data")
                {
                    Assert.That(rate, Is.EqualTo(RvcStream.DeviceRate));
                    byte[] data = reader.ReadBytes(size);
                    var samples = new float[size / (bits / 8)];
                    for (int i = 0; i < samples.Length; i++)
                    {
                        samples[i] = format == 3 ? BitConverter.ToSingle(data, i * 4) : BitConverter.ToInt16(data, i * 2) / 32768f;
                    }

                    return samples;
                }
                else
                {
                    reader.ReadBytes(size);
                }
            }
        }

        private static void WriteWav(string path, float[] samples)
        {
            using var writer = new BinaryWriter(File.Create(path));
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + samples.Length * 2);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(RvcStream.DeviceRate);
            writer.Write(RvcStream.DeviceRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(samples.Length * 2);
            foreach (float sample in samples)
            {
                writer.Write((short)(Math.Clamp(sample, -1f, 1f) * 32767f));
            }
        }
    }
}
