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
using VirtualVessel.Avatar;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Lighting;
using VirtualVessel.UI.Screens;
using Object = UnityEngine.Object;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The avatar Card on the setup screen (docs/&lt;lang&gt;/ui/screens/setup.md 5).</summary>
    public sealed class AvatarCardPanelTests
    {
        private string _directory;
        private GameObject _host;
        private UiRoot _root;
        private FakeAvatars _avatars;
        private FakeFiles _files;
        private FakeLighting _lighting;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _avatars = new FakeAvatars();
            _files = new FakeFiles();
            _lighting = new FakeLighting();
            _host = new GameObject(nameof(AvatarCardPanelTests));
            _host.AddComponent<UIDocument>();
            _root = _host.AddComponent<UiRoot>();
            _root.SetAssets(UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset"));
            _root.Build(new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false, screens: new ScreenServices { Avatars = _avatars, FileDialog = _files, Lighting = _lighting }));
            _root.Shell.Open("setup");
            ((SetupScreen)_root.Shell.Host.GetScreen("setup")).Tabs.value = SetupScreen.AvatarPage;
            yield return null;
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
        public IEnumerator Registering_ShowsTheModelsTerms_BeforeRegistering()
        {
            AvatarCard card = _root.Content.Q<AvatarCard>();
            _files.Path = "C:\\models\\a.vrm";

            Submit(card.RegisterButton);
            yield return null;
            yield return null;

            ConfirmDialog dialog = _root.Dialogs.Current;
            Assert.That(dialog, Is.Not.Null, "Asked before registering.");
            var lines = dialog.Query<Label>(className: AvatarCard.LicenseLineClass).ToList().Select(l => l.text).ToList();
            Assert.That(lines, Does.Contain("Author A"));
            Submit(dialog.Confirm);
            yield return null;
            yield return null;

            Assert.That(_avatars.Imported, Is.EqualTo(new[] { "C:\\models\\a.vrm" }));
            Assert.That(card.Choice.value, Is.EqualTo("a"), "The first avatar is put into use.");
            Assert.That(card.License.Query<Label>(className: AvatarCard.LicenseLineClass).ToList().Select(l => l.text), Does.Contain("Author A"));
        }

        [UnityTest]
        public IEnumerator FaceTuning_SlidersTuneEachPart_FollowChanges_AndShowTheAvatarsMovement()
        {
            FaceTuningCard card = _root.Content.Q<FaceTuningCard>();
            Assert.That(card, Is.Not.Null, "Placed with the avatar Card.");
            card.Q<Card>().Expanded = true;
            yield return null;
            Assert.That(card.Sliders[(int)FaceTuningPart.Eyes].value, Is.EqualTo(100f), "100% is the default.");

            card.Sliders[(int)FaceTuningPart.Eyes].value = 200f;
            Assert.That(_avatars.FaceTuning.Value.Eyes, Is.EqualTo(2f));
            Assert.That(_avatars.FaceTuning.Value.Mouth, Is.EqualTo(1f), "Only that part changes.");

            _avatars.SetFaceTuning(_avatars.FaceTuning.Value.With(FaceTuningPart.Mouth, 0.5f));
            Assert.That(card.Sliders[(int)FaceTuningPart.Mouth].value, Is.EqualTo(50f), "Follows a change made elsewhere.");

            var face = new FaceTarget();
            face[FacePart.MouthA] = 0.4f;
            face[FacePart.MouthO] = 0.3f;
            face[FacePart.EyeCloseLeft] = 1f;
            face.LookYaw = -0.6f;
            _avatars.Face = face;
            yield return new WaitForSeconds(0.2f);
            Assert.That(card.Meters[(int)FaceTuningPart.Mouth].Progress, Is.EqualTo(0.7f).Within(1e-4f), "The mouth's shapes together.");
            Assert.That(card.Meters[(int)FaceTuningPart.Wink].Progress, Is.EqualTo(1f), "One eye closed alone.");
            Assert.That(card.Meters[(int)FaceTuningPart.Gaze].Progress, Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(card.Meters[(int)FaceTuningPart.Smile].Progress, Is.EqualTo(0f));

            Assert.That(card.WinksToggle.value, Is.False, "Winks are off by default.");
            Assert.That(card.Sliders[(int)FaceTuningPart.Wink].enabledSelf, Is.False, "The wink slider matters only with winks on.");
            card.WinksToggle.value = true;
            Assert.That(_avatars.FaceTuning.Value.Winks, Is.True);
            Assert.That(card.Sliders[(int)FaceTuningPart.Wink].enabledSelf, Is.True);

            Submit(card.ResetButton);
            Assert.That(_avatars.FaceTuning.Value, Is.EqualTo(new FaceTuning()));
            Assert.That(card.Sliders[(int)FaceTuningPart.Eyes].value, Is.EqualTo(100f));
        }

        [UnityTest]
        public IEnumerator FaceTuning_OnANarrowWindow_PutsTheSlidersUnderThePicture()
        {
            FaceTuningCard card = _root.Content.Q<FaceTuningCard>();
            card.Q<Card>().Expanded = true;
            VisualElement picture = card.Q(className: FaceTuningCard.PictureClass);
            VisualElement parts = card.Q(className: FaceTuningCard.PartsClass);

            // The least layout the UI keeps (UiPanelScale): beside the picture.
            _root.Content.style.width = UiPanelScale.MinWidth;
            yield return null;
            yield return null;
            Assert.That(parts.worldBound.xMin, Is.GreaterThanOrEqualTo(picture.worldBound.xMax), "Beside the picture.");
            Assert.That(parts.worldBound.width, Is.GreaterThanOrEqualTo(320f), "With room for its text.");

            // Narrower than that, as on a 1024 window at 100%: under it, the whole width.
            _root.Content.style.width = 1024f;
            yield return null;
            yield return null;
            Assert.That(parts.worldBound.yMin, Is.GreaterThanOrEqualTo(picture.worldBound.yMax), "Under the picture.");
            Assert.That(parts.worldBound.width, Is.GreaterThanOrEqualTo(picture.worldBound.width), "The whole width.");

            // Narrower still: nothing is squeezed into a column of single characters.
            _root.Content.style.width = 760f;
            yield return null;
            yield return null;
            Assert.That(parts.worldBound.width, Is.GreaterThanOrEqualTo(240f));
            VisualElement winksText = card.WinksToggle.GetFirstAncestorOfType<SettingRow>().Q(className: SettingRow.UssClass + "__text");
            Assert.That(winksText.worldBound.width, Is.GreaterThanOrEqualTo(240f), "A setting's name and description keep their width; the switch goes under them if need be.");
        }

        [UnityTest]
        public IEnumerator ThePicture_MovesUpAndDown_KeepingItsShape()
        {
            FaceTuningCard card = _root.Content.Q<FaceTuningCard>();
            yield return null;
            card.Q<Card>().Expanded = true;
            yield return null;
            Rect frame = card.Frame.worldBound;
            float topAtDefault = card.Preview.worldBound.yMin;

            card.PictureHeight.value = 0f;
            yield return null;
            Assert.That(card.Preview.worldBound.yMax, Is.EqualTo(frame.yMax).Within(0.5f), "The bottom of the stream fills the frame's bottom.");
            Assert.That(card.Preview.worldBound.yMin, Is.LessThan(topAtDefault), "The picture moved.");

            card.PictureHeight.value = 100f;
            yield return null;
            Assert.That(card.Preview.worldBound.yMin, Is.EqualTo(frame.yMin).Within(0.5f), "The top of the stream at the frame's top.");
            Assert.That(card.Preview.worldBound.width, Is.EqualTo(frame.width * FaceTuningCard.DefaultZoom / 100f).Within(1f), "Enlarged, and only moved.");
            Assert.That(FaceTuningCard.Crop(250f, FaceTuningCard.DefaultZoom), Is.EqualTo(FaceTuningCard.Crop(100f, FaceTuningCard.DefaultZoom)), "Kept within the stream.");

            (float left, float top, float width, float height) = FaceTuningCard.Layout(new Rect(0.25f, 0.5f, 0.5f, 0.5f));
            Assert.That((left, top, width, height), Is.EqualTo((-50f, 0f, 200f, 200f)), "The top middle quarter, twice as large.");
        }

        [UnityTest]
        public IEnumerator ThePicture_ZoomsAboutTheStreamsMiddle_WithinTheStream()
        {
            FaceTuningCard card = _root.Content.Q<FaceTuningCard>();
            card.Q<Card>().Expanded = true;
            yield return null;
            Rect frame = card.Frame.worldBound;
            Assert.That(card.PictureZoom.value, Is.EqualTo(FaceTuningCard.DefaultZoom));

            card.PictureZoom.value = 100f;
            yield return null;
            Assert.That(card.Preview.worldBound.width, Is.EqualTo(frame.width).Within(0.5f), "100% shows the whole stream.");
            Assert.That(card.Preview.worldBound.xMin, Is.EqualTo(frame.xMin).Within(0.5f));

            card.PictureZoom.value = 400f;
            yield return null;
            Assert.That(card.Preview.worldBound.width, Is.EqualTo(frame.width * 4f).Within(1f));
            Assert.That(card.Preview.worldBound.center.x, Is.EqualTo(frame.center.x).Within(0.5f), "About the middle.");

            Assert.That(FaceTuningCard.Crop(100f, 200f), Is.EqualTo(new Rect(0.25f, 0.5f, 0.5f, 0.5f)), "The top middle quarter.");
            Assert.That(FaceTuningCard.Crop(0f, 50f), Is.EqualTo(new Rect(0f, 0f, 1f, 1f)), "Never smaller than the stream.");
        }

        [UnityTest]
        public IEnumerator TheLight_IsChangedBesideThePicture_AndFollowsTheStage()
        {
            FaceTuningCard card = _root.Content.Q<FaceTuningCard>();
            card.Q<Card>().Expanded = true;
            yield return null;
            Assert.That(card.Light, Is.Not.Null);
            Assert.That(card.Light.value, Is.EqualTo(100f), "100% is the light as made.");
            Assert.That(card.Light.LowValue, Is.EqualTo(StageLightIntensity.Min * 100f));

            card.Light.value = 60f;
            Assert.That(_lighting.Intensity.Value, Is.EqualTo(0.6f).Within(1e-5f));

            _lighting.SetIntensity(1.2f);
            Assert.That(card.Light.value, Is.EqualTo(120f).Within(1e-3f), "Follows a change made elsewhere.");
            Assert.That(card.Q<Label>(className: FaceTuningCard.LightNoteClass), Is.Not.Null, "Says the stream changes too.");
        }

        [Test]
        public void TheLight_IsLeftOut_WithoutTheStage()
        {
            var card = new FaceTuningCard(_avatars, null);

            Assert.That(card.Light, Is.Null);
            Assert.That(card.Q<Label>(className: FaceTuningCard.LightNoteClass), Is.Null);
        }

        [Test]
        public void Movement_IsNothing_WithoutAnAvatar()
        {
            Assert.That(FaceTuningCard.Movement(null, FaceTuningPart.Eyes), Is.EqualTo(0f));
            var face = new FaceTarget();
            face[FacePart.EyeCloseLeft] = 0.8f;
            face[FacePart.EyeCloseRight] = 0.8f;
            face[FacePart.BrowDown] = 0.5f;
            Assert.That(FaceTuningCard.Movement(face, FaceTuningPart.Eyes), Is.EqualTo(0.8f));
            Assert.That(FaceTuningCard.Movement(face, FaceTuningPart.Brows), Is.EqualTo(0.5f));
        }

        [UnityTest]
        public IEnumerator CancelingTheFileScreen_RegistersNothing()
        {
            AvatarCard card = _root.Content.Q<AvatarCard>();
            _files.Path = null;

            Submit(card.RegisterButton);
            yield return null;

            Assert.That(_avatars.Imported, Is.Empty);
            Assert.That(_root.Dialogs.Current, Is.Null);
        }

        [UnityTest]
        public IEnumerator Unregistering_AsksFirst_AsADangerousOperation()
        {
            _avatars.Add("a", "A");
            yield return null;
            AvatarCard card = _root.Content.Q<AvatarCard>();

            Submit(card.RemoveButton);
            yield return null;
            Assert.That(_root.Dialogs.Current, Is.Not.Null);
            Submit(_root.Dialogs.Current.Confirm);
            yield return null;
            yield return null;

            Assert.That(_avatars.Removed, Is.EqualTo(new[] { "a" }));
        }

        [UnityTest]
        public IEnumerator ChoosingAnAvatar_SelectsIt_AndTheChipFollowsTheState()
        {
            _avatars.Add("a", "A");
            _avatars.Add("b", "B");
            yield return null;
            AvatarCard card = _root.Content.Q<AvatarCard>();

            card.Choice.value = "b";
            yield return null;

            Assert.That(_avatars.CurrentAvatarId.Value, Is.EqualTo("b"));
            Assert.That(card.Status.Status, Is.EqualTo(StatusKind.Success));
            _avatars.SetState(AvatarState.Error, AvatarProblem.MissingBones);
            Assert.That(card.Status.Status, Is.EqualTo(StatusKind.Error));
            Assert.That(card.ProblemKey, Is.EqualTo(AvatarStrings.ProblemMissingBones));
        }

        [Test]
        public void Describe_PairsShapeColorAndText()
        {
            Assert.That(AvatarCard.Describe(AvatarState.None, AvatarProblem.None), Is.EqualTo((StatusKind.Idle, AvatarStrings.StateNone, (string)null)));
            Assert.That(AvatarCard.Describe(AvatarState.Loading, AvatarProblem.None).Kind, Is.EqualTo(StatusKind.Idle));
            Assert.That(AvatarCard.Describe(AvatarState.Ready, AvatarProblem.LoadFailed), Is.EqualTo((StatusKind.Success, AvatarStrings.StateReady, AvatarStrings.ProblemLoadFailed)), "A failed swap keeps the avatar and says so.");
            Assert.That(AvatarCard.Describe(AvatarState.Error, AvatarProblem.None).ProblemKey, Is.EqualTo(AvatarStrings.ProblemLoadFailed));
        }

        [Test]
        public void Choices_StartWithNone_AndNameUnusableAvatars()
        {
            var usable = new AvatarEntry("a", new AvatarProfile { DisplayName = "A" }, null);
            var broken = new AvatarEntry("b", null, null);
            IReadOnlyList<SelectChoice> choices = AvatarCard.Choices(new[] { usable, broken });

            Assert.That(choices.Select(c => c.Id), Is.EqualTo(new[] { string.Empty, "a", "b" }));
            Assert.That(choices[1].Text, Is.EqualTo("A"));
            Assert.That(choices[2].TextKey, Is.EqualTo(AvatarStrings.Unusable));
        }

        [Test]
        public void LicenseLines_SayNotStated_RatherThanLeavingConditionsOut()
        {
            var license = new AvatarLicense { AvatarPermission = AvatarPermission.Everyone, LegacyLicenseName = "CC_BY", LicenseUrl = "https://example.com/l", OtherLicenseUrl = "file:///C:/x.exe" };
            IReadOnlyList<LicenseLine> lines = AvatarCard.LicenseLines("Model", license);

            Assert.That(lines.First().Text, Is.EqualTo("Model"));
            Assert.That(lines.Single(l => l.NameKey == AvatarStrings.LicenseAuthors).ValueKey, Is.EqualTo(AvatarStrings.ValueNotStated));
            Assert.That(lines.Single(l => l.NameKey == AvatarStrings.LicenseRedistribution).ValueKey, Is.EqualTo(AvatarStrings.ValueNotStated));
            Assert.That(lines.Single(l => l.NameKey == AvatarStrings.LicensePermission).ValueKey, Is.EqualTo(AvatarStrings.ValueEveryone));
            Assert.That(lines.Single(l => l.NameKey == AvatarStrings.LicenseLegacy).Text, Is.EqualTo("CC_BY"));
            Assert.That(lines.Single(l => l.NameKey == AvatarStrings.LicenseUrl).Url, Is.EqualTo("https://example.com/l"));
            LicenseLine other = lines.Single(l => l.NameKey == AvatarStrings.LicenseOtherUrl);
            Assert.That(other.Text, Is.EqualTo("file:///C:/x.exe"), "Shown as written.");
            Assert.That(other.Url, Is.Null, "But never opened.");
        }

        [TestCase("https://example.com/terms", true)]
        [TestCase("http://example.com/terms", false)]
        [TestCase("file:///C:/Windows/notepad.exe", false)]
        [TestCase("javascript:alert(1)", false)]
        [TestCase("example.com", false)]
        [TestCase(null, false)]
        public void OnlyHttpsLinks_AreOpened(string url, bool expected)
        {
            Assert.That(AvatarCard.IsOpenableUrl(url), Is.EqualTo(expected));
        }

        [Test]
        public void FileFilters_AreWin32Pairs()
        {
            string text = FileDialogFilters.ToWin32(new[] { new FileFilter("VRM files", new[] { ".vrm" }), new FileFilter("Both", new[] { ".a", ".b" }) });

            Assert.That(text, Is.EqualTo("VRM files (*.vrm)\0*.vrm\0Both (*.a;*.b)\0*.a;*.b\0\0"));
        }

        private static void Submit(VisualElement element)
        {
            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = element;
                element.SendEvent(evt);
            }
        }

        private sealed class FakeFiles : IFileDialog
        {
            public string Path { get; set; }

            public Task<string> OpenAsync(FileDialogOptions options)
            {
                Assert.That(options.Filters.Single().Extensions, Is.EqualTo(new[] { ".vrm" }));
                return Task.FromResult(Path);
            }

            public Task<string> PickFolderAsync(string title)
            {
                return Task.FromResult<string>(null);
            }
        }

        private sealed class FakeLighting : IStageLighting
        {
            private readonly ObservableValue<float> _intensity = new ObservableValue<float>(StageLightIntensity.Default);

            public IReadOnlyObservable<float> Intensity => _intensity;

            public void SetIntensity(float intensity)
            {
                _intensity.Value = StageLightIntensity.Clamp(intensity);
            }
        }

        internal sealed class FakeAvatars : IAvatarService
        {
            private readonly ObservableValue<IReadOnlyList<AvatarEntry>> _avatars = new ObservableValue<IReadOnlyList<AvatarEntry>>(Array.Empty<AvatarEntry>());
            private readonly ObservableValue<string> _current = new ObservableValue<string>(null);
            private readonly ObservableValue<AvatarState> _state = new ObservableValue<AvatarState>(AvatarState.None);
            private readonly ObservableValue<AvatarProblem> _problem = new ObservableValue<AvatarProblem>(AvatarProblem.None);

            public List<string> Imported { get; } = new List<string>();

            public List<string> Removed { get; } = new List<string>();

            public IReadOnlyObservable<IReadOnlyList<AvatarEntry>> Avatars => _avatars;

            public IReadOnlyObservable<string> CurrentAvatarId => _current;

            public IReadOnlyObservable<AvatarState> State => _state;

            public IReadOnlyObservable<AvatarProblem> Problem => _problem;

            public IReadOnlyList<string> Extensions { get; } = new[] { ".vrm" };

            private readonly ObservableValue<FaceTuning> _tuning = new ObservableValue<FaceTuning>(new FaceTuning());

            public IReadOnlyObservable<FaceTuning> FaceTuning => _tuning;

            public FaceTarget Face { get; set; }

            public BodyTarget Body { get; set; }

            public void SetFaceTuning(FaceTuning tuning)
            {
                _tuning.Value = tuning;
            }

            public void Add(string id, string name)
            {
                var license = new AvatarLicense { Authors = { "Author " + name }, AvatarPermission = AvatarPermission.Everyone };
                _avatars.Value = _avatars.Value.Append(new AvatarEntry(id, new AvatarProfile { AvatarId = id, DisplayName = name, Format = "vrm", FormatVersion = "1.0", License = license }, null)).ToList();
                if (_current.Value == null)
                {
                    _current.Value = id;
                    _state.Value = AvatarState.Ready;
                }
            }

            public void SetState(AvatarState state, AvatarProblem problem)
            {
                _problem.Value = problem;
                _state.Value = state;
            }

            public async Task<AvatarImportResult> ImportAsync(string path, Func<AvatarModelInfo, Task<bool>> confirm, CancellationToken cancellationToken)
            {
                var license = new AvatarLicense { Authors = { "Author A" }, AvatarPermission = AvatarPermission.OnlyAuthor };
                if (!await confirm(new AvatarModelInfo("A", "vrm", "1.0", false, license, null)))
                {
                    return AvatarImportResult.UserDeclined();
                }

                Imported.Add(path);
                Add("a", "A");
                return AvatarImportResult.Succeeded(_avatars.Value.Last());
            }

            public Task SelectAsync(string avatarId, CancellationToken cancellationToken)
            {
                _current.Value = avatarId;
                _state.Value = avatarId == null ? AvatarState.None : AvatarState.Ready;
                return Task.CompletedTask;
            }

            public Task RemoveAsync(string avatarId, CancellationToken cancellationToken)
            {
                Removed.Add(avatarId);
                _avatars.Value = _avatars.Value.Where(e => e.Id != avatarId).ToList();
                _current.Value = null;
                _state.Value = AvatarState.None;
                return Task.CompletedTask;
            }
        }
    }
}
