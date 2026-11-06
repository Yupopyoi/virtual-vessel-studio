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
            Assert.That(() => lab.MakeCandidatesAsync(new VoiceLabMood(null, ""), 6, null, CancellationToken.None).GetAwaiter().GetResult(), Throws.TypeOf<VoiceLabFailedException>());
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
            Assert.That(VoiceLabService.ToMaking(new Scripts.ScriptProgress("generate.py", "load", 0, 1)).Loading, Is.True);
            VoiceLabMaking making = VoiceLabService.ToMaking(new Scripts.ScriptProgress("generate.py", "candidates", 2, 6));
            Assert.That((making.Done, making.Total), Is.EqualTo((2, 6)));
        }

        [Test]
        public void Timbre_IsClampedOnTheWayIn()
        {
            Assert.That(VoiceLabService.ToSettings(new VoiceLabTimbre(9, -2, 0, 1)).Pitch, Is.EqualTo(6f));
            Assert.That(VoiceLabService.ToSettings(new VoiceLabTimbre(0, 0, 0, 0, 4)).Softness, Is.EqualTo(4f));
        }

        private VoiceLabService Create()
        {
            return VoiceLabService.Create(_folder, Path.Combine(_folder, "VoiceLab"), Path.Combine(_folder, "VoiceModels"), new FixedClock(), new NullLog());
        }
    }
}
