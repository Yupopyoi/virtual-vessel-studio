using System;
using System.IO;
using NUnit.Framework;
using VirtualVessel.UI.VoiceLab;
using VirtualVessel.VoiceLab.Activation;
using VirtualVessel.VoiceLab.Scripts;

namespace VirtualVessel.VoiceLab.Tests
{
    /// <summary>Preparing Voice Lab as the screen sees it (Voice Lab detailed design 8.1).</summary>
    public sealed class SetupTests
    {
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "vv-setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_folder, true);
        }

        [Test]
        public void TheGpuAndDriver_AreReadFromNvidiaSmi_AndHeldToWhatCuda128Needs()
        {
            Assert.That(VoiceLabPcProbe.ParseGpu("NVIDIA GeForce RTX 4070, 610.88, 8.9"), Is.EqualTo(("NVIDIA GeForce RTX 4070", "610.88", "8.9")));
            Assert.That(VoiceLabPcProbe.ParseGpu("No devices were found"), Is.Null);
            Assert.That(VoiceLabPcProbe.ParseGpu(null), Is.Null);

            Assert.That(VoiceLabPcProbe.AtLeast("528.33", VoiceLabPcProbe.MinimumDriver), Is.True);
            Assert.That(VoiceLabPcProbe.AtLeast("528.24", VoiceLabPcProbe.MinimumDriver), Is.False);
            Assert.That(VoiceLabPcProbe.AtLeast("12.0", VoiceLabPcProbe.MinimumCapability), Is.True, "The RTX 50 series.");
            Assert.That(VoiceLabPcProbe.AtLeast("3.7", VoiceLabPcProbe.MinimumCapability), Is.False, "Kepler is not in PyTorch's cu128 build.");
            Assert.That(VoiceLabPcProbe.AtLeast("[N/A]", VoiceLabPcProbe.MinimumCapability), Is.False);
        }

        [Test]
        public void TheScreensState_FollowsTheBuild()
        {
            Assert.That(VoiceLabSetupService.StateOf(false, false, false, VoiceActivationState.NotStarted, VoiceLabSetupFailure.None), Is.EqualTo(VoiceLabSetupState.NotStarted));
            Assert.That(VoiceLabSetupService.StateOf(true, true, true, VoiceActivationState.Ready, VoiceLabSetupFailure.None), Is.EqualTo(VoiceLabSetupState.Preparing), "An update running.");
            Assert.That(VoiceLabSetupService.StateOf(false, false, true, VoiceActivationState.Installing, VoiceLabSetupFailure.None), Is.EqualTo(VoiceLabSetupState.Paused));
            Assert.That(VoiceLabSetupService.StateOf(false, false, true, VoiceActivationState.Failed, VoiceLabSetupFailure.None), Is.EqualTo(VoiceLabSetupState.Failed), "Failed before a restart.");
            Assert.That(VoiceLabSetupService.StateOf(false, true, true, VoiceActivationState.Ready, VoiceLabSetupFailure.Network), Is.EqualTo(VoiceLabSetupState.Ready), "A failed update keeps the environment in use.");

            Assert.That(VoiceLabSetupService.StepOf(VoiceActivationPhase.InstallingPackages), Is.EqualTo(VoiceLabSetupStep.Install));
            Assert.That(VoiceLabSetupService.FailureOf(VoiceActivationFailure.Verify), Is.EqualTo(VoiceLabSetupFailure.Check));

            // The record starts a line for another phase or another component, not for each chunk.
            var download = new VoiceLabSetupEntry(System.DateTime.Now, VoiceLabSetupEntryKind.Download, "rvc", 10);
            Assert.That(VoiceLabSetupService.StartsEntry(null, new VoiceActivationProgress(VoiceActivationPhase.Downloading, 0, 9, "rvc", 10)), Is.True);
            Assert.That(VoiceLabSetupService.StartsEntry(download, new VoiceActivationProgress(VoiceActivationPhase.Downloading, 5, 9, "rvc", 10)), Is.False);
            Assert.That(VoiceLabSetupService.StartsEntry(download, new VoiceActivationProgress(VoiceActivationPhase.Downloading, 10, 9, "uv", 3)), Is.True);
            Assert.That(VoiceLabSetupService.StartsEntry(download, new VoiceActivationProgress(VoiceActivationPhase.Placing, 0, 21)), Is.True);
            Assert.That(VoiceLabSetupService.KindOf(VoiceActivationPhase.InstallingPython), Is.EqualTo(VoiceLabSetupEntryKind.Runtime));
            Assert.That(new VoiceLabSetupProgress(VoiceLabSetupStep.Download, 50, 100).Fraction, Is.EqualTo(0.3f).Within(1e-6));
        }

        [Test]
        public void Reloading_SetsVoiceLabUp_FromTheEnvironmentFileWrittenSinceStart()
        {
            VoiceLabService lab = VoiceLabService.Create(_folder, Path.Combine(_folder, "VoiceLab"), Path.Combine(_folder, "VoiceModels"), new FixedClock(), new NullLog());
            Assert.That(lab.Availability, Is.EqualTo(VoiceLabAvailability.NotSetUp));

            File.WriteAllText(Path.Combine(_folder, VoiceLabService.EnvironmentFile), FakeEnvironment().ToJson());
            lab.Reload();

            Assert.That(lab.Availability, Is.EqualTo(VoiceLabAvailability.Ready));
            Assert.That(lab.Shelf, Is.Not.Null);
            Assert.That(lab.EnvironmentState, Is.EqualTo(VoiceLabEnvironmentState.External), "Not built in the folder chosen for activation.");
        }

        [Test]
        public void ADevelopersOwnEnvironment_IsReady_SoNothingIsOfferedToPrepare()
        {
            File.WriteAllText(Path.Combine(_folder, VoiceLabService.EnvironmentFile), FakeEnvironment().ToJson());
            VoiceLabService lab = VoiceLabService.Create(_folder, Path.Combine(_folder, "VoiceLab"), Path.Combine(_folder, "VoiceModels"), new FixedClock(), new NullLog());
            string scripts = Path.GetFullPath(Path.Combine("..", "..", "services", "voice-lab"));
            VoiceLabSetupService setup = VoiceLabSetupService.Create(_folder, scripts, Path.Combine(_folder, "Shared"), Path.Combine(_folder, "Logs"), lab, new FixedClock(), new NullLog());

            Assert.That(setup, Is.Not.Null, "The shipped list and terms are read.");
            Assert.That(setup.State, Is.EqualTo(VoiceLabSetupState.Ready));
            Assert.That(setup.UpdateAvailable, Is.False);
            Assert.That(setup.TermsVersion, Is.EqualTo(VoiceTerms.Load(scripts, "ja").Version));
            Assert.That(VoiceLabSetupService.Create(_folder, _folder, null, null, lab, new FixedClock(), new NullLog()), Is.Null, "No list here.");
        }

        // Every part an environment names, as empty files and folders; the shelf is the real one.
        private VoiceLabEnvironment FakeEnvironment()
        {
            string root = Path.Combine(_folder, "env");
            string scripts = Path.Combine(root, "scripts");
            Directory.CreateDirectory(Path.Combine(scripts, "voicelab"));
            File.Copy(Path.GetFullPath(Path.Combine("..", "..", "services", "voice-lab", "voicelab", "shelf.json")), Path.Combine(scripts, "voicelab", "shelf.json"));
            string python = Path.Combine(root, "venv", "Scripts", "python.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(python));
            File.WriteAllText(python, string.Empty);
            Directory.CreateDirectory(Path.Combine(root, "rvc"));
            Directory.CreateDirectory(Path.Combine(root, "voxcpm2"));
            File.WriteAllText(Path.Combine(root, "t.sentis"), string.Empty);
            File.WriteAllText(Path.Combine(root, "t.json"), "{}");
            return new VoiceLabEnvironment(python, scripts, Path.Combine(root, "rvc"), Path.Combine(root, "voxcpm2"), Path.Combine(root, "t.sentis"), Path.Combine(root, "t.json"));
        }
    }
}
