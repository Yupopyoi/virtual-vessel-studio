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
using VirtualVessel.UI.Streaming;
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
            Assert.That(screen.Q<Card>().TitleKey, Is.EqualTo(VoiceLabStrings.NotSetUpTitle));
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

            Submit(request.MakeSamplesButton);
            yield return null;
            Assert.That(_lab.SampleRuns, Is.EqualTo(1));
            Assert.That(plays.All(p => p.enabledSelf), Is.True);

            Submit(plays[0]);
            yield return null;
            Assert.That(_preview.Played.Last(), Does.EndWith("example_low.wav"));

            request.PickExample(_lab.Shelf.Examples[0]);
            Assert.That(request.Picked, Is.EqualTo(new[] { "male", "deep", "clean" }), "A group of one keeps its default where the example has none.");
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
        public IEnumerator Building_WaitsWhileAStreamRuns()
        {
            Object.Destroy(_host);
            var streaming = new StreamPanelTests.FakeStreaming();
            streaming.SetState(StreamingState.Live);
            yield return Build(new ScreenServices { VoiceLab = _lab, Streaming = streaming });

            VoiceLabScreen screen = Screen();
            var build = (VoiceLabBuildStep)screen.Panel(VoiceLabScreen.BuildStep);
            Assert.That(build.StartButton.enabledSelf, Is.False);

            streaming.SetState(StreamingState.Idle);
            Assert.That(build.StartButton.enabledSelf, Is.True);
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

        private sealed class FakeLab : IVoiceLab
        {
            private int _made;

            public VoiceLabAvailability Availability { get; set; } = VoiceLabAvailability.Ready;

            public List<VoiceLabRequest> Requests { get; } = new List<VoiceLabRequest>();

            public VoiceLabShelf Shelf { get; } = new VoiceLabShelf(
                new[]
                {
                    new VoiceLabShelfGroup("voice", true, new[] { new VoiceLabShelfWord("female", "voice", true), new VoiceLabShelfWord("male", "voice", false) }),
                    new VoiceLabShelfGroup("pitch", true, new[] { new VoiceLabShelfWord("light", "pitch", true), new VoiceLabShelfWord("deep", "pitch", false) }),
                    new VoiceLabShelfGroup("quality", false, new[] { new VoiceLabShelfWord("clean", "breath", true), new VoiceLabShelfWord("airy", "breath", false) }),
                },
                new[] { new VoiceLabExample("low", new[] { "male", "deep" }) });

            public bool SamplesMade { get; set; }

            public int SampleRuns { get; private set; }

            public VoiceLabMaking SamplesProgress => default;

            public string SampleFile(string language, string name)
            {
                return SamplesMade ? language + "/" + name : null;
            }

            public Task MakeSamplesAsync(CancellationToken cancellationToken)
            {
                SampleRuns++;
                SamplesMade = true;
                return Task.CompletedTask;
            }

            public List<VoiceLabCandidate> Near { get; } = new List<VoiceLabCandidate>();

            public List<(VoiceLabCandidate Candidate, VoiceLabTimbre Timbre)> Built { get; } = new List<(VoiceLabCandidate, VoiceLabTimbre)>();

            public List<(string RunId, string Name)> Registered { get; } = new List<(string, string)>();

            public VoiceLabBuild BuildResult { get; set; } = new VoiceLabBuild("run-1", VoiceLabBuildState.Completed, false);

            public VoiceLabBuildProgress BuildProgress => new VoiceLabBuildProgress(VoiceLabBuildState.None, 0f);

            public VoiceLabMaking CandidatesProgress => default;

            public List<int> Counts { get; } = new List<int>();

            public Task<IReadOnlyList<VoiceLabCandidate>> MakeCandidatesAsync(VoiceLabRequest request, int count, VoiceLabCandidate near, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                Near.Add(near);
                Counts.Add(count);
                var made = new List<VoiceLabCandidate>();
                for (int i = 0; i < count; i++, _made++)
                {
                    made.Add(new VoiceLabCandidate("c" + _made, "c" + _made + ".wav", 200 + 10 * _made, 180, 300, "instruction"));
                }

                return Task.FromResult<IReadOnlyList<VoiceLabCandidate>>(made);
            }

            /// <summary>When set, previews wait until their source is completed, and cancelling cancels them.</summary>
            public bool HoldPreviews { get; set; }

            public List<(VoiceLabTimbre Timbre, TaskCompletionSource<VoiceLabPreview> Source)> Previews { get; } = new List<(VoiceLabTimbre, TaskCompletionSource<VoiceLabPreview>)>();

            public Task<VoiceLabPreview> PreviewAsync(VoiceLabCandidate candidate, VoiceLabTimbre timbre, CancellationToken cancellationToken)
            {
                var source = new TaskCompletionSource<VoiceLabPreview>();
                Previews.Add((timbre, source));
                if (!HoldPreviews)
                {
                    source.SetResult(new VoiceLabPreview("p.wav", candidate.MedianHz));
                }

                cancellationToken.Register(() => source.TrySetCanceled());
                return source.Task;
            }

            public Task<VoiceLabBuild> BuildAsync(VoiceLabCandidate candidate, VoiceLabTimbre timbre, CancellationToken cancellationToken)
            {
                Built.Add((candidate, timbre));
                return Task.FromResult(BuildResult);
            }

            public void Register(string runId, string name)
            {
                lock (Registered)
                {
                    Registered.Add((runId, name));
                }
            }
        }

        private sealed class FakePreview : IAudioPreview
        {
            public List<string> Played { get; } = new List<string>();

            public float? Progress => Played.Count > 0 ? 0.5f : (float?)null;

            public Task<bool> PlayAsync(string path)
            {
                Played.Add(path);
                return Task.FromResult(true);
            }

            public void Stop()
            {
            }
        }
    }
}
