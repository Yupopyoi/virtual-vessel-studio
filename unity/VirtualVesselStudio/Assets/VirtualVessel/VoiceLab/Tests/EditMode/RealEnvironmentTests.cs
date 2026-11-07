using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;
using VirtualVessel.Core.Time;
using VirtualVessel.ExternalServices.Process;
using VirtualVessel.VoiceLab.Design;
using VirtualVessel.VoiceLab.Registration;
using VirtualVessel.VoiceLab.Scripts;
using VirtualVessel.VoiceLab.Training;

namespace VirtualVessel.VoiceLab.Tests
{
    /// <summary>
    /// The real scripts in a developer's environment (Voice Lab detailed design 14): VV_VOICE_LAB_ENV names its
    /// JSON file, and without it these are ignored. Building a whole voice takes about 20 minutes on an RTX 4070,
    /// so it runs only with VV_VOICE_LAB_BUILD=1 as well. Outputs stay in VV_VOICE_LAB_OUT, or a temporary folder.
    /// </summary>
    public sealed class RealEnvironmentTests
    {
        private VoiceLabEnvironment _environment;
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            string path = Environment.GetEnvironmentVariable("VV_VOICE_LAB_ENV");
            if (string.IsNullOrEmpty(path))
            {
                Assert.Ignore("VV_VOICE_LAB_ENV is not set.");
            }

            _environment = VoiceLabEnvironment.Parse(File.ReadAllText(path), out string problem);
            Assert.That(problem, Is.Null);
            IReadOnlyList<string> problems = _environment.Problems(File.Exists, Directory.Exists);
            Assert.That(problems, Is.Empty, string.Join("; ", problems));
            _folder = Environment.GetEnvironmentVariable("VV_VOICE_LAB_OUT") ?? Path.Combine(Path.GetTempPath(), "vv-voicelab-real");
            Directory.CreateDirectory(_folder);
        }

        [Test]
        public void Candidates_AndATimbrePreview_ComeFromTheRealScripts()
        {
            VoiceDesigner designer = Designer(out _, out _);

            IReadOnlyList<VoiceCandidate> candidates = designer.MakeCandidatesAsync(Airy(), 1, null, null, CancellationToken.None).Result;
            TimbrePreview preview = designer.PreviewTimbreAsync(candidates[0], new TimbreSettings(4, 4, 0, 0), CancellationToken.None).Result;

            Assert.That(candidates, Has.Count.EqualTo(1));
            Assert.That(File.Exists(candidates[0].File), Is.True);
            Assert.That(candidates[0].Pitch.MedianHz, Is.InRange(120, 700));
            Assert.That(File.Exists(preview.File), Is.True);
            // +4 semitones raise the median by about 26%.
            Assert.That(preview.Pitch.MedianHz / (float)candidates[0].Pitch.MedianHz, Is.InRange(1.15f, 1.40f));
        }

        [Test]
        [Timeout(60 * 60 * 1000)] // Unity's default of 3 minutes is far too short for a whole build.
        public void Build_AndRegister_AWholeVoice()
        {
            if (Environment.GetEnvironmentVariable("VV_VOICE_LAB_BUILD") != "1")
            {
                Assert.Ignore("VV_VOICE_LAB_BUILD is not 1.");
            }

            VoiceDesigner designer = Designer(out TrainingRunStore runs, out ISystemClock clock);
            VoiceCandidate candidate = designer.MakeCandidatesAsync(Airy(), 1, null, null, CancellationToken.None).Result[0];

            TrainingRun run = designer.BuildVoiceAsync(candidate, new TimbreSettings(4, 4, 0, 0), null, CancellationToken.None).Result;
            Assert.That(run.GetState(), Is.EqualTo(TrainingState.Completed), run.Error);
            RegisteredVoice voice = new VoiceModelRegistrar(new VoiceModelStore(Path.Combine(_folder, "VoiceModels"), clock), runs, clock).Register(run, "Real environment test");

            Assert.That(File.Exists(Path.Combine(voice.Folder, "voice.sentis")), Is.True);
            Assert.That(voice.MedianPitchHz, Is.InRange(150, 700));
            TestContext.WriteLine($"Registered {voice.Id} at {voice.Folder}, median {voice.MedianPitchHz} Hz");
        }

        private VoiceDesigner Designer(out TrainingRunStore runs, out ISystemClock clock)
        {
            clock = new UtcSystemClock();
            var runner = new ExternalProcessRunner(_environment.AllowedFolders, new NullLog(), clock);
            runs = new TrainingRunStore(Path.Combine(_folder, "VoiceLab"), clock);
            return new VoiceDesigner(new PythonScripts(_environment, runner), runs, Path.Combine(_folder, "VoiceLab", "Designs"), clock, new NullLog(), 101);
        }

        // The experiment's airy_soft (decision record, 2026-11-03).
        private static VoiceDescription Airy()
        {
            return new VoiceDescription(new[] { "airy", "gentle" }, "");
        }
    }
}
