using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.VoiceLab;
using VirtualVessel.VoiceLab.Design;
using VirtualVessel.VoiceLab.Training;

namespace VirtualVessel.VoiceLab.Tests
{
    /// <summary>STEP 1 and STEP 2 left unfinished, kept in VoiceLab/work.json (Voice Lab detailed design 6.6).</summary>
    public sealed class WorkTests
    {
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "vv-voicelab-work-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_folder, true);
        }

        [Test]
        public void TheWork_IsKeptReadAndForgotten()
        {
            var store = new WorkStore(_folder, new FixedClock());
            Assert.That(store.Load().Error, Is.EqualTo(CrudError.NotFound), "None kept yet.");

            WorkFile work = Work(@"C:\d\candidate_7.wav");
            work.Held.Add(new TimbreValues { Pitch = 2, Softness = 3 });
            Assert.That(store.Save(work).Ok, Is.True);

            WorkFile read = store.Load().Value;
            Assert.That((read.Step, read.Picked, read.FirstSeed, read.Line), Is.EqualTo((1, 2, 506, "おはよう。")));
            Assert.That(read.Words, Is.EqualTo(new[] { "male", "shy" }));
            Assert.That(read.Candidates.Select(c => (c.Number, c.From, c.Seed)), Is.EqualTo(new[] { (1, 0, 500), (2, 1, 501) }));
            Assert.That(read.Candidates[1].Reference, Is.EqualTo(@"C:\d\candidate_7.wav"));
            Assert.That((read.Timbre.Pitch, read.Held.Single().Softness), Is.EqualTo((4f, 3f)));

            Assert.That(store.Delete().Ok, Is.True);
            Assert.That(store.Load().Error, Is.EqualTo(CrudError.NotFound));
            Assert.That(store.Delete().Ok, Is.True, "Nothing to delete is fine.");
        }

        [Test]
        public void ADamagedFile_IsMovedAside_AndReportedInvalid()
        {
            var store = new WorkStore(_folder, new FixedClock());
            File.WriteAllText(store.FilePath, "{ not json");

            Assert.That(store.Load().Error, Is.EqualTo(CrudError.Invalid));
            Assert.That(File.Exists(store.FilePath), Is.False, "Moved aside, so it is not read again.");
        }

        [Test]
        public void ACandidate_ComesBackWithWhatMadeIt_UnderANewId()
        {
            var made = new VoiceCandidate("candidate_3_501", 501, @"C:\d\a.wav", new PitchSummary(268, 203, 338), "calm", @"C:\d\r.wav", new VoiceDescription(new[] { "male" }, "husky", null, "おはよう。"));

            VoiceCandidate back = WorkStore.FromFile(WorkStore.ToFile(4, 2, made));

            Assert.That((back.Seed, back.File, back.Pitch.MedianHz, back.Instruction, back.Reference), Is.EqualTo((501, @"C:\d\a.wav", 268, "calm", @"C:\d\r.wav")));
            Assert.That((back.Description.Words.Single(), back.Description.FreeWords, back.Description.Line), Is.EqualTo(("male", "husky", "おはよう。")));
            Assert.That(back.Id, Is.EqualTo("work_4_501"), "Never one this session's candidates have.");
        }

        [Test]
        public void WithoutItsPickedCandidate_TheWorkGoesBackToStep1()
        {
            WorkFile work = Work(null);
            var left = new[] { new VoiceLabWorkCandidate(1, 0, new VoiceLabCandidate("work_1_500", "a.wav", 260, 200, 300, "x", 500)) };

            VoiceLabWork shown = VoiceLabService.ToScreen(work, left);

            Assert.That((shown.Picked, shown.Step), Is.EqualTo((0, 0)), "Candidate 2, the one picked, is gone.");
            Assert.That(shown.Request.FirstSeed, Is.EqualTo(506));

            work.Step = 7;
            var both = left.Append(new VoiceLabWorkCandidate(2, 1, new VoiceLabCandidate("work_2_501", "b.wav", 260, 200, 300, "x", 501))).ToArray();
            Assert.That(VoiceLabService.ToScreen(work, both).Step, Is.EqualTo(1), "A step this version does not know is STEP 2 at most.");
        }

        [Test]
        public void TheFile_KeepsOnlyAPickThatIsAmongItsCandidates()
        {
            var work = new VoiceLabWork(1, new VoiceLabRequest(null, ""), Array.Empty<VoiceLabWorkCandidate>(), 3, default, null);

            WorkFile file = VoiceLabService.ToFile(work, new List<WorkCandidate>(), DateTimeOffset.UnixEpoch);

            Assert.That(file.Picked, Is.EqualTo(0));
        }

        [Test]
        public void WithoutAnEnvironment_TheWorkIsNotReadOrKept()
        {
            VoiceLabService lab = VoiceLabService.Create(Path.Combine(_folder, "Config"), _folder, Path.Combine(_folder, "Voices"), new FixedClock(), new NullLog());

            Assert.That(lab.LoadWork().Error, Is.EqualTo(CrudError.Invalid));
            Assert.That(lab.SaveWork(new VoiceLabWork(0, null, null, 0, default, null)).Error, Is.EqualTo(CrudError.Invalid));
            Assert.That(lab.DeleteWork().Ok, Is.True);
        }

        [Test]
        public void OldCandidates_KeepTheWorksAudio()
        {
            string kept = Write("Designs/old/candidates/001/candidate_500.wav");
            string near = Write("Designs/old/candidates/001/candidate_400.wav");
            string other = Write("Designs/old/candidates/001/candidate_9.wav");
            var clock = new FixedClock();
            WorkFile work = Work(near);
            work.Candidates[0].File = kept;
            Assert.That(new WorkStore(_folder, clock).Save(work).Ok, Is.True);
            var runs = new TrainingRunStore(_folder, clock, new Random(1));
            var storage = new VoiceLabStorage(_folder, runs, () => new VoiceLabActivity(false, Path.Combine(_folder, "Designs", "now")), () => WorkStore.KeptFiles(_folder, clock));

            Assert.That(storage.DeleteOldCandidates().Ok, Is.True);

            Assert.That(File.Exists(kept) && File.Exists(near), Is.True);
            Assert.That(File.Exists(other), Is.False);
        }

        private string Write(string relative)
        {
            string path = Path.Combine(_folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, new byte[10]);
            return path;
        }

        private static WorkFile Work(string reference)
        {
            return new WorkFile
            {
                Step = 1,
                Words = new List<string> { "male", "shy" },
                FreeWords = "husky",
                Line = "おはよう。",
                FirstSeed = 506,
                Picked = 2,
                Timbre = new TimbreValues { Pitch = 4 },
                Candidates = new List<WorkCandidate>
                {
                    new WorkCandidate { Number = 1, Seed = 500, File = @"C:\d\a.wav", MedianHz = 260 },
                    new WorkCandidate { Number = 2, From = 1, Seed = 501, File = @"C:\d\b.wav", Reference = reference },
                },
            };
        }
    }
}
