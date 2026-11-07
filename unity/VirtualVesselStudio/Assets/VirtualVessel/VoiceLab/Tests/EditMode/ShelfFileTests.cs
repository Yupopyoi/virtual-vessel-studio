using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.VoiceLab;
using VirtualVessel.VoiceLab.Design;

namespace VirtualVessel.VoiceLab.Tests
{
    /// <summary>STEP 1's shelf as the scripts define it (services/voice-lab/voicelab/shelf.json).</summary>
    public sealed class ShelfFileTests
    {
        private static string RepositoryShelf => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "services", "voice-lab", "voicelab", "shelf.json"));

        private static VoiceLabShelf Load()
        {
            VoiceLabShelf shelf = ShelfFile.Parse(File.ReadAllText(RepositoryShelf, Encoding.UTF8), out string problem);
            Assert.That(problem, Is.Null);
            return shelf;
        }

        [Test]
        public void TheScriptsShelf_GroupsItsWords_AndStartsWithTheMainGroups()
        {
            VoiceLabShelf shelf = Load();
            Assert.That(shelf.Groups.Where(g => g.Main).Select(g => g.Id), Is.EqualTo(new[] { "voice", "person", "pitch", "energy" }));
            Assert.That(shelf.GroupOf("airy").Id, Is.EqualTo("quality"));
            Assert.That(shelf.GroupOf("airy").PickOne, Is.False, "Resonance and breath share a group.");
            Assert.That(shelf.Start(), Is.EquivalentTo(new[] { "female", "p20", "light", "attentive", "friendly" }));
            Assert.That(shelf.Word("soft").IsDefault, Is.True);
            Assert.That(shelf.Examples.Select(e => e.Id), Has.Member("low-calm-male"));
        }

        [Test]
        public void EveryGroupWordAndExample_HasItsTextsInEveryTable()
        {
            // Built from the shelf's ids rather than declared, so the key test of the string tables does not see them.
            VoiceLabShelf shelf = Load();
            string folder = Path.Combine(Application.dataPath, "VirtualVessel", "UI", "Strings");
            foreach (string file in Directory.GetFiles(folder, "*.json"))
            {
                StringTable table = StringTable.Parse(File.ReadAllText(file, Encoding.UTF8));
                foreach (VoiceLabShelfGroup group in shelf.Groups)
                {
                    Assert.That(table.Strings.Keys, Does.Contain(group.NameKey), file);
                }

                foreach (VoiceLabShelfWord word in shelf.Words)
                {
                    Assert.That(table.Strings.Keys, Does.Contain(word.LabelKey), file);
                    Assert.That(table.Strings.Keys, Does.Contain(word.PhraseKey), file);
                }

                foreach (VoiceLabExample example in shelf.Examples)
                {
                    Assert.That(table.Strings.Keys, Does.Contain(example.NameKey), file);
                }
            }
        }

        [TestCase("{\"schemaVersion\":2}")]
        [TestCase("not json")]
        [TestCase("{\"schemaVersion\":1,\"groups\":[{\"id\":\"g\"}],\"slots\":[],\"words\":[{\"id\":\"w\",\"slot\":\"missing\"}]}")]
        public void AShelfThisVersionCannotRead_IsRefusedWithAReason(string json)
        {
            Assert.That(ShelfFile.Parse(json, out string problem), Is.Null);
            Assert.That(problem, Is.Not.Empty);
        }
    }
}
