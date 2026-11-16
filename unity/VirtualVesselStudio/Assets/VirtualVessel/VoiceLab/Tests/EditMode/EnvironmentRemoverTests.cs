using System;
using System.IO;
using NUnit.Framework;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.VoiceLab.Scripts;

namespace VirtualVessel.VoiceLab.Tests
{
    /// <summary>Deleting the training environment the app built, and only that (Voice Lab detailed design 11).</summary>
    public sealed class EnvironmentRemoverTests
    {
        private const string Root = @"C:\Users\u\AppData\Local\VirtualVesselStudio\External\VoiceLab";

        [Test]
        public void AnEnvironmentWhollyInOneVersionFolder_IsTheApps()
        {
            VoiceLabEnvironment inside = Environment(Root + @"\1.0");

            Assert.That(VoiceLabEnvironmentRemover.Owned(inside, Root), Is.EqualTo(Root + @"\1.0"));
            var appScripts = new VoiceLabEnvironment(Root + @"\1.0\venv\Scripts\python.exe", @"C:\Program Files\VVS\VoiceLab\scripts", Root + @"\1.0\rvc", Root + @"\1.0\voxcpm2", Root + @"\1.0\t.sentis", Root + @"\1.0\t.json");
            Assert.That(VoiceLabEnvironmentRemover.Owned(appScripts, Root), Is.EqualTo(Root + @"\1.0"), "The scripts come with the app, wherever it is.");
        }

        [Test]
        public void AnEnvironmentElsewhere_OrAcrossVersions_IsNot()
        {
            Assert.That(VoiceLabEnvironmentRemover.Owned(Environment(@"D:\work\voice-work"), Root), Is.Null, "A developer's environment.");
            var mixed = new VoiceLabEnvironment(Root + @"\1.0\python\python.exe", Root + @"\1.0\scripts", Root + @"\2.0\rvc", Root + @"\1.0\voxcpm", Root + @"\1.0\t.onnx", Root + @"\1.0\m.json");
            Assert.That(VoiceLabEnvironmentRemover.Owned(mixed, Root), Is.Null, "Parts of two versions.");
            var partly = new VoiceLabEnvironment(Root + @"\1.0\python\python.exe", Root + @"\1.0\scripts", @"E:\rvc", Root + @"\1.0\voxcpm", Root + @"\1.0\t.onnx", Root + @"\1.0\m.json");
            Assert.That(VoiceLabEnvironmentRemover.Owned(partly, Root), Is.Null, "A part outside.");
            Assert.That(VoiceLabEnvironmentRemover.Owned(Environment(Root + "Other"), Root), Is.Null, "A folder whose name only starts the same.");
        }

        [Test]
        public void Removing_DeletesTheFolderAndItsSettingsFile_AndRefusesWhatIsNotTheApps()
        {
            string folder = Path.Combine(Path.GetTempPath(), "vv-env-" + Guid.NewGuid().ToString("N"));
            string version = Path.Combine(folder, "1.0");
            Directory.CreateDirectory(Path.Combine(version, "python"));
            File.WriteAllBytes(Path.Combine(version, "python", "python.exe"), new byte[100]);
            string settings = Path.Combine(folder, "voice-lab-environment.json");
            File.WriteAllText(settings, "{}");
            try
            {
                var remover = new VoiceLabEnvironmentRemover(settings, version);
                Assert.That(remover.SizeAsync(default).Result, Is.EqualTo(100));

                Assert.That(remover.RemoveAsync().Result.Ok, Is.True);

                Assert.That(Directory.Exists(version) || File.Exists(settings), Is.False);
                Assert.That(new VoiceLabEnvironmentRemover(settings, null).RemoveAsync().Result.Error, Is.EqualTo(CrudError.Invalid));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static VoiceLabEnvironment Environment(string folder)
        {
            return new VoiceLabEnvironment(folder + @"\python\python.exe", folder + @"\scripts", folder + @"\rvc", folder + @"\voxcpm", folder + @"\template.onnx", folder + @"\manifest.json");
        }
    }
}
