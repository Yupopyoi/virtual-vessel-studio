using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Audio;
using VirtualVessel.Core.Time;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Screens;
using VirtualVessel.UI.Recording;
using VirtualVessel.UI.Streaming;
using VirtualVessel.UI.Voice;
using VirtualVessel.UI.VoiceLab;
using Object = UnityEngine.Object;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The Voice Lab screen (system design 14.13 to 14.18), with a Voice Lab that answers at once.</summary>
    public sealed class VoiceLabPanelTests
    {
        private string _directory;
        private GameObject _host;
        private UiRoot _root;
        private FakeLab _lab;
        private FakePreview _preview;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _lab = new FakeLab();
            _preview = new FakePreview();
            yield return Build(new ScreenServices { VoiceLab = _lab, AudioPreview = _preview });
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_host);
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [UnityTest]
        public IEnumerator WithoutAnEnvironment_ItSaysVoicesCannotBeMadeYet()
        {
            Object.Destroy(_host);
            _lab.Availability = VoiceLabAvailability.NotSetUp;
            yield return Build(new ScreenServices { VoiceLab = _lab });

            VoiceLabScreen screen = Screen();
            Assert.That(screen.Steps, Is.Null);
            Assert.That(screen.Query<Card>().ToList().Any(c => c.TitleKey == VoiceLabStrings.NotSetUpTitle && c.resolvedStyle.display == DisplayStyle.Flex), Is.True);
            Assert.That(screen.LiveLock.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "Not live, so not locked.");
        }

        [UnityTest]
        public IEnumerator AVoice_IsMadeStepByStep_FromCandidateToRegistration()
        {
            VoiceLabScreen screen = Screen();
            var baseStep = (VoiceLabBaseStep)screen.Panel(VoiceLabScreen.BaseStep);
            Assert.That(screen.Current, Is.EqualTo(VoiceLabScreen.BaseStep));
            Assert.That(baseStep.NextButton.enabledSelf, Is.False, "Nothing is picked yet.");

            VoiceLabShelfView request = baseStep.RequestView;
            Submit(request.WordButton("male"));
            Submit(request.WordButton("airy"));
            Assert.That(request.WordButton("male").Variant, Is.EqualTo(ButtonVariant.Primary), "A picked word is filled.");
            Assert.That(request.WordButton("female").Variant, Is.EqualTo(ButtonVariant.Secondary));
            Assert.That(request.SentenceText, Does.Contain("lab.word.male.phrase").Or.Contain("男性").Or.Contain("A man"));
            Submit(baseStep.MakeButton);
            yield return null;
            Assert.That(_lab.Requests.Single().Words, Is.EqualTo(new[] { "male", "light", "airy" }), "The pressed words replace their slot's.");
            Assert.That(baseStep.InstructionText, Is.EqualTo("instruction"), "The instruction the candidates came from can be read.");
            List<CandidateTile> tiles = baseStep.Tiles.ToList();
            Assert.That(tiles.Count, Is.EqualTo(VoiceLabView.CandidatesPerRound));

            Submit(tiles[1].PlayButton);
            yield return null;
            Assert.That(_preview.Played, Is.EqualTo(new[] { "c1.wav" }));

            Submit(tiles[1].PickButton);
            yield return null;
            Assert.That(tiles[1].Picked, Is.True);
            Assert.That(baseStep.NextButton.enabledSelf, Is.True);
            Submit(baseStep.NextButton);
            yield return null;
            Assert.That(screen.Current, Is.EqualTo(VoiceLabScreen.TimbreStep));

            var timbre = (VoiceLabTimbreStep)screen.Panel(VoiceLabScreen.TimbreStep);
            ValueSlider age = timbre.Query<ValueSlider>().ToList()[1];
            VisualElement far = timbre.Q(className: VoiceLabTimbreStep.FarClass);
            age.value = 4f;
            Assert.That(far.resolvedStyle.display == DisplayStyle.None || far.style.display == DisplayStyle.None, Is.True, "+4 still sounded like the same person.");
            age.value = 6f;
            Assert.That(far.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            age.value = 4f;

            Submit(timbre.NextButton);
            yield return null;
            var build = (VoiceLabBuildStep)screen.Panel(VoiceLabScreen.BuildStep);
            Assert.That(screen.Current, Is.EqualTo(VoiceLabScreen.BuildStep));
            Submit(build.StartButton);
            yield return null;
            Assert.That(_lab.Built.Single().Candidate.Id, Is.EqualTo("c1"));
            Assert.That(_lab.Built.Single().Timbre.Age, Is.EqualTo(4f));
            Assert.That(_lab.BuiltLength, Is.EqualTo(VoiceLabTrainingLength.Short), "Short unless chosen in the details.");
            Assert.That(screen.Current, Is.EqualTo(VoiceLabScreen.TryStep), "A finished voice goes on to be tried.");

            screen.GoTo(VoiceLabScreen.RegisterStep);
            yield return null;
            var register = (VoiceLabRegisterStep)screen.Panel(VoiceLabScreen.RegisterStep);
            Submit(register.RegisterButton);
            yield return null;
            Assert.That(register.NameInput.ErrorKey, Is.EqualTo(VoiceLabStrings.NameRequired));
            Assert.That(_lab.Registered, Is.Empty);

            register.NameInput.value = " ふわり ";
            Submit(register.RegisterButton);
            float waited = 0f;
            while (_lab.Registered.Count == 0 && waited < 5f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            yield return null;
            Assert.That(_lab.Registered, Is.EqualTo(new[] { ("run-1", "ふわり") }));
        }

        [UnityTest]
        public IEnumerator PressingShapedAgain_KeepsTheOneBeingMade_AndAChangedTimbreSaysItStartedOver()
        {
            VoiceLabScreen screen = Screen();
            var baseStep = (VoiceLabBaseStep)screen.Panel(VoiceLabScreen.BaseStep);
            Submit(baseStep.MakeButton);
            yield return null;
            Submit(baseStep.Tiles.First().PickButton);
            yield return null;
            Submit(baseStep.NextButton);
            yield return null;

            var timbre = (VoiceLabTimbreStep)screen.Panel(VoiceLabScreen.TimbreStep);
            ValueSlider pitch = timbre.Query<ValueSlider>().First();
            ActionButton shaped = timbre.Query<ActionButton>().ToList().Single(b => b.TextKey == VoiceLabStrings.ListenShaped);
            _lab.HoldPreviews = true;
            pitch.value = 2f;
            Submit(shaped);
            Submit(shaped);
            yield return null;
            Assert.That(_lab.Previews.Count, Is.EqualTo(1), "Pressing again with the same timbre does not start over.");
            Assert.That(timbre.ShapingKey, Is.EqualTo(VoiceLabStrings.Shaping));

            pitch.value = 3f;
            Submit(shaped);
            yield return null;
            Assert.That(_lab.Previews.Count, Is.EqualTo(2));
            Assert.That(_lab.Previews[0].Source.Task.IsCanceled, Is.True);
            Assert.That(timbre.ShapingKey, Is.EqualTo(VoiceLabStrings.Reshaping), "Stopping the earlier one is said.");
            StatusChip failed = timbre.Query<StatusChip>().ToList().Single(c => c.TextKey == VoiceLabStrings.ShapeFailed);
            Assert.That(failed.style.display.value, Is.EqualTo(DisplayStyle.None), "The stopped one is not a failure.");

            _lab.Previews[1].Source.SetResult(new VoiceLabPreview("p3.wav", 300));
            yield return null;
            Assert.That(timbre.ShapingKey, Is.Null);
            Assert.That(_preview.Played.Last(), Is.EqualTo("p3.wav"));
        }

        [UnityTest]
        public IEnumerator TimbresCanBeHeld_Played_AndGoneBackTo_UntilTheBaseChanges()
        {
            VoiceLabScreen screen = Screen();
            var baseStep = (VoiceLabBaseStep)screen.Panel(VoiceLabScreen.BaseStep);
            Submit(baseStep.MakeButton);
            yield return null;
            Submit(baseStep.Tiles.First().PickButton);
            yield return null;
            Submit(baseStep.NextButton);
            yield return null;

            var timbre = (VoiceLabTimbreStep)screen.Panel(VoiceLabScreen.TimbreStep);
            ValueSlider pitch = timbre.Query<ValueSlider>().First();
            pitch.value = 4f;
            Submit(timbre.HoldButton);
            Assert.That(timbre.HoldButton.enabledSelf, Is.False, "The same timbre is held once.");
            pitch.value = -2f;
            Submit(timbre.HoldButton);
            Assert.That(timbre.HeldList.Held.Select(t => t.Pitch), Is.EqualTo(new[] { 4f, -2f }));

            Submit(timbre.HeldList.Query<IconButton>().First());
            yield return null;
            Assert.That(_lab.Previews.Last().Timbre.Pitch, Is.EqualTo(4f), "A held timbre plays its own preview.");

            timbre.Restore(timbre.HeldList.Held[0]);
            Assert.That(timbre.Timbre.Pitch, Is.EqualTo(4f));

            screen.GoTo(VoiceLabScreen.BaseStep);
            Submit(baseStep.Tiles.Last().PickButton);
            yield return null;
            screen.GoTo(VoiceLabScreen.TimbreStep);
            timbre.Refresh();
            Assert.That(timbre.HeldList.Held, Is.Empty, "Held timbres belong to the base.");
        }

        [UnityTest]
        public IEnumerator Samples_AreOfferedUntilMade_ThenEachWordAndExamplePlays()
        {
            var baseStep = (VoiceLabBaseStep)Screen().Panel(VoiceLabScreen.BaseStep);
            VoiceLabShelfView request = baseStep.RequestView;
            IconButton[] plays = request.Query<IconButton>().ToList().ToArray();
            Assert.That(plays.All(p => p.style.display == DisplayStyle.Flex && !p.enabledSelf), Is.True, "Shown but disabled before the samples are made, so the user sees where they will be.");

            Card examples = request.Query<Card>().ToList().First(c => c.TitleKey == VoiceLabStrings.Examples);
            Assert.That(examples.style.display.value, Is.EqualTo(DisplayStyle.None), "Nothing to play, so the examples wait with the samples.");

            Submit(request.MakeSamplesButton);
            yield return null;
            Assert.That(_lab.SampleRuns, Is.EqualTo(1));
            Assert.That(plays.All(p => p.enabledSelf), Is.True);
            Assert.That(examples.style.display.value, Is.EqualTo(DisplayStyle.Flex));

            Submit(plays[0]);
            yield return null;
            Assert.That(_preview.Played.Last(), Does.EndWith("example_low.wav"));

            request.PickExample(_lab.Shelf.Examples[0]);
            Assert.That(request.Picked, Is.EqualTo(new[] { "male", "deep", "clean" }), "A group of one keeps its default where the example has none.");
        }

        [UnityTest]
        public IEnumerator WordsWithoutASample_AreOfferedTheirs_WhileTheExamplesStay()
        {
            _lab.SamplesMade = true;
            _lab.MissingSamples.Add("word_airy.wav");
            var baseStep = (VoiceLabBaseStep)Screen().Panel(VoiceLabScreen.BaseStep);
            VoiceLabShelfView request = baseStep.RequestView;
            request.RefreshSamples();
            yield return null;

            Assert.That(request.OffersSamples, Is.True, "A word a later version added has no sample yet.");
            Card examples = request.Query<Card>().ToList().First(c => c.TitleKey == VoiceLabStrings.Examples);
            Assert.That(examples.style.display.value, Is.EqualTo(DisplayStyle.Flex), "The base is made, so the examples play.");

            _lab.MissingSamples.Clear();
            request.RefreshSamples();
            Assert.That(request.OffersSamples, Is.False);
        }

        [UnityTest]
        public IEnumerator CandidatesNearAPickedOne_AreAddedAndSayWhereTheyCameFrom()
        {
            VoiceLabScreen screen = Screen();
            var baseStep = (VoiceLabBaseStep)screen.Panel(VoiceLabScreen.BaseStep);
            Submit(baseStep.MakeButton);
            yield return null;
            Assert.That(baseStep.MakeNearButton.enabledSelf, Is.False, "Nothing is picked yet.");

            CandidateTile third = baseStep.Tiles.ToList()[2];
            Submit(third.PickButton);
            yield return null;
            Assert.That(baseStep.MakeNearButton.enabledSelf, Is.True);
            Submit(baseStep.MakeNearButton);
            yield return null;

            Assert.That(_lab.Near.Last().Id, Is.EqualTo("c2"));
            List<CandidateTile> tiles = baseStep.Tiles.ToList();
            Assert.That(tiles.Count, Is.EqualTo(2 * VoiceLabView.CandidatesPerRound), "The new ones join the earlier ones.");
            Assert.That(tiles.Skip(VoiceLabView.CandidatesPerRound).All(t => t.FromNumber == 3), Is.True);
            Assert.That(tiles.Count(t => t.Picked), Is.EqualTo(1), "The picked one stays picked.");
        }

        [UnityTest]
        public IEnumerator HowManyToMake_CanBeChosen_SixByDefault()
        {
            var baseStep = (VoiceLabBaseStep)Screen().Panel(VoiceLabScreen.BaseStep);
            Submit(baseStep.MakeButton);
            yield return null;
            baseStep.CountChooser.value = 0;
            Submit(baseStep.MakeButton);
            yield return null;

            Assert.That(_lab.Counts, Is.EqualTo(new[] { 6, 1 }));
            Assert.That(baseStep.Tiles.Count(), Is.EqualTo(1), "Making again replaces the candidates.");
        }

        [UnityTest]
        public IEnumerator PressingAStepReached_OpensIt()
        {
            VoiceLabScreen screen = Screen();
            var baseStep = (VoiceLabBaseStep)screen.Panel(VoiceLabScreen.BaseStep);
            Submit(baseStep.MakeButton);
            yield return null;
            Submit(baseStep.Tiles.First().PickButton);
            screen.GoTo(VoiceLabScreen.BuildStep);
            screen.GoTo(VoiceLabScreen.BaseStep);
            yield return null;

            Submit(screen.Steps.Query<Button>().AtIndex(VoiceLabScreen.TimbreStep));
            yield return null;

            Assert.That(screen.Steps.Current, Is.EqualTo(VoiceLabScreen.TimbreStep));
        }

        // Builds a voice through STEP 3 and opens STEP 4.
        private IEnumerator ToTryStep(VoiceLabScreen screen)
        {
            var baseStep = (VoiceLabBaseStep)screen.Panel(VoiceLabScreen.BaseStep);
            Submit(baseStep.MakeButton);
            yield return null;
            Submit(baseStep.Tiles.First().PickButton);
            screen.GoTo(VoiceLabScreen.BuildStep);
            Submit(((VoiceLabBuildStep)screen.Panel(VoiceLabScreen.BuildStep)).StartButton);
            yield return null;
            Assert.That(screen.Current, Is.EqualTo(VoiceLabScreen.TryStep));
        }

        [UnityTest]
        public IEnumerator ARecording_IsConvertedWithTheVoiceJustMade_AndPlayedToTheMonitor()
        {
            Object.Destroy(_host);
            var trial = new FakeTrial();
            yield return Build(new ScreenServices { VoiceLab = _lab, AudioPreview = _preview, VoiceTrial = trial });
            VoiceLabScreen screen = Screen();
            yield return ToTryStep(screen);
            VoiceLabTryStep step = screen.Try;
            step.Recording = "me.m4a";
            yield return null;

            Submit(step.ConvertedButton);
            yield return null;
            Submit(step.ConvertedButton);
            yield return null;

            Assert.That(trial.Requests.Count, Is.EqualTo(1), "The same conversion plays again without converting again.");
            Assert.That((trial.Requests[0].VoiceFolder, trial.Requests[0].Recording, trial.Requests[0].AutoPitch), Is.EqualTo(("built", "me.m4a", true)));
            Assert.That(_preview.Played.Last(), Is.EqualTo("converted-1.wav"));

            step.Adjustment.value = 2f;
            Submit(step.ConvertedButton);
            yield return null;
            Assert.That(trial.Requests.Count, Is.EqualTo(2), "A new adjustment converts again.");
            Assert.That(trial.Requests[1].KnownUserHz, Is.EqualTo(200f), "The measured pitch is reused.");

            Submit(step.OriginalButton);
            yield return null;
            Assert.That(_preview.Played.Last(), Is.EqualTo("me.m4a"));
        }

        [UnityTest]
        public IEnumerator TheWork_IsKeptAMomentAfterItChanges_AsTheStepsStand()
        {
            VoiceLabScreen screen = Screen();
            var baseStep = (VoiceLabBaseStep)screen.Panel(VoiceLabScreen.BaseStep);
            Assert.That(_lab.KeptWork, Is.Null);
            Submit(baseStep.MakeButton);
            yield return null;
            Submit(baseStep.Tiles.Last().PickButton);
            screen.GoTo(VoiceLabScreen.TimbreStep);
            var timbre = (VoiceLabTimbreStep)screen.Panel(VoiceLabScreen.TimbreStep);
            timbre.Restore(new VoiceLabTimbre(3f, 0f, 0f, 0f));
            Submit(timbre.HoldButton);
            yield return new WaitForSeconds(0.7f);

            VoiceLabWork kept = _lab.KeptWork;
            Assert.That(kept, Is.Not.Null);
            Assert.That((kept.Step, kept.Candidates.Count, kept.Picked), Is.EqualTo((VoiceLabScreen.TimbreStep, 6, 6)));
            Assert.That((kept.Timbre.Pitch, kept.Held.Single().Pitch), Is.EqualTo((3f, 3f)));
            Assert.That(_lab.WorkSaves, Is.LessThan(4), "Changes close together are kept in one write.");
            Assert.That(screen.Resume.WorkCard.style.display.value, Is.EqualTo(DisplayStyle.None), "This session holds the work itself.");
        }

        [UnityTest]
        public IEnumerator KeptWork_IsOffered_AndContinuesWhereItStopped()
        {
            Object.Destroy(_host);
            var candidates = new[]
            {
                new VoiceLabWorkCandidate(1, 0, new VoiceLabCandidate("work_1_500", "a.wav", 240, 200, 300, "calm", 500)),
                new VoiceLabWorkCandidate(2, 1, new VoiceLabCandidate("work_2_501", "b.wav", 260, 200, 300, "calm", 501)),
            };
            var request = new VoiceLabRequest(new[] { "male" }, "husky", null, "おはよう。", 502);
            _lab.KeptWork = new VoiceLabWork(VoiceLabScreen.TimbreStep, request, candidates, 2, new VoiceLabTimbre(2f, 1f, 0f, 0f), new[] { new VoiceLabTimbre(4f, 0f, 0f, 0f) }, DateTimeOffset.UtcNow);
            yield return Build(new ScreenServices { VoiceLab = _lab, AudioPreview = _preview });
            VoiceLabScreen screen = Screen();
            yield return null;

            Assert.That(screen.Resume.WorkCard.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Submit(screen.Resume.ContinueButton);
            yield return null;

            var baseStep = (VoiceLabBaseStep)screen.Panel(VoiceLabScreen.BaseStep);
            var timbre = (VoiceLabTimbreStep)screen.Panel(VoiceLabScreen.TimbreStep);
            Assert.That(screen.Current, Is.EqualTo(VoiceLabScreen.TimbreStep));
            Assert.That(baseStep.Tiles.Count(), Is.EqualTo(2));
            Assert.That(baseStep.Tiles.Single(t => t.Picked), Is.Not.Null, "The candidate picked is picked again.");
            Assert.That(baseStep.RequestView.Picked, Does.Contain("male"));
            Assert.That((baseStep.Details.Line.value, baseStep.Details.FirstSeed), Is.EqualTo(("おはよう。", 502)));
            Assert.That(timbre.Timbre.Pitch, Is.EqualTo(2f));
            Assert.That(timbre.HeldList.Held.Single().Pitch, Is.EqualTo(4f));
            Assert.That(screen.Resume.WorkCard.style.display.value, Is.EqualTo(DisplayStyle.None), "Continued, so no longer offered.");

            Submit(baseStep.MakeButton);
            yield return null;
            Assert.That(baseStep.Tiles.Count(), Is.EqualTo(6), "Make replaces the set, as a new request does.");
        }

        [UnityTest]
        public IEnumerator AVoiceNotRegistered_IsOfferedInTheFirstSteps_AndOpensSTEP4()
        {
            Object.Destroy(_host);
            var library = new FakeLibrary();
            library.Runs.Add(new VoiceLabRunRecord("run-old", DateTimeOffset.UtcNow.AddDays(-1), VoiceLabBuildState.Completed, new[] { "male" }, "", default, "", "a.wav", null));
            library.Runs.Add(new VoiceLabRunRecord("run-new", DateTimeOffset.UtcNow, VoiceLabBuildState.Completed, new[] { "male" }, "", default, "", "b.wav", null));
            library.Runs.Add(new VoiceLabRunRecord("run-kept", DateTimeOffset.UtcNow, VoiceLabBuildState.Completed, new[] { "male" }, "", default, "", "c.wav", new[] { "Fuwari" }));
            _lab.RemakeResult = new VoiceLabRemake(new[] { "male" }, "", new VoiceLabCandidate("candidate_7", "b.wav", 140, 100, 200), default);
            yield return Build(new ScreenServices { VoiceLab = _lab, AudioPreview = _preview, VoiceLabLibrary = library, VoiceTrial = new FakeTrial() });
            VoiceLabScreen screen = Screen();
            screen.ShowTab(VoiceLabScreen.MakeTab);
            yield return null;

            Assert.That(screen.Resume.VoiceCard.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Submit(screen.Resume.TryButton);
            yield return null;
            Assert.That(screen.Current, Is.EqualTo(VoiceLabScreen.TryStep), "The newest one is tried.");
            Assert.That(screen.Resume.VoiceCard.style.display.value, Is.EqualTo(DisplayStyle.None), "Not over the voice being tried.");
        }

        [UnityTest]
        public IEnumerator ChangingTheLanguage_KeepsMadeVoicesInTheirOwnTab()
        {
            // Made voices showed under Make a voice right after the language changed (the user's report, 2026-11-15).
            Object.Destroy(_host);
            var library = new FakeLibrary();
            library.Runs.Add(new VoiceLabRunRecord("run-1", DateTimeOffset.UtcNow, VoiceLabBuildState.Completed, new[] { "male" }, "", default, "", "b.wav", new[] { "Fuwari" }));
            yield return Build(new ScreenServices { VoiceLab = _lab, AudioPreview = _preview, VoiceLabLibrary = library });
            VoiceLabScreen screen = Screen();
            screen.ShowTab(VoiceLabScreen.MakeTab);
            yield return null;

            foreach (string language in new[] { "en", "ja" })
            {
                _root.Settings.SetLanguage(language);
                yield return null;
                yield return null;
                VoiceLabScreen shown = Screen();
                Assert.That(shown.Library.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), $"After switching to {language}, Made voices stays in its tab.");
                Assert.That(shown.Tabs.value, Is.EqualTo(VoiceLabScreen.MakeTab));
            }
        }

        [UnityTest]
        public IEnumerator TheTermsTab_ShowsTheTermsAsText_AndWhetherTheyWereAgreed()
        {
            Object.Destroy(_host);
            var setup = new FakeSetup { State = VoiceLabSetupState.Ready, TermsAgreed = true };
            yield return Build(new ScreenServices { VoiceLab = _lab, AudioPreview = _preview, VoiceLabLibrary = new FakeLibrary(), VoiceLabSetup = setup });
            VoiceLabScreen screen = Screen();

            screen.ShowTab(VoiceLabScreen.TermsTab);
            yield return null;

            Assert.That(screen.Terms.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(screen.Library.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(screen.Terms.Body.Query<Label>().ToList().Select(l => l.text), Is.EqualTo(new[] { "Terms", "Article 1 (Use)", "1. Use it well.", "1. A nested item.", "Name ／ Source", "uv ／ GitHub" }), "As text, without the Markdown's marks.");
            Assert.That(screen.Terms.AgreedChip.TextKey, Is.EqualTo(VoiceLabStrings.TermsTabAgreed));
        }

        [UnityTest]
        public IEnumerator AConversion_GoesOnWhileAnotherScreenShows_AndCanBePlayedAfter()
        {
            // Leaving for another screen cancelled the conversion, and it could not be started again (the user's
            // report, 2026-11-15).
            Object.Destroy(_host);
            var library = new FakeLibrary();
            library.Voices.Add(new VoiceLabVoiceRecord("voice-1", "Fuwari", "run-1", DateTimeOffset.UtcNow, 285, null));
            var trial = new FakeTrial { Hold = true };
            yield return Build(new ScreenServices { VoiceLab = _lab, AudioPreview = _preview, VoiceLabLibrary = library, VoiceTrial = trial });
            VoiceLabScreen screen = Screen();
            screen.ShowTab(VoiceLabScreen.MadeTab);
            yield return null;
            Submit(screen.Library.Query<ActionButton>().Where(b => b.TextKey == VoiceLabStrings.LibraryTry).First());
            yield return null;
            screen.Try.Recording = "me.wav";
            Submit(screen.Try.ConvertedButton);
            yield return null;
            Assert.That(screen.Try.ConvertedButton.enabledSelf, Is.False, "Converting.");

            screen.GoTo(VoiceLabScreen.RegisterStep);
            yield return null;
            Assert.That(trial.Cancelled, Is.False, "Another step does not stop the conversion.");
            screen.GoTo(VoiceLabScreen.TryStep);
            yield return null;
            Assert.That(screen.Try.ConvertedButton.enabledSelf, Is.False, "Still converting when back.");

            _root.Shell.Open("live");
            yield return null;
            Assert.That(trial.Cancelled, Is.False, "Another screen does not stop the conversion.");
            trial.Finish();
            yield return null;
            yield return null;
            Assert.That(_preview.Played, Is.Empty, "Not played over another screen.");

            _root.Shell.Open("lab");
            yield return null;
            Assert.That(screen.Try.ConvertedButton.enabledSelf, Is.True);
            Submit(screen.Try.ConvertedButton);
            yield return null;
            Assert.That(trial.Requests.Count, Is.EqualTo(1), "The finished conversion plays without converting again.");
            Assert.That(_preview.Played.Last(), Is.EqualTo("converted-1.wav"));
        }

        [UnityTest]
        public IEnumerator TryingFromMadeVoices_OpensSTEP4_WithThatVoice()
        {
            Object.Destroy(_host);
            var library = new FakeLibrary();
            library.Voices.Add(new VoiceLabVoiceRecord("voice-1", "Fuwari", "run-1", DateTimeOffset.UtcNow, 285, null));
            var trial = new FakeTrial();
            yield return Build(new ScreenServices { VoiceLab = _lab, AudioPreview = _preview, VoiceLabLibrary = library, VoiceTrial = trial });
            VoiceLabScreen screen = Screen();
            screen.ShowTab(VoiceLabScreen.MadeTab);
            yield return null;

            Submit(screen.Library.Query<ActionButton>().Where(b => b.TextKey == VoiceLabStrings.LibraryTry).First());
            yield return null;

            Assert.That(screen.Current, Is.EqualTo(VoiceLabScreen.TryStep));
            screen.Try.Recording = "me.wav";
            Submit(screen.Try.ConvertedButton);
            yield return null;
            Assert.That(trial.Requests.Single().VoiceFolder, Is.EqualTo("voices/voice-1"), "The registered voice, not a build.");
        }

        [UnityTest]
        public IEnumerator Registering_UsesTheVoiceForTheStream_WhenAsked()
        {
            Object.Destroy(_host);
            var voice = new FakeVoice();
            yield return Build(new ScreenServices { VoiceLab = _lab, AudioPreview = _preview, Voice = voice });
            VoiceLabScreen screen = Screen();
            yield return ToTryStep(screen);
            screen.GoTo(VoiceLabScreen.RegisterStep);
            var register = (VoiceLabRegisterStep)screen.Panel(VoiceLabScreen.RegisterStep);
            Assert.That(register.UseNow.value, Is.True, "On by default.");
            register.NameInput.value = "Fuwari";

            Submit(register.RegisterButton);
            yield return null;
            yield return null;

            Assert.That(voice.InUseVoiceId, Is.EqualTo("voice-run-1"));
        }

        [UnityTest]
        public IEnumerator MadeVoices_UseAVoice_AndKeepTheOneInUseFromDeletion()
        {
            Object.Destroy(_host);
            var library = new FakeLibrary();
            library.Voices.Add(new VoiceLabVoiceRecord("voice-1", "Fuwari", "run-1", DateTimeOffset.UtcNow, 285, null));
            var voice = new FakeVoice();
            yield return Build(new ScreenServices { VoiceLab = _lab, AudioPreview = _preview, VoiceLabLibrary = library, Voice = voice });
            VoiceLabScreen screen = Screen();
            screen.ShowTab(VoiceLabScreen.MadeTab);
            yield return null;

            Submit(screen.Library.Query<ActionButton>().Where(b => b.TextKey == VoiceLabStrings.Use).First());
            yield return null;

            Assert.That(voice.InUseVoiceId, Is.EqualTo("voice-1"));
            Assert.That(screen.Library.Query<StatusChip>().Where(c => c.TextKey == VoiceLabStrings.InUse).ToList(), Has.Count.EqualTo(1));
            ActionButton delete = screen.Library.Query<ActionButton>().Where(b => b.TextKey == VoiceLabStrings.Delete).First();
            Assert.That(delete.enabledSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator AfterRegistering_STEP4CanBeOpenedAgain()
        {
            VoiceLabScreen screen = Screen();
            yield return ToTryStep(screen);
            screen.GoTo(VoiceLabScreen.RegisterStep);
            var register = (VoiceLabRegisterStep)screen.Panel(VoiceLabScreen.RegisterStep);
            register.Q<TextInput>().value = "Fuwari";
            Submit(register.RegisterButton);
            yield return null;
            yield return null;

            Submit(register.BackButton);

            Assert.That(screen.Current, Is.EqualTo(VoiceLabScreen.TryStep));
        }

        [UnityTest]
        public IEnumerator TheMicrophoneTrial_UsesTheVoiceJustMade_AndPutsTheConversionBack()
        {
            Object.Destroy(_host);
            var voice = new FakeVoice { Semitones = 3f };
            voice.SelectVoice("voice-old");
            yield return Build(new ScreenServices { VoiceLab = _lab, AudioPreview = _preview, Voice = voice });
            VoiceLabScreen screen = Screen();
            yield return ToTryStep(screen);
            VoiceLabTryStep step = screen.Try;
            step.Sources.value = VoiceLabTryStep.MicrophoneSource;

            step.MicToggle.value = true;
            Assert.That((voice.VoiceId, voice.State.Value), Is.EqualTo(("folder:built", VoiceConversionState.Running)));
            Assert.That(voice.Semitones, Is.EqualTo(0f), "STEP 4's own adjustment.");

            screen.GoTo(VoiceLabScreen.TimbreStep);
            Assert.That((voice.VoiceId, voice.State.Value, voice.Semitones), Is.EqualTo(("voice-old", VoiceConversionState.Off, 3f)), "As before the trial.");
        }

        [UnityTest]
        public IEnumerator ChangingTheTimbre_ClosesTheStepsAfterIt()
        {
            VoiceLabScreen screen = Screen();
            var baseStep = (VoiceLabBaseStep)screen.Panel(VoiceLabScreen.BaseStep);
            Submit(baseStep.MakeButton);
            yield return null;
            Submit(baseStep.Tiles.First().PickButton);
            screen.GoTo(VoiceLabScreen.BuildStep);
            Assert.That(screen.Steps.Reached, Is.EqualTo(VoiceLabScreen.BuildStep));

            screen.GoTo(VoiceLabScreen.TimbreStep);
            screen.Panel(VoiceLabScreen.TimbreStep).Q<ValueSlider>().value = 2f;

            Assert.That(screen.Steps.Reached, Is.EqualTo(VoiceLabScreen.TimbreStep));
        }

        [UnityTest]
        public IEnumerator TheScreen_IsLocked_WhileStreamingOrRecording()
        {
            Object.Destroy(_host);
            var streaming = new StreamPanelTests.FakeStreaming();
            var recording = new RecordPanelTests.FakeRecording();
            streaming.SetState(StreamingState.Live);
            yield return Build(new ScreenServices { VoiceLab = _lab, Streaming = streaming, Recording = recording });

            VoiceLabScreen screen = Screen();
            Assert.That(screen.LiveLock.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(screen.Q(className: VoiceLabScreen.ContentClass).style.display.value, Is.EqualTo(DisplayStyle.None), "Voice Lab is hidden.");

            streaming.SetState(StreamingState.Idle);
            Assert.That(screen.LiveLock.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(screen.Q(className: VoiceLabScreen.ContentClass).style.display.value, Is.EqualTo(DisplayStyle.Flex));

            recording.SetState(RecordingState.Recording);
            Assert.That(screen.LiveLock.style.display.value, Is.EqualTo(DisplayStyle.Flex), "Recording locks it too.");
        }

        [UnityTest]
        public IEnumerator ALongerTraining_ChosenInTheDetails_IsBuilt_AndSaysItsTime()
        {
            VoiceLabScreen screen = Screen();
            var baseStep = (VoiceLabBaseStep)screen.Panel(VoiceLabScreen.BaseStep);
            Submit(baseStep.MakeButton);
            yield return null;
            Submit(baseStep.Tiles.First().PickButton);
            screen.GoTo(VoiceLabScreen.BuildStep);
            var build = (VoiceLabBuildStep)screen.Panel(VoiceLabScreen.BuildStep);

            build.LengthChooser.value = (int)VoiceLabTrainingLength.Long;
            Assert.That(build.Query<Label>().ToList().Any(l => l.text == _root.Localizer.Get(VoiceLabStrings.BuildNoteLong)), Is.True);
            Submit(build.StartButton);
            yield return null;

            Assert.That(_lab.BuiltLength, Is.EqualTo(VoiceLabTrainingLength.Long));
        }

        [UnityTest]
        public IEnumerator AFailedBuild_SaysWhy_AndStaysOnTheStep()
        {
            _lab.BuildResult = new VoiceLabBuild("run-1", VoiceLabBuildState.Failed, outOfMemory: true);
            VoiceLabScreen screen = Screen();
            var baseStep = (VoiceLabBaseStep)screen.Panel(VoiceLabScreen.BaseStep);
            Submit(baseStep.MakeButton);
            yield return null;
            Submit(baseStep.Tiles.First().PickButton);
            screen.GoTo(VoiceLabScreen.BuildStep);
            var build = (VoiceLabBuildStep)screen.Panel(VoiceLabScreen.BuildStep);

            Submit(build.StartButton);
            yield return null;

            Assert.That(screen.Current, Is.EqualTo(VoiceLabScreen.BuildStep));
            Assert.That(build.Query<StatusChip>().ToList().Any(c => c.TextKey == VoiceLabStrings.BuildOutOfMemory && c.style.display == DisplayStyle.Flex), Is.True);
            Assert.That(build.Log.Lines.Last(), Does.EndWith(_root.Localizer.Get(VoiceLabStrings.BuildLogFailed)), "The record says how it ended.");
            Assert.That(screen.Steps.enabledSelf, Is.True, "The steps open again after a build.");
        }

        private IEnumerator Build(ScreenServices services)
        {
            _host = new GameObject(nameof(VoiceLabPanelTests));
            _host.AddComponent<UIDocument>();
            _root = _host.AddComponent<UiRoot>();
            _root.SetAssets(UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset"));
            _root.Build(new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false, screens: services));
            _root.Shell.Open("lab");
            yield return null;
        }

        [UnityTest]
        public IEnumerator MadeVoices_AreListedRenamedDeletedAndMadeAgain()
        {
            Object.Destroy(_host);
            var library = new FakeLibrary();
            library.Voices.Add(new VoiceLabVoiceRecord("voice-1", "Fuwari", "run-1", DateTimeOffset.UtcNow, 140, "b.wav"));
            library.Runs.Add(new VoiceLabRunRecord("run-1", DateTimeOffset.UtcNow, VoiceLabBuildState.Completed, new[] { "male" }, "", new VoiceLabTimbre(4f, 0f, 0f, 0f), "", "b.wav", new[] { "Fuwari" }));
            yield return Build(new ScreenServices { VoiceLab = _lab, AudioPreview = _preview, VoiceLabLibrary = library });

            VoiceLabScreen screen = Screen();
            screen.ShowTab(VoiceLabScreen.MadeTab);
            yield return null;
            VoiceLabLibraryView view = screen.Library;
            Assert.That(view.Query<ActionButton>().ToList().Count(b => b.TextKey == VoiceLabStrings.Delete && !view.StorageCard.Contains(b)), Is.EqualTo(2), "A voice row and a run row.");

            ActionButton rename = view.Query<ActionButton>().ToList().First(b => b.TextKey == VoiceLabStrings.Rename);
            Submit(rename);
            view.Query<TextInput>().ToList().First().value = "Kirari";
            Submit(view.Query<ActionButton>().ToList().First(b => b.TextKey == VoiceLabStrings.SaveName));
            Assert.That(library.Voices.Single().Name, Is.EqualTo("Kirari"));

            library.DeleteFailure = VirtualVessel.ProjectData.Records.CrudError.StorageFailed;
            Submit(view.Query<ActionButton>().ToList().First(b => b.TextKey == VoiceLabStrings.Delete));
            yield return null;
            Submit(_root.Dialogs.Current.Confirm);
            yield return null;
            StatusChip failed = view.Query<StatusChip>().ToList().First(c => c.TextKey == VoiceLabStrings.CrudStorageFailed);
            Assert.That(failed.style.display.value, Is.EqualTo(DisplayStyle.Flex), "The screen, as the caller, tells the user.");
            library.DeleteFailure = VirtualVessel.ProjectData.Records.CrudError.None;

            Submit(view.Query<ActionButton>().ToList().First(b => b.TextKey == VoiceLabStrings.Delete));
            yield return null;
            Assert.That(library.Voices.Count, Is.EqualTo(1), "Asked before deleting.");
            Assert.That(_root.Dialogs.Current.Q<Label>().text, Is.Not.Empty);
            Submit(_root.Dialogs.Current.Confirm);
            yield return null;
            Assert.That(library.Voices, Is.Empty);
            Assert.That(library.Runs.Count, Is.EqualTo(1), "Deleting a voice keeps its run.");

            var candidate = new VoiceLabCandidate("candidate_7", "b.wav", 140, 100, 200);
            _lab.RemakeResult = new VoiceLabRemake(new[] { "male" }, "husky", candidate, new VoiceLabTimbre(4f, 0f, 0f, 0f, 2f));
            Submit(view.Query<ActionButton>().ToList().First(b => b.TextKey == VoiceLabStrings.Remake));
            yield return null;
            Assert.That(screen.Current, Is.EqualTo(VoiceLabScreen.TimbreStep));
            var timbre = (VoiceLabTimbreStep)screen.Panel(VoiceLabScreen.TimbreStep);
            Assert.That(timbre.Timbre.Softness, Is.EqualTo(2f));
            var baseStep = (VoiceLabBaseStep)screen.Panel(VoiceLabScreen.BaseStep);
            Assert.That(baseStep.RequestView.Picked, Does.Contain("male"));

            _lab.RemakeResult = new VoiceLabRemake(new[] { "male" }, "", null, default);
            screen.Remake("run-1");
            Assert.That(screen.Current, Is.EqualTo(VoiceLabScreen.BaseStep), "Without the base, STEP 1 opens with the words.");
        }

        [UnityTest]
        public IEnumerator APromptWrittenWhole_IsAskedWith_InPlaceOfTheWords()
        {
            VoiceLabScreen screen = Screen();
            var baseStep = (VoiceLabBaseStep)screen.Panel(VoiceLabScreen.BaseStep);
            VoiceLabShelfView request = baseStep.RequestView;
            request.Mode.value = VoiceLabShelfView.PromptMode;
            yield return null;
            Assert.That(request.Prompt.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(request.WordButton("male").worldBound.height, Is.EqualTo(0f).Or.NaN, "The shelf is hidden.");

            Submit(baseStep.MakeButton);
            yield return null;
            Assert.That(_lab.Requests, Is.Empty, "Nothing is asked with an empty prompt.");
            Assert.That(request.Prompt.ErrorKey, Is.EqualTo(VoiceLabStrings.PromptRequired));

            request.Prompt.value = "A calm woman in her twenties.";
            Submit(baseStep.MakeButton);
            yield return null;
            VoiceLabRequest asked = _lab.Requests.Single();
            Assert.That((asked.Prompt, asked.Words.Count, asked.IsWritten), Is.EqualTo(("A calm woman in her twenties.", 0, true)));
            Assert.That(request.Prompt.ErrorKey, Is.Null);
        }

        [UnityTest]
        public IEnumerator ARunMadeFromAWrittenPrompt_OpensWithIt()
        {
            VoiceLabScreen screen = Screen();
            _lab.RemakeResult = new VoiceLabRemake(null, "", null, default, "A calm woman.");

            screen.Remake("run-1");
            yield return null;

            var baseStep = (VoiceLabBaseStep)screen.Panel(VoiceLabScreen.BaseStep);
            Assert.That(baseStep.RequestView.Mode.value, Is.EqualTo(VoiceLabShelfView.PromptMode));
            Assert.That(baseStep.RequestView.Prompt.value, Is.EqualTo("A calm woman."));
            var run = new VoiceLabRunRecord("run-1", DateTimeOffset.UtcNow, VoiceLabBuildState.Completed, null, "", default, "", null, null, "A calm woman.");
            Assert.That(VoiceLabLibraryView.RunSummary(run, null), Does.StartWith("A calm woman. / "));
        }

        [UnityTest]
        public IEnumerator TheDetails_SendTheLineAndSeed_AndTheSeedMovesOn()
        {
            VoiceLabScreen screen = Screen();
            var baseStep = (VoiceLabBaseStep)screen.Panel(VoiceLabScreen.BaseStep);
            VoiceLabCandidateDetails details = baseStep.Details;
            yield return null;
            Assert.That(details.Card.Expanded, Is.False, "Folded until the user opens it.");
            Assert.That(details.Line.value, Is.EqualTo(FakeLab.DefaultLine));
            Assert.That(details.ResetLineButton.enabledSelf, Is.False, "Nothing to reset yet.");

            baseStep.CountChooser.value = 1;
            Submit(baseStep.MakeButton);
            yield return null;
            VoiceLabRequest first = _lab.Requests.Last();
            Assert.That((first.Line, first.FirstSeed), Is.EqualTo(("", 500)), "The default line is not sent; the script reads the shelf's.");
            Assert.That(details.FirstSeed, Is.EqualTo(503), "Moved on by the three made, so Make gives new voices.");
            Assert.That(baseStep.Tiles.First().Query<Label>().ToList().Select(l => l.text), Has.Member("500"), "The tile shows its seed.");

            details.Line.value = "おはよう。";
            details.Seed.value = 500;
            Assert.That(details.ResetLineButton.enabledSelf, Is.True);
            Submit(baseStep.MakeButton);
            yield return null;
            Assert.That((_lab.Requests.Last().Line, _lab.Requests.Last().FirstSeed), Is.EqualTo(("おはよう。", 500)), "A seed given again makes the same seeds.");

            Submit(details.ResetLineButton);
            Assert.That(details.Line.value, Is.EqualTo(FakeLab.DefaultLine));
        }

        [UnityTest]
        public IEnumerator TheInstruction_IsCopied_AndTheButtonSaysSoForAMoment()
        {
            VoiceLabScreen screen = Screen();
            var baseStep = (VoiceLabBaseStep)screen.Panel(VoiceLabScreen.BaseStep);
            Submit(baseStep.MakeButton);
            yield return null;
            UnityEngine.GUIUtility.systemCopyBuffer = string.Empty;

            Submit(baseStep.CopyInstructionButton);
            Assert.That(UnityEngine.GUIUtility.systemCopyBuffer, Is.EqualTo(baseStep.InstructionText));
            Assert.That(baseStep.CopyInstructionButton.TextKey, Is.EqualTo(VoiceLabStrings.InstructionCopied));
            yield return new WaitForSeconds(2.2f);
            Assert.That(baseStep.CopyInstructionButton.TextKey, Is.EqualTo(VoiceLabStrings.InstructionCopy));
        }

        [UnityTest]
        public IEnumerator AnEmptyLine_AsksNothing_AndOpensTheDetailsOnItsError()
        {
            VoiceLabScreen screen = Screen();
            var baseStep = (VoiceLabBaseStep)screen.Panel(VoiceLabScreen.BaseStep);
            baseStep.Details.Line.value = "  ";
            yield return null;

            Submit(baseStep.MakeButton);
            yield return null;
            Assert.That(_lab.Requests, Is.Empty);
            Assert.That(baseStep.Details.Line.ErrorKey, Is.EqualTo(VoiceLabStrings.LineRequired));
            Assert.That(baseStep.Details.Card.Expanded, Is.True);
        }

        [UnityTest]
        public IEnumerator ARunsLineAndSeed_ComeBack_ToMakeItsBaseAgain()
        {
            VoiceLabScreen screen = Screen();
            _lab.RemakeResult = new VoiceLabRemake(new[] { "male" }, "", null, default, null, "おはよう。", 4242);

            screen.Remake("run-1");
            yield return null;

            var baseStep = (VoiceLabBaseStep)screen.Panel(VoiceLabScreen.BaseStep);
            Assert.That((baseStep.Details.Line.value, baseStep.Details.FirstSeed), Is.EqualTo(("おはよう。", 4242)));
        }

        [UnityTest]
        public IEnumerator TheSamplesAndOldCandidates_AreDeletedApart_AfterAsking()
        {
            Object.Destroy(_host);
            var library = new FakeLibrary();
            yield return Build(new ScreenServices { VoiceLab = _lab, AudioPreview = _preview, VoiceLabLibrary = library });
            VoiceLabScreen screen = Screen();
            screen.ShowTab(VoiceLabScreen.MadeTab);
            yield return null;
            VoiceLabLibraryView view = screen.Library;
            Assert.That(view.Query<Label>().ToList().Select(l => l.text), Does.Contain("25 MB").And.Contain("39 MB"));

            Submit(view.DeleteSamplesButton);
            yield return null;
            Assert.That(library.SamplesDeleted, Is.EqualTo(0), "Asked first.");
            Submit(_root.Dialogs.Current.Confirm);
            yield return null;

            Assert.That((library.SamplesDeleted, library.CandidatesDeleted), Is.EqualTo((1, 0)), "Only the samples.");
            Assert.That(view.DeleteSamplesButton.enabledSelf, Is.False, "Nothing left to delete.");
            Assert.That(view.DeleteCandidatesButton.enabledSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator TheTrainingEnvironment_IsDeletedOnlyWhenTheAppBuiltIt()
        {
            Object.Destroy(_host);
            var library = new FakeLibrary();
            yield return Build(new ScreenServices { VoiceLab = _lab, AudioPreview = _preview, VoiceLabLibrary = library });
            VoiceLabScreen screen = Screen();
            screen.ShowTab(VoiceLabScreen.MadeTab);
            yield return null;
            VoiceLabEnvironmentRow row = screen.EnvironmentRow;
            Assert.That(row.DeleteButton.enabledSelf, Is.False, "A developer's environment is not the app's to delete.");
            Assert.That(row.DeleteButton.TooltipKey, Is.EqualTo(VoiceLabStrings.StorageEnvironmentExternal));

            _lab.EnvironmentState = VoiceLabEnvironmentState.AppBuilt;
            row.Show();
            yield return null;
            Assert.That(row.Query<Label>().ToList().Select(l => l.text), Does.Contain("6.0 GB"));
            Submit(row.DeleteButton);
            yield return null;
            Assert.That(_lab.EnvironmentRemovals, Is.EqualTo(0), "Asked first.");
            Submit(_root.Dialogs.Current.Confirm);
            yield return null;

            Assert.That(_lab.EnvironmentRemovals, Is.EqualTo(1));
            Assert.That(row.style.display.value, Is.EqualTo(DisplayStyle.None), "Gone, so its row goes too.");
        }

        [UnityTest]
        public IEnumerator Preparing_ChecksThePc_ShowsTheTerms_Builds_AndThenOpensTheSteps()
        {
            Object.Destroy(_host);
            _lab.Availability = VoiceLabAvailability.NotSetUp;
            var setup = new FakeSetup { Lab = _lab };
            yield return Build(new ScreenServices { VoiceLab = _lab, VoiceLabLibrary = new FakeLibrary(), VoiceLabSetup = setup });
            yield return null;

            VoiceLabScreen screen = Screen();
            VoiceLabSetupView view = screen.Setup;
            Assert.That(screen.Steps, Is.Null, "Nothing to make with yet.");
            Assert.That(view.Page, Is.EqualTo(VoiceLabSetupPage.Check));
            Assert.That(view.LastCheck, Is.SameAs(setup.Check));
            Assert.That(view.ToTermsButton.enabledSelf, Is.True, "This PC meets every condition.");
            Assert.That(screen.Query<Card>().ToList().Any(c => c.TitleKey == VoiceLabStrings.NotSetUpTitle), Is.False);

            Submit(view.ToTermsButton);
            yield return null;
            Assert.That(view.Page, Is.EqualTo(VoiceLabSetupPage.Terms));
            Assert.That(view.TermsCard.Query<Label>().ToList().Any(l => l.text == "Article 1 (Use)"), Is.True, "The terms are shown in full.");
            Assert.That(view.StartButton.enabledSelf, Is.False, "Not before agreeing.");
            view.AgreeToggle.value = true;
            Assert.That(view.StartButton.enabledSelf, Is.True);

            Submit(view.StartButton);
            yield return null;
            Assert.That(setup.TermsAgreed, Is.True);
            Assert.That(setup.Runs, Is.EqualTo(1));
            Assert.That(view.Page, Is.EqualTo(VoiceLabSetupPage.Progress));
            Assert.That(view.StopButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(view.ResumeButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));

            setup.Log.Add(new VoiceLabSetupEntry(new DateTime(2026, 11, 11, 22, 14, 5), VoiceLabSetupEntryKind.Download, "voxcpm2-model", 4L * 1024 * 1024 * 1024));
            setup.Log.Add(new VoiceLabSetupEntry(new DateTime(2026, 11, 11, 22, 20, 0), VoiceLabSetupEntryKind.Unpack));
            yield return new WaitForSeconds(0.4f);
            List<Label> lines = view.ProgressCard.Q(className: VoiceLabSetupView.LogClass).Query<Label>().ToList();
            Assert.That(lines.Count, Is.EqualTo(2), "The record shows each line once.");
            Assert.That(lines[0].text, Does.StartWith("22:14:05").And.Contain("VoxCPM2"));

            setup.Finish(true);
            yield return null;
            yield return null;
            Assert.That(view.Page, Is.EqualTo(VoiceLabSetupPage.Done));

            Submit(view.ToMakeButton);
            yield return null;
            Assert.That(screen.Steps, Is.Not.Null, "The steps are built once ready.");
            Assert.That(screen.Current, Is.EqualTo(VoiceLabScreen.BaseStep));
            Assert.That(view.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "Nothing left to prepare.");
        }

        [UnityTest]
        public IEnumerator APcThatDoesNotMeetTheConditions_SaysWhat_AndKeepsTheTermsClosed()
        {
            Object.Destroy(_host);
            _lab.Availability = VoiceLabAvailability.NotSetUp;
            var setup = new FakeSetup { Check = new VoiceLabPcCheck(null, false, null, false, @"C:\Users\me\AppData\Local", 10L * 1024 * 1024 * 1024, false, true) };
            yield return Build(new ScreenServices { VoiceLab = _lab, VoiceLabSetup = setup });
            yield return null;

            VoiceLabSetupView view = Screen().Setup;
            Assert.That(view.ToTermsButton.enabledSelf, Is.False);
            List<Label> problems = view.CheckCard.Query<Label>(className: VoiceLabSetupView.ProblemClass).ToList().Where(l => l.resolvedStyle.display == DisplayStyle.Flex).ToList();
            Assert.That(problems.Count, Is.EqualTo(3), "The GPU, the driver, and the space; the connection is fine.");
        }

        [UnityTest]
        public IEnumerator AStoppedBuild_Continues_AndAsksForTheTermsAgainWhenTheyChanged()
        {
            Object.Destroy(_host);
            _lab.Availability = VoiceLabAvailability.NotSetUp;
            var setup = new FakeSetup { State = VoiceLabSetupState.Paused, TermsAgreed = true };
            yield return Build(new ScreenServices { VoiceLab = _lab, VoiceLabSetup = setup });

            VoiceLabSetupView view = Screen().Setup;
            Assert.That(view.Page, Is.EqualTo(VoiceLabSetupPage.Progress));
            Assert.That(view.ResumeButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Submit(view.ResumeButton);
            yield return null;
            Assert.That(setup.Runs, Is.EqualTo(1));

            Submit(view.StopButton);
            yield return null;
            yield return null;
            Assert.That(view.Page, Is.EqualTo(VoiceLabSetupPage.Progress), "Stopped again, it can continue.");
            Assert.That(setup.State, Is.EqualTo(VoiceLabSetupState.Paused));

            setup.TermsAgreed = false;
            Submit(view.ResumeButton);
            yield return null;
            Assert.That(view.Page, Is.EqualTo(VoiceLabSetupPage.Terms), "Changed terms come first.");
            Assert.That(setup.Runs, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AFailedBuild_SaysWhatToDo()
        {
            Object.Destroy(_host);
            _lab.Availability = VoiceLabAvailability.NotSetUp;
            var setup = new FakeSetup { State = VoiceLabSetupState.Failed, LastFailure = VoiceLabSetupFailure.DiskFull, TermsAgreed = true };
            yield return Build(new ScreenServices { VoiceLab = _lab, VoiceLabSetup = setup });

            VoiceLabSetupView view = Screen().Setup;
            StatusChip title = view.ProgressCard.Query<StatusChip>().ToList().First(c => c.TextKey == VoiceLabStrings.SetupFailedDiskTitle);
            Assert.That(title.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(title.Status, Is.EqualTo(StatusKind.Error));
        }

        [UnityTest]
        public IEnumerator ChangedTerms_WithAnUpdateWaiting_OnlyAgree_ThenOfferTheUpdate()
        {
            Object.Destroy(_host);
            var setup = new FakeSetup { State = VoiceLabSetupState.Ready, UpdateAvailable = true, TermsAgreed = false };
            yield return Build(new ScreenServices { VoiceLab = _lab, VoiceLabSetup = setup });
            yield return null;

            VoiceLabSetupView view = Screen().Setup;
            Assert.That(view.Page, Is.EqualTo(VoiceLabSetupPage.Terms));
            Assert.That(view.StartButton.TextKey, Is.EqualTo(VoiceLabStrings.SetupAgreeOnly), "Agreeing does not start the update.");
            Assert.That(view.BackButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));

            view.AgreeToggle.value = true;
            Submit(view.StartButton);
            yield return null;

            Assert.That((setup.TermsAgreed, setup.Runs), Is.EqualTo((true, 0)));
            Assert.That(view.Page, Is.EqualTo(VoiceLabSetupPage.Update), "The update is the user's choice, on its own card.");
        }

        [UnityTest]
        public IEnumerator AnUpdate_IsOfferedAboveTheSteps_WhichStayUsable()
        {
            Object.Destroy(_host);
            var setup = new FakeSetup { State = VoiceLabSetupState.Ready, UpdateAvailable = true, TermsAgreed = true };
            yield return Build(new ScreenServices { VoiceLab = _lab, VoiceLabSetup = setup });

            VoiceLabScreen screen = Screen();
            Assert.That(screen.Steps, Is.Not.Null);
            Assert.That(screen.Setup.Page, Is.EqualTo(VoiceLabSetupPage.Update));
            Submit(screen.Setup.UpdateButton);
            yield return null;
            Assert.That(screen.Setup.Page, Is.EqualTo(VoiceLabSetupPage.Progress));
            setup.Finish(true);
            yield return null;
            yield return null;
            Assert.That(screen.Setup.Page, Is.EqualTo(VoiceLabSetupPage.None), "Updated; no first-build notice.");
        }

        [UnityTest]
        public IEnumerator AfterTheEnvironmentIsDeleted_TheStepsGo_AndPreparingComesBack()
        {
            Object.Destroy(_host);
            var setup = new FakeSetup { State = VoiceLabSetupState.Ready, TermsAgreed = true };
            yield return Build(new ScreenServices { VoiceLab = _lab, VoiceLabLibrary = new FakeLibrary(), VoiceLabSetup = setup });
            VoiceLabScreen screen = Screen();
            Assert.That(screen.Steps.parent.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));

            _lab.Availability = VoiceLabAvailability.NotSetUp;
            setup.State = VoiceLabSetupState.NotStarted;
            screen.ShowTab(VoiceLabScreen.MakeTab);
            yield return null;

            Assert.That(screen.Steps.parent.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(screen.Setup.Page, Is.EqualTo(VoiceLabSetupPage.Check));
            Assert.That(screen.Setup.LastCheck, Is.Not.Null, "Checked again on coming back to it.");
        }

        [UnityTest]
        public IEnumerator TheChecksValues_FollowTheLanguage()
        {
            Object.Destroy(_host);
            _lab.Availability = VoiceLabAvailability.NotSetUp;
            var setup = new FakeSetup();
            yield return Build(new ScreenServices { VoiceLab = _lab, VoiceLabSetup = setup });
            yield return null;
            VoiceLabSetupView view = Screen().Setup;

            _root.Localizer.SetLocale("ja");
            yield return null;
            Assert.That(view.CheckCard.Query<Label>().ToList().Any(l => l.text == "接続済み"), Is.True);

            _root.Localizer.SetLocale("en");
            yield return null;
            List<string> texts = view.CheckCard.Query<Label>().ToList().Select(l => l.text).ToList();
            Assert.That(texts, Does.Contain("Connected"));
            Assert.That(texts.Any(t => t.Contains("空き") || t.Contains("接続済み")), Is.False, "Nothing is left in Japanese.");
        }

        [UnityTest]
        public IEnumerator ChangingTheLanguage_KeepsTheTabShown()
        {
            Object.Destroy(_host);
            _lab.Availability = VoiceLabAvailability.NotSetUp;
            var library = new FakeLibrary();
            library.Voices.Add(new VoiceLabVoiceRecord("voice-1", "Fuwari", "run-1", DateTimeOffset.UtcNow, 140, "b.wav"));
            yield return Build(new ScreenServices { VoiceLab = _lab, VoiceLabLibrary = library, VoiceLabSetup = new FakeSetup { State = VoiceLabSetupState.Paused, TermsAgreed = true } });
            yield return null;
            VoiceLabScreen screen = Screen();
            Assert.That(screen.Library.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "Make a voice is open.");

            _root.Localizer.SetLocale("en");
            yield return null;
            _root.Localizer.SetLocale("ja");
            yield return null;

            Assert.That(screen.Library.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "Still only Make a voice.");
            Assert.That(screen.Tabs.value, Is.EqualTo(VoiceLabScreen.MakeTab));
        }

        [UnityTest]
        public IEnumerator ChangedTerms_AreAgreedToBeforeMaking_WithNothingToBuild()
        {
            Object.Destroy(_host);
            var library = new FakeLibrary();
            library.Runs.Add(new VoiceLabRunRecord("run-1", DateTimeOffset.UtcNow, VoiceLabBuildState.Completed, new[] { "male" }, "", new VoiceLabTimbre(4f, 0f, 0f, 0f), "", "b.wav", new string[0]));
            var setup = new FakeSetup { State = VoiceLabSetupState.Ready, TermsAgreed = false };
            yield return Build(new ScreenServices { VoiceLab = _lab, VoiceLabLibrary = library, VoiceLabSetup = setup });
            yield return null;

            VoiceLabScreen screen = Screen();
            Assert.That(screen.Setup.Page, Is.EqualTo(VoiceLabSetupPage.Terms), "The terms changed since they were agreed to.");
            Assert.That(screen.Steps.parent.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "No making before agreeing.");
            Assert.That(screen.Setup.BackButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "Nowhere to go back to.");
            screen.ShowTab(VoiceLabScreen.MadeTab);
            Assert.That(screen.Library.Query<ActionButton>().ToList().First(b => b.TextKey == VoiceLabStrings.Remake).enabledSelf, Is.False);

            screen.ShowTab(VoiceLabScreen.MakeTab);
            screen.Setup.AgreeToggle.value = true;
            Submit(screen.Setup.StartButton);
            yield return null;

            Assert.That(setup.TermsAgreed, Is.True);
            Assert.That(setup.Runs, Is.EqualTo(0), "Built already: nothing to build.");
            Assert.That(screen.Steps.parent.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            screen.ShowTab(VoiceLabScreen.MadeTab);
            Assert.That(screen.Library.Query<ActionButton>().ToList().First(b => b.TextKey == VoiceLabStrings.Remake).enabledSelf, Is.True, "Made again, the library can make its runs again.");
        }

        [UnityTest]
        public IEnumerator AfterTheEnvironmentIsDeleted_MakeAgainIsOff()
        {
            Object.Destroy(_host);
            var library = new FakeLibrary();
            library.Runs.Add(new VoiceLabRunRecord("run-1", DateTimeOffset.UtcNow, VoiceLabBuildState.Completed, new[] { "male" }, "", new VoiceLabTimbre(4f, 0f, 0f, 0f), "", "b.wav", new string[0]));
            yield return Build(new ScreenServices { VoiceLab = _lab, VoiceLabLibrary = library });
            VoiceLabScreen screen = Screen();
            screen.ShowTab(VoiceLabScreen.MadeTab);
            Assert.That(screen.Library.Query<ActionButton>().ToList().First(b => b.TextKey == VoiceLabStrings.Remake).enabledSelf, Is.True);

            _lab.Availability = VoiceLabAvailability.NotSetUp;
            screen.ShowTab(VoiceLabScreen.MadeTab);
            yield return null;

            Assert.That(screen.Library.Query<ActionButton>().ToList().First(b => b.TextKey == VoiceLabStrings.Remake).enabledSelf, Is.False);
            Assert.That(screen.Library.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "Still on the Made voices tab.");
        }

        [UnityTest]
        public IEnumerator ABuildPausedForStreaming_SaysSo()
        {
            Object.Destroy(_host);
            _lab.Availability = VoiceLabAvailability.NotSetUp;
            var setup = new FakeSetup { State = VoiceLabSetupState.Paused, TermsAgreed = true, PausedForLive = true };
            yield return Build(new ScreenServices { VoiceLab = _lab, VoiceLabSetup = setup });

            Label note = Screen().Setup.ProgressCard.Query<Label>().ToList().Last();
            Assert.That(note.text, Does.Contain("配信").Or.Contain("streaming"));
        }

        private VoiceLabScreen Screen()
        {
            return (VoiceLabScreen)_root.Shell.Host.GetScreen("lab");
        }

        private static void Submit(VisualElement element)
        {
            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = element;
                element.SendEvent(evt);
            }
        }
    }
}
