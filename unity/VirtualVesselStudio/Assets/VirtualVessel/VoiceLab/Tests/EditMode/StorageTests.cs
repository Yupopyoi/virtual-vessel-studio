using System;
using System.IO;
using NUnit.Framework;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.VoiceLab.Training;

namespace VirtualVessel.VoiceLab.Tests
{
    /// <summary>Deleting the samples and earlier candidates to free space (Voice Lab detailed design 8.6).</summary>
    public sealed class StorageTests
    {
        private string _folder;
        private TrainingRunStore _runs;
        private VoiceLabActivity _activity;
        private VoiceLabStorage _storage;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "vv-voicelab-storage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            _runs = new TrainingRunStore(_folder, new FixedClock(), new Random(1));
            _activity = new VoiceLabActivity(false, Path.Combine(_folder, "Designs", "now"));
            _storage = new VoiceLabStorage(_folder, _runs, () => _activity);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_folder, true);
        }

        [Test]
        public void Samples_AreMeasuredAndDeleted_ButNotWhileBeingMade()
        {
            Write("Samples/ja/base.wav", 3000);
            Write("Samples/ja/word_male.wav", 1000);

            Assert.That(_storage.Use().Value.SamplesBytes, Is.EqualTo(4000));

            _activity = new VoiceLabActivity(true, _activity.Session);
            Assert.That(_storage.DeleteSamples().Error, Is.EqualTo(CrudError.Busy));
            _activity = new VoiceLabActivity(false, _activity.Session);

            Assert.That(_storage.DeleteSamples().Ok, Is.True);
            Assert.That(Directory.Exists(Path.Combine(_folder, "Samples")), Is.False);
            Assert.That(_storage.Use().Value.SamplesBytes, Is.EqualTo(0));
            Assert.That(_storage.DeleteSamples().Ok, Is.True, "Nothing to delete is fine.");
        }

        [Test]
        public void OldCandidates_GoButThoseARunUses_AndThisSessions()
        {
            string used = Write("Designs/old/candidates/candidate_7.wav", 100);
            string reference = Write("Designs/old/candidates/candidate_3.wav", 10);
            Write("Designs/old/candidates/candidate_8.wav", 200);
            Write("Designs/older/previews/p.wav", 300);
            string current = Write("Designs/now/candidates/candidate_1.wav", 400);
            Assert.That(_runs.Create(new RunDesign { Candidate = used, Reference = reference }).Ok, Is.True);

            Assert.That(_storage.Use().Value.CandidatesBytes, Is.EqualTo(500), "Only what can go is counted.");

            Assert.That(_storage.DeleteOldCandidates().Ok, Is.True);

            Assert.That(File.Exists(used) && File.Exists(reference) && File.Exists(current), Is.True);
            Assert.That(File.Exists(Path.Combine(_folder, "Designs", "old", "candidates", "candidate_8.wav")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(_folder, "Designs", "older")), Is.False, "A folder left empty goes too.");
            Assert.That(_storage.Use().Value.CandidatesBytes, Is.EqualTo(0));
        }

        [Test]
        public void Deletable_ComparesWholePathsWithoutCase()
        {
            string session = @"C:\Data\Designs\now";

            var files = new[] { @"C:\Data\Designs\old\a.wav", @"C:\Data\Designs\old\b.wav", @"C:\Data\Designs\now\c.wav", @"C:\Data\Designs\nowhere\d.wav" };

            Assert.That(VoiceLabStorage.Deletable(files, new[] { @"c:\data\designs\OLD\a.wav", null }, session), Is.EqualTo(new[] { @"C:\Data\Designs\old\b.wav", @"C:\Data\Designs\nowhere\d.wav" }));
        }

        private string Write(string relative, int bytes)
        {
            string path = Path.Combine(_folder, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, new byte[bytes]);
            return path;
        }
    }
}
