using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using VirtualVessel.UI.VoiceLab;
using VirtualVessel.VoiceLab.Training;

namespace VirtualVessel.VoiceLab.Tests
{
    public sealed class VoiceLabServiceTests
    {
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "vv-voicelab-service-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_folder, true);
        }

        [Test]
        public void WithoutAnEnvironment_ItIsNotSetUp_AndRefusesWork()
        {
            VoiceLabService lab = Create();

            Assert.That(lab.Availability, Is.EqualTo(VoiceLabAvailability.NotSetUp));
            Assert.That(lab.BuildProgress.State, Is.EqualTo(VoiceLabBuildState.None));
            Assert.Throws<VoiceLabFailedException>(() => lab.Register("r", "x"));
            Assert.That(() => lab.MakeCandidatesAsync(new VoiceLabRequest(null, ""), 6, null, CancellationToken.None).GetAwaiter().GetResult(), Throws.TypeOf<VoiceLabFailedException>());
            Assert.That(lab.Shelf, Is.Null);
            Assert.That(lab.SampleFile("ja", "base.wav"), Is.Null);
            Assert.That(() => lab.MakeSamplesAsync(CancellationToken.None).GetAwaiter().GetResult(), Throws.TypeOf<VoiceLabFailedException>());
        }

        [TestCase("base.wav", true)]
        [TestCase("ja", true)]
        [TestCase("..", false)]
        [TestCase("../x.wav", false)]
        [TestCase("", false)]
        public void Samples_AreOnlyReadByPlainNames(string name, bool plain)
        {
            Assert.That(VoiceLabService.IsPlainName(name), Is.EqualTo(plain));
        }

        [Test]
        public void AnEnvironmentPointingAtNothing_IsNotSetUp()
        {
            File.WriteAllText(Path.Combine(_folder, VoiceLabService.EnvironmentFile), "{\"python\":\"C:\\\\nowhere\\\\python.exe\"}");

            Assert.That(Create().Availability, Is.EqualTo(VoiceLabAvailability.NotSetUp));
        }

        [TestCase(TrainingState.Pending, VoiceLabBuildState.Preparing)]
        [TestCase(TrainingState.Training, VoiceLabBuildState.Training)]
        [TestCase(TrainingState.Completed, VoiceLabBuildState.Completed)]
        [TestCase(TrainingState.Cancelled, VoiceLabBuildState.Cancelled)]
        [TestCase(TrainingState.Failed, VoiceLabBuildState.Failed)]
        public void States_MapToTheScreens(TrainingState state, VoiceLabBuildState shown)
        {
            Assert.That(VoiceLabService.ToScreen(state), Is.EqualTo(shown));
        }

        [Test]
        public void CandidatesProgress_CountsOnlyOnceTheModelHasLoaded()
        {
            Assert.That(VoiceLabService.ToMaking(new Scripts.ScriptProgress("generate.py", "load", 0, 1), "candidates").Loading, Is.True);
            Assert.That(VoiceLabService.ToMaking(new Scripts.ScriptProgress("generate.py", "samples", 3, 27), "samples").Done, Is.EqualTo(3));
            VoiceLabMaking making = VoiceLabService.ToMaking(new Scripts.ScriptProgress("generate.py", "candidates", 2, 6), "candidates");
            Assert.That((making.Done, making.Total), Is.EqualTo((2, 6)));
        }

        [Test]
        public void Timbre_IsClampedOnTheWayIn()
        {
            Assert.That(VoiceLabService.ToSettings(new VoiceLabTimbre(9, -2, 0, 1)).Pitch, Is.EqualTo(6f));
            Assert.That(VoiceLabService.ToSettings(new VoiceLabTimbre(0, 0, 0, 0, 4)).Softness, Is.EqualTo(4f));
        }

        [Test]
        public void StoppedScripts_AreCancels_NotFailures()
        {
            // A preview stopped for a newer one showed "could not make it" while the newer one was still being made.
            Assert.That(VoiceLabService.ToScreen(new Scripts.ScriptFailedException("cancelled", "stopped")), Is.InstanceOf<OperationCanceledException>());
            Assert.That(VoiceLabService.ToScreen(new Scripts.ScriptFailedException("failed", "broken")), Is.TypeOf<VoiceLabFailedException>());
        }

        private VoiceLabService Create()
        {
            return VoiceLabService.Create(_folder, Path.Combine(_folder, "VoiceLab"), Path.Combine(_folder, "VoiceModels"), new FixedClock(), new NullLog());
        }
    }
}
