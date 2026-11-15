using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Recording;
using VirtualVessel.UI.Screens;
using VirtualVessel.UI.Streaming;
using VirtualVessel.UI.Voice;
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

        [TestCase("今日は。", "今日は。", "")]
        [TestCase("おはよう。", "今日は。", "おはよう。")]
        [TestCase(" \n", "今日は。", null)]
        [TestCase("", "今日は。", null)]
        public void TheLineToSend_IsEmptyForTheDefault_AndNullWhenNothingIsLeft(string text, string defaultLine, string expected)
        {
            Assert.That(VoiceLabCandidateDetails.LineToSend(text, defaultLine), Is.EqualTo(expected));
        }

        [Test]
        public void TheSeed_MovesOnByTheCandidatesMade_AndStartsOverPastTheLargest()
        {
            Assert.That(VoiceLabCandidateDetails.NextSeed(500, 6), Is.EqualTo(506));
            Assert.That(VoiceLabCandidateDetails.NextSeed(500, 0), Is.EqualTo(500), "Nothing made, as when stopped at once.");
            Assert.That(VoiceLabCandidateDetails.NextSeed(VoiceLabCandidateDetails.MaxSeed, 1), Is.EqualTo(0));
        }

        [TestCase(false, true, 0, VoiceLabScreen.BaseStep, false, true, false)]
        [TestCase(true, true, 0, VoiceLabScreen.BaseStep, false, false, false)]
        [TestCase(false, false, 2, VoiceLabScreen.TimbreStep, false, false, true)]
        [TestCase(false, false, 2, VoiceLabScreen.TryStep, false, false, false)]
        [TestCase(false, false, 2, VoiceLabScreen.BaseStep, true, false, false)]
        public void TheResumeNotice_OffersTheWorkOnlyBeforeThisSessionMadeAny_AndAVoiceOnlyInTheFirstSteps(bool made, bool kept, int voices, int step, bool building, bool work, bool voice)
        {
            Assert.That(VoiceLabResumeNotice.WhatToShow(made, kept, voices, step, building), Is.EqualTo((work, voice)));
        }

        [TestCase(VoiceLabScreen.BaseStep, false, false, VoiceLabScreen.BaseStep)]
        [TestCase(VoiceLabScreen.TimbreStep, false, false, VoiceLabScreen.TimbreStep)]
        [TestCase(VoiceLabScreen.BuildStep, false, false, VoiceLabScreen.TimbreStep)]
        [TestCase(VoiceLabScreen.BuildStep, false, true, -1)]
        [TestCase(VoiceLabScreen.BuildStep, true, false, -1)]
        [TestCase(VoiceLabScreen.TryStep, true, false, -1)]
        public void TheWork_IsKeptUntilBuildingBegins(int current, bool built, bool building, int expected)
        {
            Assert.That(VoiceLabResumeNotice.StepToKeep(current, built, building), Is.EqualTo(expected));
        }

        [Test]
        public void VoicesNotRegistered_AreTheCompletedRunsWithoutAVoice_NewestFirst()
        {
            var now = System.DateTimeOffset.UtcNow;
            var runs = new[]
            {
                new VoiceLabRunRecord("old", now.AddDays(-1), VoiceLabBuildState.Completed, null, "", default, "", null, null),
                new VoiceLabRunRecord("failed", now, VoiceLabBuildState.Failed, null, "", default, "", null, null),
                new VoiceLabRunRecord("registered", now, VoiceLabBuildState.Completed, null, "", default, "", null, new[] { "Fuwari" }),
                new VoiceLabRunRecord("new", now, VoiceLabBuildState.Completed, null, "", default, "", null, null),
            };

            Assert.That(VoiceLabResumeNotice.UnregisteredOf(runs).Select(r => r.RunId), Is.EqualTo(new[] { "new", "old" }));
        }

        [TestCase("run-1", null, false, null)]
        [TestCase("run-1", null, true, null)]
        [TestCase(null, "voices/voice-1", false, VoiceLabStrings.RegisterBlockedRegistered)]
        [TestCase("run-1", "voices/voice-1", false, VoiceLabStrings.RegisterBlockedRegistered)]
        [TestCase(null, null, false, VoiceLabStrings.RegisterBlockedNoRun)]
        public void Register_SaysWhyItCannotBePressed(string runId, string tryFolder, bool registeredHere, string expected)
        {
            Assert.That(VoiceLabRegisterStep.BlockedKey(runId, tryFolder, registeredHere), Is.EqualTo(expected));
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
        public void AFoldedGroupOfOne_CanBeTakenOff_ButAMainOneCannot()
        {
            var shelf = new VoiceLabShelf(
                new[]
                {
                    new VoiceLabShelfGroup("voice", true, new[] { new VoiceLabShelfWord("female", "voice", true), new VoiceLabShelfWord("male", "voice", false) }),
                    new VoiceLabShelfGroup("character", false, new[] { new VoiceLabShelfWord("friendly", "character", true), new VoiceLabShelfWord("shy", "character", false) }),
                },
                null);
            IReadOnlyList<string> picked = shelf.Start();
            Assert.That(picked, Is.EqualTo(new[] { "female", "friendly" }), "The screen starts with the character the base has.");
            Assert.That(shelf.Toggle(picked, "friendly"), Is.EqualTo(new[] { "female" }), "Taken off, the character says none (the user's decision, 2026-11-14).");
            Assert.That(shelf.Toggle(picked, "female"), Is.EqualTo(picked));
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

        [TestCase(StreamingState.Idle, RecordingState.Idle, false)]
        [TestCase(StreamingState.Connecting, RecordingState.Idle, true)]
        [TestCase(StreamingState.Live, RecordingState.Idle, true)]
        [TestCase(StreamingState.Reconnecting, RecordingState.Idle, true)]
        [TestCase(StreamingState.Failed, RecordingState.Idle, false)]
        [TestCase(StreamingState.Idle, RecordingState.Starting, true)]
        [TestCase(StreamingState.Idle, RecordingState.Recording, true)]
        [TestCase(StreamingState.Idle, RecordingState.Stopping, true)]
        [TestCase(StreamingState.Idle, RecordingState.Failed, false)]
        public void VoiceLab_IsNotUsed_WhileAStreamOrARecordingRuns(StreamingState streaming, RecordingState recording, bool live)
        {
            Assert.That(VoiceLabView.IsLive(streaming, recording), Is.EqualTo(live));
        }

        [Test]
        public void TheVoiceCard_SaysTheState_AndTheConvertedPitch()
        {
            Assert.That(VoiceCard.StatusOf(VoiceConversionState.Running, true, true), Is.EqualTo((StatusKind.Success, VoiceCardStrings.StateRunning)));
            Assert.That(VoiceCard.StatusOf(VoiceConversionState.Off, true, true), Is.EqualTo((StatusKind.Idle, VoiceCardStrings.StateOff)));
            Assert.That(VoiceCard.StatusOf(VoiceConversionState.Failed, true, true), Is.EqualTo((StatusKind.Error, VoiceCardStrings.StateStopped)));
            Assert.That(VoiceCard.StatusOf(VoiceConversionState.Running, false, true).Key, Is.EqualTo(VoiceCardStrings.StateUnavailable));
            Assert.That(VoiceCard.StatusOf(VoiceConversionState.Off, true, false).Key, Is.EqualTo(VoiceCardStrings.StateUnavailable));
            Assert.That(VoiceCard.FinalHz(200f, true, 6f, 0f), Is.EqualTo(200f * (float)System.Math.Pow(2.0, 0.5)).Within(0.01f));
            Assert.That(VoiceCard.FinalHz(200f, false, 6f, 12f), Is.EqualTo(400f).Within(0.01f), "Automatic off: the adjustment alone.");
            Assert.That(VoiceCard.FinalHz(0f, true, 6f, 0f), Is.EqualTo(0f));
            Assert.That(VoiceCard.Choices(new[] { new VoiceConversionVoice("", null, 0, true), new VoiceConversionVoice("voice-a", "A", 0, false), new VoiceConversionVoice("voice-b", "B", 0, true) }).Select(c => c.Id), Is.EqualTo(new[] { "voice-b" }));
        }

        [Test]
        public void TheVoiceInUse_IsTheSavedChoice()
        {
            Assert.That(VoiceLabLibraryView.IsInUse("voice-a", "voice-a"), Is.True);
            Assert.That(VoiceLabLibraryView.IsInUse("voice-a", "voice-b"), Is.False);
            Assert.That(VoiceLabLibraryView.IsInUse(null, "voice-a"), Is.False);
        }

        [Test]
        public void STEP4_KeysAConversion_AndShowsThePitches()
        {
            Assert.That(VoiceLabTryStep.ConversionKey("f", "r", true, 0f, 200f), Is.EqualTo(VoiceLabTryStep.ConversionKey("f", "r", true, 0f, 200f)));
            Assert.That(VoiceLabTryStep.ConversionKey("f", "r", true, 1f, 200f), Is.Not.EqualTo(VoiceLabTryStep.ConversionKey("f", "r", true, 0f, 200f)));
            Assert.That(VoiceLabTryStep.Hz(285.4f), Is.EqualTo("285 Hz"));
            Assert.That(VoiceLabTryStep.Hz(0f), Is.Null);
            Assert.That(VoiceLabTryStep.Shift(7f), Is.EqualTo("+7"));
            Assert.That(VoiceLabTryStep.Shift(-2f), Is.EqualTo("-2"));
        }

        [Test]
        public void Sizes_AreShownInKbOrMb()
        {
            Assert.That(VoiceLabLibraryView.SizeText(0), Is.EqualTo("0 KB"));
            Assert.That(VoiceLabLibraryView.SizeText(1500), Is.EqualTo("2 KB"));
            Assert.That(VoiceLabLibraryView.SizeText(5L * 1024 * 1024 + 300 * 1024), Is.EqualTo("5.3 MB"));
            Assert.That(VoiceLabLibraryView.SizeText(39L * 1024 * 1024), Is.EqualTo("39 MB"));
            Assert.That(VoiceLabLibraryView.SizeText(6L * 1024 * 1024 * 1024 + 300L * 1024 * 1024), Is.EqualTo("6.3 GB"));
        }

        [Test]
        public void TheTestLength_IsOfferedInTheEditorOnly()
        {
            Assert.That(VoiceLabBuildStep.LengthKeys(true).Last(), Is.EqualTo(VoiceLabStrings.BuildLengthTest));
            Assert.That(VoiceLabBuildStep.LengthKeys(true).Length, Is.EqualTo((int)VoiceLabTrainingLength.EditorTest + 1), "In the enum's order.");
            Assert.That(VoiceLabBuildStep.LengthKeys(false), Does.Not.Contain(VoiceLabStrings.BuildLengthTest));
            Assert.That(VoiceLabBuildStep.NoteKey(VoiceLabTrainingLength.EditorTest), Is.EqualTo(VoiceLabStrings.BuildNoteTest));
        }

        [Test]
        public void StartingWhileAVoiceIsMade_AsksAboutTheBuild()
        {
            var usual = new ConfirmationRequest("t", "b", "c");
            ConfirmationRequest whileBuilding = BuildGuardedStart.WhileBuildingStream();
            Assert.That(BuildGuardedStart.ConfirmationFor(true, usual, whileBuilding), Is.SameAs(whileBuilding));
            Assert.That(BuildGuardedStart.ConfirmationFor(false, usual, whileBuilding), Is.SameAs(usual));
            Assert.That(BuildGuardedStart.ConfirmationFor(false, null, whileBuilding), Is.Null, "Recording starts without asking.");
            Assert.That(BuildGuardedStart.Building(VoiceLabBuildState.None), Is.False);
            Assert.That(BuildGuardedStart.Building(VoiceLabBuildState.Training), Is.True);
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

        [Test]
        public void TheSetupPage_FollowsActivation()
        {
            Assert.That(VoiceLabSetupView.PageOf(VoiceLabSetupState.NotStarted, false, false, false), Is.EqualTo(VoiceLabSetupPage.Check));
            Assert.That(VoiceLabSetupView.PageOf(VoiceLabSetupState.NotStarted, false, true, false), Is.EqualTo(VoiceLabSetupPage.Terms));
            Assert.That(VoiceLabSetupView.PageOf(VoiceLabSetupState.Preparing, true, true, false), Is.EqualTo(VoiceLabSetupPage.Progress), "A running build wins.");
            Assert.That(VoiceLabSetupView.PageOf(VoiceLabSetupState.Paused, false, false, false), Is.EqualTo(VoiceLabSetupPage.Progress));
            Assert.That(VoiceLabSetupView.PageOf(VoiceLabSetupState.Failed, false, false, false), Is.EqualTo(VoiceLabSetupPage.Progress));
            Assert.That(VoiceLabSetupView.PageOf(VoiceLabSetupState.Ready, true, false, true), Is.EqualTo(VoiceLabSetupPage.Done));
            Assert.That(VoiceLabSetupView.PageOf(VoiceLabSetupState.Ready, true, false, false), Is.EqualTo(VoiceLabSetupPage.Update));
            Assert.That(VoiceLabSetupView.PageOf(VoiceLabSetupState.Ready, false, false, false), Is.EqualTo(VoiceLabSetupPage.None));
            Assert.That(VoiceLabSetupView.PageOf(VoiceLabSetupState.Ready, false, false, false, termsAgreed: false), Is.EqualTo(VoiceLabSetupPage.Terms), "Changed terms come before making.");
            Assert.That(VoiceLabSetupView.PageOf(VoiceLabSetupState.Ready, false, false, true, termsAgreed: false), Is.EqualTo(VoiceLabSetupPage.Done));
            Assert.That(VoiceLabSetupView.OnlyToAgree(VoiceLabSetupState.Ready, false), Is.True, "Changed terms only agree, with or without an update waiting.");
            Assert.That(VoiceLabSetupView.OnlyToAgree(VoiceLabSetupState.Ready, true), Is.False, "The user chose to update: it builds after agreeing.");
            Assert.That(VoiceLabSetupView.OnlyToAgree(VoiceLabSetupState.NotStarted, true), Is.False);
            Assert.That(VoiceLabSetupView.PageOf(VoiceLabSetupState.Ready, true, false, false, termsAgreed: false), Is.EqualTo(VoiceLabSetupPage.Terms), "Agreed first; the update card comes after.");
            Assert.That(VoiceLabScreen.CanMakeWith(true, false), Is.False);
            Assert.That(VoiceLabScreen.CanMakeWith(true, true), Is.True);
        }

        [Test]
        public void TheBuildsSteps_ShowDone_TheOneUnderWay_AndNotYet()
        {
            Assert.That(VoiceLabSetupText.StepStatusOf(0, 2, VoiceLabSetupState.Preparing), Is.EqualTo((StatusKind.Success, VoiceLabStrings.SetupStepDone)));
            Assert.That(VoiceLabSetupText.StepStatusOf(2, 2, VoiceLabSetupState.Preparing), Is.EqualTo((StatusKind.Idle, VoiceLabStrings.SetupRunning)), "Live is for streaming only.");
            Assert.That(VoiceLabSetupText.StepStatusOf(2, 2, VoiceLabSetupState.Paused), Is.EqualTo((StatusKind.Warning, VoiceLabStrings.SetupPaused)));
            Assert.That(VoiceLabSetupText.StepStatusOf(2, 2, VoiceLabSetupState.Failed), Is.EqualTo((StatusKind.Error, VoiceLabStrings.SetupFailed)));
            Assert.That(VoiceLabSetupText.StepStatusOf(3, 2, VoiceLabSetupState.Preparing), Is.EqualTo((StatusKind.Idle, VoiceLabStrings.SetupStepLater)));
        }

        [Test]
        public void EachFailure_SaysWhatToDo()
        {
            Assert.That(VoiceLabSetupText.FailureKeys(VoiceLabSetupFailure.Network).Title, Is.EqualTo(VoiceLabStrings.SetupFailedNetworkTitle));
            Assert.That(VoiceLabSetupText.FailureKeys(VoiceLabSetupFailure.DiskFull).Body, Is.EqualTo(VoiceLabStrings.SetupFailedDiskBody));
            Assert.That(VoiceLabSetupText.FailureKeys(VoiceLabSetupFailure.None).Title, Is.EqualTo(VoiceLabStrings.SetupFailedSetupTitle), "A failure from before a restart.");
        }

        [Test]
        public void TheDownload_ShowsItsBytes_AndTheOtherStepsTheirShare()
        {
            const long Gb = 1024L * 1024 * 1024;
            Assert.That(VoiceLabSetupText.ProgressDetail(null, new VoiceLabSetupProgress(VoiceLabSetupStep.Download, 3 * Gb, 6 * Gb)), Is.EqualTo("3.0 GB / 6.0 GB (50%) · 30%"), "The download's own share, then the whole build's.");
            Assert.That(VoiceLabSetupText.ProgressDetail(null, new VoiceLabSetupProgress(VoiceLabSetupStep.Install, 0, 0)), Is.EqualTo("75%"));
        }

        [Test]
        public void TheTermsMarkdown_BecomesHeadingsItemsAndRows()
        {
            var blocks = MarkdownText.Blocks("<!-- version: 1 -->\n<!-- note -->\n\n# 規約\n\n前文の\n続き。\n\n## 第1条（定義）\n\n1. 一つ目\n   1. 入れ子\n\n1\\. 段落の番号\n\n| 名称 | 取得元 |\n|---|---|\n| **uv** | GitHub |\n");

            Assert.That(blocks, Is.EqualTo(new[]
            {
                (MarkdownBlockKind.Title, 0, "規約"),
                (MarkdownBlockKind.Paragraph, 0, "前文の続き。"),
                (MarkdownBlockKind.Heading, 0, "第1条（定義）"),
                (MarkdownBlockKind.Item, 0, "1. 一つ目"),
                (MarkdownBlockKind.Item, 1, "1. 入れ子"),
                (MarkdownBlockKind.Paragraph, 0, "1. 段落の番号"),
                (MarkdownBlockKind.Row, 0, "名称 ／ 取得元"),
                (MarkdownBlockKind.Row, 0, "uv ／ GitHub"),
            }));
        }

        [Test]
        public void ALicensesMarkdown_ReadsAsText_WithItsLinesJoinedAsTheLanguageWrites()
        {
            var blocks = MarkdownText.Blocks("<!--\n  a note\n-->\n# otoha-chan.vrm\n\n- Contact: <https://github.com/x>\n- See [the license](https://vrm.dev/)\n  * nested `code`\n\nThird-party components are\nsubject to their licenses.\n\n本ソフトウェアの\n機能です。\n");

            Assert.That(blocks, Is.EqualTo(new[]
            {
                (MarkdownBlockKind.Title, 0, "otoha-chan.vrm"),
                (MarkdownBlockKind.Item, 0, "・Contact: https://github.com/x"),
                (MarkdownBlockKind.Item, 0, "・See the license (https://vrm.dev/)"),
                (MarkdownBlockKind.Item, 1, "・nested code"),
                (MarkdownBlockKind.Paragraph, 0, "Third-party components are subject to their licenses."),
                (MarkdownBlockKind.Paragraph, 0, "本ソフトウェアの機能です。"),
            }));
            Assert.That(MarkdownText.IsMarkdown("licenses/a.MD"), Is.True);
            Assert.That(MarkdownText.IsMarkdown("LICENSE"), Is.False);
        }

        [Test]
        public void TheBuildRecord_WritesANewStage_ThenItsCountEveryTwentieth()
        {
            var training = new VoiceLabBuildProgress(VoiceLabBuildState.Training, 0.5f, 30, 600);
            Assert.That(VoiceLabBuildLog.Worth(VoiceLabBuildState.Preparing, 48, training), Is.True, "A new stage.");
            Assert.That(VoiceLabBuildLog.Worth(VoiceLabBuildState.Training, 10, training), Is.False, "20 epochs is under a twentieth of 600.");
            Assert.That(VoiceLabBuildLog.Worth(VoiceLabBuildState.Training, 0, training), Is.True);
            Assert.That(VoiceLabBuildLog.Worth(VoiceLabBuildState.Training, 599, new VoiceLabBuildProgress(VoiceLabBuildState.Training, 1f, 600, 600)), Is.True, "The end of a stage.");
            Assert.That(VoiceLabBuildLog.Worth(VoiceLabBuildState.Preprocessing, 0, new VoiceLabBuildProgress(VoiceLabBuildState.Preprocessing, 0.1f, 1, 1)), Is.False, "A count of one is a stage loading.");
            Assert.That(VoiceLabBuildLog.Worth(VoiceLabBuildState.None, -1, default), Is.False);
        }

        [Test]
        public void TheBuildRecordsLine_SaysTheTimeTheStageAndItsCount()
        {
            var at = new System.DateTime(2026, 11, 15, 14, 20, 5);
            Assert.That(VoiceLabBuildLog.LineText(null, at, VoiceLabBuildState.Training, 30, 600, false), Is.EqualTo("14:20:05  " + VoiceLabView.BuildStageKey(VoiceLabBuildState.Training) + "  30 / 600"));
            Assert.That(VoiceLabBuildLog.LineText(null, at, VoiceLabBuildState.Cancelled, 0, 0, true), Is.EqualTo("14:20:05  " + VoiceLabStrings.BuildLogStopped));
        }

        [Test]
        public void TheShippedTerms_ReadAsArticles_InBothLanguages()
        {
            foreach (string language in new[] { "ja", "en" })
            {
                string file = System.IO.Path.GetFullPath(System.IO.Path.Combine("..", "..", "services", "voice-lab", "terms", $"voice-lab-terms.{language}.md"));
                var blocks = MarkdownText.Blocks(System.IO.File.ReadAllText(file));
                Assert.That(blocks.Count(b => b.Kind == MarkdownBlockKind.Heading), Is.EqualTo(12), $"Twelve articles in {language}.");
                Assert.That(blocks.Any(b => b.Text.Contains("<!--") || b.Text.Contains("**") || b.Text.Contains("\\")), Is.False, language);
            }
        }

        [Test]
        public void TheDownloadsSpeed_IsTheAverageOverTheSamples_WithTheTimeLeftAtIt()
        {
            const long Mb = 1024L * 1024;
            var samples = new List<(double Seconds, long Done)> { (0d, 0L), (2d, 10 * Mb), (4d, 20 * Mb) };
            (double rate, double? left) = VoiceLabSetupText.Rate(samples, 620 * Mb);
            Assert.That(rate, Is.EqualTo(5d * Mb).Within(1d));
            Assert.That(left, Is.EqualTo(120d).Within(1e-6), "600 MB left at 5 MB/s.");

            Assert.That(VoiceLabSetupText.Rate(new List<(double, long)> { (1d, 5L) }, 10L), Is.EqualTo((0d, (double?)null)), "One sample has no speed.");
            Assert.That(VoiceLabSetupText.Rate(new List<(double, long)> { (1d, 5L), (2d, 5L) }, 10L).SecondsLeft, Is.Null, "Stalled.");
            Assert.That(VoiceLabSetupText.RateText(null, 12.34 * Mb, 300d), Is.EqualTo("12.3 MB/s"));
        }

        [Test]
        public void TheRecordsLines_NameComponentsByTheirProducts()
        {
            Assert.That(VoiceLabSetupText.ItemName(null, "voxcpm2-model"), Is.EqualTo("VoxCPM2"));
            Assert.That(VoiceLabSetupText.ItemName(null, "hubert-weights"), Is.EqualTo("HuBERT"));
            Assert.That(VoiceLabSetupText.ItemName(null, "rvc"), Is.EqualTo("RVC"));
            Assert.That(VoiceLabSetupText.ItemName(null, "template"), Is.EqualTo("template"), "Without a localizer, the id.");
            Assert.That(VoiceLabSetupText.ItemName(null, "shared-rmvpe"), Is.EqualTo("RMVPE"));

            var entry = new VoiceLabSetupEntry(new System.DateTime(2026, 11, 11, 9, 5, 7), VoiceLabSetupEntryKind.Unpack);
            Assert.That(VoiceLabSetupText.EntryText(null, entry), Is.EqualTo("09:05:07  Unpack"));
        }
    }
}
