using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualVessel.VoiceLab.Activation;

namespace VirtualVessel.VoiceLab.Tests
{
    /// <summary>The list of what activation downloads (Voice Lab detailed design 6.2).</summary>
    public sealed class ComponentManifestTests
    {
        private const string Sha = "4e1278ede866be6c0bf32d2f466cc6de7a9fb399ecf20c9ce2d186e52424be47";

        [Test]
        public void TheBundledList_IsValid_AndPutsTheSharedModelsApart()
        {
            // Unity runs tests from the project folder; the scripts are two folders up, in services/voice-lab.
            string path = Path.GetFullPath(Path.Combine("..", "..", "services", "voice-lab", VoiceComponentManifest.FileName));

            VoiceComponentManifest manifest = VoiceComponentManifest.Parse(File.ReadAllText(path), out IReadOnlyList<string> problems);

            Assert.That(problems, Is.Empty);
            Assert.That(manifest.Components.Single(c => c.Id == "uv").Extract, Is.True);
            Assert.That(manifest.Components.Where(c => c.Place == VoiceComponentPlace.SharedModels).Select(c => c.Target), Is.EquivalentTo(new[] { "hubert_16000_anchored.sentis", "rmvpe_32.sentis" }), "What streaming uses stays when the environment goes.");
            Assert.That(manifest.TotalSize, Is.GreaterThan(5L * 1024 * 1024 * 1024));
        }

        [Test]
        public void AListThatCouldWriteOutsideItsFolder_OrSkipItsHash_IsRefused()
        {
            string json = List(Component("a", target: "../evil.dll"), Component("b", sha: "ABC"), Component("c", url: "http://plain"), Component("d", place: "desktop"));

            Assert.That(VoiceComponentManifest.Parse(json, out IReadOnlyList<string> problems), Is.Null);
            Assert.That(problems.Count, Is.EqualTo(4));
        }

        [Test]
        public void ANewerSchema_OrTwoComponentsWithOneId_AreRefused()
        {
            Assert.That(VoiceComponentManifest.Parse("{\"schemaVersion\":2}", out _), Is.Null);
            Assert.That(VoiceComponentManifest.Parse(List(Component("a"), Component("a")), out IReadOnlyList<string> problems), Is.Null);
            Assert.That(problems.Single(), Does.Contain("already used"));
        }

        [Test]
        public void Paths_StayInside()
        {
            Assert.That(VoiceComponentManifest.IsRelativeInside("rvc/assets/rmvpe/rmvpe.pt"), Is.True);
            Assert.That(VoiceComponentManifest.IsRelativeInside(@"C:\Windows"), Is.False);
            Assert.That(VoiceComponentManifest.IsRelativeInside("a/../../b"), Is.False);
            Assert.That(VoiceComponentManifest.IsRelativeInside("/etc"), Is.False);
            Assert.That(VoiceComponentManifest.IsRelativeInside("a//b"), Is.False);
            Assert.That(VoiceComponentManifest.IsRelativeInside("C:x"), Is.False);
        }

        private static string List(params string[] components)
        {
            return "{\"schemaVersion\":1,\"version\":\"1\",\"python\":\"3.12.2\",\"lock\":\"uv.lock\",\"components\":[" + string.Join(",", components) + "]}";
        }

        private static string Component(string id, string url = "https://example.com/f.zip", string sha = Sha, string place = "environment", string target = "f")
        {
            return $"{{\"id\":\"{id}\",\"url\":\"{url}\",\"sha256\":\"{sha}\",\"size\":1,\"license\":\"MIT\",\"place\":\"{place}\",\"target\":\"{target}\"}}";
        }
    }
}
