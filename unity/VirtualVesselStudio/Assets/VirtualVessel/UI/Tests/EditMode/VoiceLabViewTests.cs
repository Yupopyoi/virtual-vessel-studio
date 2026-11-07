using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualVessel.UI.Streaming;
using VirtualVessel.UI.VoiceLab;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The Voice Lab screen's decisions (system design 14.13 to 14.18); the screen itself is in the PlayMode tests.</summary>
    public sealed class VoiceLabViewTests
    {
        private static VoiceLabShelf Shelf()
        {
            return new VoiceLabShelf(
                new[]
                {
                    new VoiceLabShelfGroup("voice", true, new[] { new VoiceLabShelfWord("female", "voice", true), new VoiceLabShelfWord("male", "voice", false) }),
                    new VoiceLabShelfGroup("pitch", true, new[] { new VoiceLabShelfWord("deep", "pitch", false), new VoiceLabShelfWord("light", "pitch", true) }),
                    new VoiceLabShelfGroup("quality", false, new[]
                    {
                        new VoiceLabShelfWord("soft", "resonance", true), new VoiceLabShelfWord("focused", "resonance", false),
                        new VoiceLabShelfWord("airy", "breath", false),
                    }),
                },
                new[] { new VoiceLabExample("low-male", new[] { "male", "deep", "focused" }), new VoiceLabExample("airy", new[] { "airy" }) });
        }

        [Test]
        public void Shelf_StartsWithEachPickOneGroupsDefault()
        {
            Assert.That(Shelf().Start(), Is.EqualTo(new[] { "female", "light" }));
            Assert.That(Shelf().Groups.Select(g => g.PickOne), Is.EqualTo(new[] { true, true, false }));
        }

        [Test]
        public void PressingAWord_ReplacesItsSlot_AndOnlyManyGroupsLetGo()
        {
            VoiceLabShelf shelf = Shelf();
            IReadOnlyList<string> picked = shelf.Start();
            Assert.That(shelf.Toggle(picked, "male"), Is.EqualTo(new[] { "male", "light" }));
            Assert.That(shelf.Toggle(picked, "female"), Is.EqualTo(picked), "A group of one always keeps one.");
            picked = shelf.Toggle(shelf.Toggle(picked, "soft"), "airy");
            Assert.That(picked, Is.EqualTo(new[] { "female", "light", "soft", "airy" }));
            Assert.That(shelf.Toggle(picked, "focused"), Is.EqualTo(new[] { "female", "light", "focused", "airy" }), "Opposites share a slot.");
            Assert.That(shelf.Toggle(picked, "airy"), Is.EqualTo(new[] { "female", "light", "soft" }));
            Assert.That(shelf.Toggle(picked, "nonsense"), Is.EqualTo(picked));
        }

        [Test]
        public void AnExample_FillsTheSlotsItLeavesOpenFromTheStart()
        {
            VoiceLabShelf shelf = Shelf();
            Assert.That(shelf.FromExample(shelf.Examples[0]), Is.EqualTo(new[] { "male", "deep", "focused" }));
            Assert.That(shelf.FromExample(shelf.Examples[1]), Is.EqualTo(new[] { "female", "light", "airy" }));
        }

        [Test]
        public void ADefaultWord_PlaysTheBase()
        {
            Assert.That(VoiceLabShelf.WordSampleName(new VoiceLabShelfWord("soft", "resonance", true)), Is.EqualTo("base.wav"));
            Assert.That(VoiceLabShelf.WordSampleName(new VoiceLabShelfWord("airy", "breath", false)), Is.EqualTo("word_airy.wav"));
            Assert.That(VoiceLabShelf.ExampleSampleName(new VoiceLabExample("low-male", null)), Is.EqualTo("example_low-male.wav"));
        }

        [Test]
        public void TheRequest_IsReadBackAsOneSentence()
        {
            var texts = new Dictionary<string, string>
            {
                ["lab.ask.sentence"] = "{who}が、{voice}声で、{how}話す。",
                ["lab.ask.who"] = "{person}{voice}",
                ["lab.ask.list"] = "、",
                ["lab.ask.and"] = "、",
                ["lab.ask.manner"] = "{manner}。",
                ["lab.ask.manner-join"] = "で、",
                ["lab.word.male.phrase"] = "男性",
                ["lab.word.deep.phrase"] = "低く太い",
                ["lab.word.airy.phrase"] = "ほんのり息まじりの",
            };
            string Text(string key) => texts.TryGetValue(key, out string value) ? value : string.Empty;
            VoiceLabShelf shelf = Shelf();
            Assert.That(VoiceLabView.AskSentence(shelf, new[] { "male", "deep", "airy" }, " 少しかすれた ", Text), Is.EqualTo("男性が、低く太い、ほんのり息まじりの声で、話す。 少しかすれた"));
        }

        [TestCase(2f, 0f, 1f, 5, 2)]
        [TestCase(2.4f, 0f, 1f, 5, 2)]
        [TestCase(9f, 0f, 1f, 5, 4)]
        [TestCase(-1f, 0f, 1f, 5, 0)]
        public void SliderSteps_AreCountedFromTheLowEnd(float value, float low, float step, int count, int index)
        {
            Assert.That(VirtualVessel.UI.Controls.ValueSlider.StepIndex(value, low, step, count), Is.EqualTo(index));
        }

        [Test]
        public void Candidates_SortByPitch_KeepingTheOrderMadeForEqualPitches()
        {
            int[] hz = { 268, 406, 214, 268 };
            Assert.That(VoiceLabView.Order(hz, CandidateOrder.AsMade), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(VoiceLabView.Order(hz, CandidateOrder.LowestFirst), Is.EqualTo(new[] { 2, 0, 3, 1 }));
            Assert.That(VoiceLabView.Order(hz, CandidateOrder.HighestFirst), Is.EqualTo(new[] { 1, 0, 3, 2 }));
        }

        [Test]
        public void PitchChange_ShowsWhereTheKnobMovesIt()
        {
            Assert.That(VoiceLabView.MovedHz(268, 12f), Is.EqualTo(536));
            Assert.That(VoiceLabView.PitchChangeText(268, 4f), Is.EqualTo("268 Hz → 338 Hz"));
            Assert.That(VoiceLabView.PitchChangeText(268, 0f), Is.EqualTo("268 Hz"));
            Assert.That(VoiceLabView.PitchChangeText(0, 4f), Is.EqualTo("—"));
        }

        [TestCase(4f, false)]
        [TestCase(5f, true)]
        [TestCase(-5f, true)]
        public void FarMoves_AreAgeBeyondWhatStillSoundedTheSame(float age, bool far)
        {
            Assert.That(VoiceLabView.IsFar(new VoiceLabTimbre(6f, age, 6f, 6f)), Is.EqualTo(far));
        }

        [TestCase(4f, 4f, 4f, true)]
        [TestCase(-4f, 4f, -5f, true)]
        [TestCase(4f, 4f, 3f, false)]
        [TestCase(6f, 0f, 6f, false)]
        public void PitchAgeAndBrightness_AllFar_SoundMechanical(float pitch, float age, float brightness, bool mechanical)
        {
            // Pitch +4, age +4, and the brightness lift together sounded mechanical; pitch +4 with brightness did not.
            Assert.That(VoiceLabView.IsMechanical(new VoiceLabTimbre(pitch, age, brightness, 0f)), Is.EqualTo(mechanical));
        }

        [TestCase(VoiceLabBuildState.Completed, VoiceLabStrings.RunCompleted)]
        [TestCase(VoiceLabBuildState.Failed, VoiceLabStrings.RunFailed)]
        [TestCase(VoiceLabBuildState.Cancelled, VoiceLabStrings.RunCancelled)]
        [TestCase(VoiceLabBuildState.Training, VoiceLabStrings.RunMaking)]
        public void RunStates_HaveTheirChips(VoiceLabBuildState state, string key)
        {
            Assert.That(VoiceLabLibraryView.RunStatus(state).Key, Is.EqualTo(key));
        }

        [TestCase(VirtualVessel.ProjectData.Records.CrudError.NotFound, VoiceLabStrings.CrudNotFound)]
        [TestCase(VirtualVessel.ProjectData.Records.CrudError.Invalid, VoiceLabStrings.CrudInvalid)]
        [TestCase(VirtualVessel.ProjectData.Records.CrudError.Busy, VoiceLabStrings.CrudBusy)]
        [TestCase(VirtualVessel.ProjectData.Records.CrudError.StorageFailed, VoiceLabStrings.CrudStorageFailed)]
        public void CrudFailures_TellTheUserByTheirKind(VirtualVessel.ProjectData.Records.CrudError error, string key)
        {
            Assert.That(VoiceLabView.CrudFailureKey(error), Is.EqualTo(key));
        }

        [Test]
        public void MadeVoices_ShowDatesAndBaseNames()
        {
            Assert.That(VoiceLabLibraryView.DateText(System.DateTimeOffset.MinValue), Is.EqualTo("—"));
            var candidate = new VoiceLabCandidate("c", "c.wav", 140, 100, 200);
            Assert.That(VoiceLabView.BaseName(null, candidate, 0), Is.EqualTo("[" + VoiceLabStrings.RemadeBase + "]"), "A base put back from a run has no number.");
            Assert.That(VoiceLabView.BaseName(null, null, 3), Is.Empty);
        }

        [Test]
        public void HeldTimbres_AreDistinct_AndAtMostSix()
        {
            var held = new List<VoiceLabTimbre> { new VoiceLabTimbre(4f, 0f, 0f, 0f) };
            Assert.That(VoiceLabHoldList.CanHold(held, new VoiceLabTimbre(4f, 0f, 0f, 0f)), Is.False);
            Assert.That(VoiceLabHoldList.CanHold(held, new VoiceLabTimbre(4f, 1f, 0f, 0f)), Is.True);
            for (int i = 1; i < VoiceLabHoldList.Most; i++)
            {
                held.Add(new VoiceLabTimbre(0f, i, 0f, 0f));
            }

            Assert.That(VoiceLabHoldList.CanHold(held, new VoiceLabTimbre(-6f, 0f, 0f, 0f)), Is.False);
        }

        [Test]
        public void Softness_CountsAsAChange()
        {
            Assert.That(VoiceLabView.IsUnchanged(new VoiceLabTimbre(0f, 0f, 0f, 0f, 2f)), Is.False);
        }

        [Test]
        public void TimbreSummary_NamesEachKnob()
        {
            Assert.That(VoiceLabView.TimbreSummary(null, new VoiceLabTimbre(4f, 4f, 0f, -2f, 3f)), Is.EqualTo("lab.timbre.pitch +4 · lab.timbre.age +4 · lab.timbre.brightness 0 · lab.timbre.softness +3 · lab.timbre.breathiness -2"));
        }

        [TestCase(VoiceLabBuildState.Preparing, VoiceLabStrings.BuildPreparing)]
        [TestCase(VoiceLabBuildState.Preprocessing, VoiceLabStrings.BuildLearning)]
        [TestCase(VoiceLabBuildState.Training, VoiceLabStrings.BuildLearning)]
        [TestCase(VoiceLabBuildState.PostProcessing, VoiceLabStrings.BuildFinishing)]
        [TestCase(VoiceLabBuildState.None, null)]
        public void BuildStages_HaveTheirTexts(VoiceLabBuildState state, string key)
        {
            Assert.That(VoiceLabView.BuildStageKey(state), Is.EqualTo(key));
        }

        [Test]
        public void BuildResults_TellOutOfMemoryApart()
        {
            Assert.That(VoiceLabView.BuildResultKey(new VoiceLabBuild("r", VoiceLabBuildState.Completed, false)), Is.Null);
            Assert.That(VoiceLabView.BuildResultKey(new VoiceLabBuild("r", VoiceLabBuildState.Failed, true)), Is.EqualTo(VoiceLabStrings.BuildOutOfMemory));
            Assert.That(VoiceLabView.BuildResultKey(new VoiceLabBuild("r", VoiceLabBuildState.Failed, false)), Is.EqualTo(VoiceLabStrings.BuildFailed));
            Assert.That(VoiceLabView.BuildResultKey(new VoiceLabBuild("r", VoiceLabBuildState.Cancelled, false)), Is.EqualTo(VoiceLabStrings.BuildCancelled));
            Assert.That(VoiceLabView.BuildResultKey(null), Is.EqualTo(VoiceLabStrings.BuildFailed));
        }

        [TestCase(StreamingState.Idle, false)]
        [TestCase(StreamingState.Connecting, true)]
        [TestCase(StreamingState.Live, true)]
        [TestCase(StreamingState.Reconnecting, true)]
        [TestCase(StreamingState.Failed, false)]
        public void Builds_WaitWhileAStreamRuns(StreamingState state, bool streaming)
        {
            Assert.That(VoiceLabView.IsStreaming(state), Is.EqualTo(streaming));
        }

        [Test]
        public void CandidatesBar_SaysGettingReadyThenCounts()
        {
            Assert.That(VoiceLabView.MakingBar(default), Is.EqualTo((VoiceLabStrings.Loading, 0f, string.Empty)));
            Assert.That(VoiceLabView.MakingBar(new VoiceLabMaking(2, 6)).LabelKey, Is.EqualTo(VoiceLabStrings.Making));
            Assert.That(VoiceLabView.MakingBar(new VoiceLabMaking(2, 6)).Fraction, Is.EqualTo(1f / 3f).Within(1e-6f));
            Assert.That(VoiceLabView.MakingBar(new VoiceLabMaking(2, 6)).Detail, Is.EqualTo("2 / 6"));
            Assert.That(VoiceLabView.CandidateCounts[VoiceLabView.DefaultCountIndex], Is.EqualTo(6));
        }

        [Test]
        public void Names_AreTrimmed_AndBlankIsNone()
        {
            Assert.That(VoiceLabView.CleanName("  ふわり "), Is.EqualTo("ふわり"));
            Assert.That(VoiceLabView.CleanName("   "), Is.Null);
            Assert.That(VoiceLabView.PercentText(0.456f), Is.EqualTo("46%"));
        }

        [Test]
        public void Playback_EndsWhenNothingPlaysOrItReachedTheEnd()
        {
            Assert.That(VoiceLabPlayer.HasEnded(null), Is.True);
            Assert.That(VoiceLabPlayer.HasEnded(1f), Is.True);
            Assert.That(VoiceLabPlayer.HasEnded(0.5f), Is.False);
        }
    }
}
