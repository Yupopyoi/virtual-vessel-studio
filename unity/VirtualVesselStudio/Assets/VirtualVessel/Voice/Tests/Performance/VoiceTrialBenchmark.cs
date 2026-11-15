using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using VirtualVessel.Core.Time;
using VirtualVessel.UI.Voice;
using VirtualVessel.Voice.Models;

namespace VirtualVessel.Voice.Tests
{
    /// <summary>
    /// A trial with the real models (Voice detailed design 8.5): a recording converted with a registered voice's folder,
    /// the user's pitch measured first, the frames kept short. Voices come from VV_VOICE_MODELS.
    /// </summary>
    public sealed class VoiceTrialBenchmark
    {
        [UnityTest]
        public IEnumerator ConvertsARecording_WithAVoiceFolder()
        {
            string work = Environment.GetEnvironmentVariable("VV_VOICE_WORK");
            string voicesRoot = Environment.GetEnvironmentVariable("VV_VOICE_MODELS");
            if (string.IsNullOrEmpty(work) || string.IsNullOrEmpty(voicesRoot))
            {
                Assert.Ignore("VV_VOICE_WORK or VV_VOICE_MODELS is not set.");
            }

            var store = new VoiceModelStore(voicesRoot, new UtcSystemClock());
            var library = new VoiceModelLibrary(voicesRoot, Path.Combine(work, "sentis"), store);
            VoiceConversionVoice voice = library.List().Value.FirstOrDefault(v => v.Id != VoiceModelLibrary.PrototypeId && v.Usable && v.MedianPitchHz > 0);
            if (voice.Id == null)
            {
                Assert.Ignore("No usable registered voice with a known pitch.");
            }

            string recording = Environment.GetEnvironmentVariable("VV_VOICE_INPUT") ?? Path.Combine(work, "rec", "record_speak2.wav");
            var loop = new VoiceServiceRealtimeBenchmark.ManualFrameLoop();
            var trial = new VoiceTrialService(loop, library, null, Path.Combine(work, "out", "trials"));
            Stopwatch clock = null;
            Task<VoiceTrialResult> converting = null;
            double longest = 0;
            // Twice in one process: the first run in a process compiles the GPU's shaders, which later runs reuse.
            double firstLongest = 0;
            for (int run = 0; run < 2; run++)
            {
                firstLongest = longest;
                longest = 0;
                clock = Stopwatch.StartNew();
                converting = trial.ConvertAsync(new VoiceTrialRequest(store.FolderOf(voice.Id), recording, true, 0f, 0f), null, CancellationToken.None);
                while (!converting.IsCompleted)
                {
                    var frame = Stopwatch.StartNew();
                    loop.Tick();
                    longest = Math.Max(longest, frame.Elapsed.TotalMilliseconds);
                    yield return null;
                }
            }

            VoiceTrialResult result = converting.Result;
            float[] converted = WavFile.Read(File.ReadAllBytes(result.Converted), out int rate);
            float[] input = WavFile.Read(File.ReadAllBytes(recording), out int inputRate);
            string report = $"Trial with {voice.Id}: {clock.Elapsed.TotalSeconds:F1} s for {input.Length / (double)inputRate:F1} s, user {result.UserHz:F0} Hz, voice {result.VoiceHz:F0} Hz, shift {result.Semitones:+0;-0;0}, longest frame {longest:F0} ms (first run {firstLongest:F0} ms)";
            TestContext.WriteLine(report);
            File.AppendAllText(Path.Combine(work, "out", "benchmark.txt"), report + "\n");
            Assert.That(rate, Is.EqualTo(48000));
            Assert.That(converted.Length / (double)rate, Is.EqualTo(input.Length / (double)inputRate + 1.0).Within(0.2), "The recording and the second of silence after it.");
            Assert.That(result.UserHz, Is.InRange(80f, 500f));
            Assert.That(result.Semitones, Is.EqualTo(PitchMatch.Semitones(result.VoiceHz, result.UserHz)));
        }
    }
}
