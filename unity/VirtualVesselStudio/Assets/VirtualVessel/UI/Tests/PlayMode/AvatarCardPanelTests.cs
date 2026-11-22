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
using VirtualVessel.Core.Geometry;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Frames;
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
            Build(sample: null);
            yield return null;
        }

        private void Build(string sample)
        {
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
            }

            _host = new GameObject(nameof(AvatarCardPanelTests));
            _host.AddComponent<UIDocument>();
            _root = _host.AddComponent<UiRoot>();
            _root.SetAssets(UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset"));
            _root.Build(new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false, screens: new ScreenServices { Avatars = _avatars, FileDialog = _files, SampleAvatar = sample, Lighting = _lighting }));
            _root.Shell.Open("setup");
            ((SetupScreen)_root.Shell.Host.GetScreen("setup")).Tabs.value = SetupScreen.AvatarPage;
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
        public IEnumerator FingerTuning_SlidersTuneIt_AndResetBringsTheDefaultsBack()
        {
            FaceTuningCard card = _root.Content.Q<FaceTuningCard>();
            yield return null;
            Assert.That((card.Fingers.value, card.Thumb.value), Is.EqualTo((100f, 100f)));

            card.Fingers.value = 150f;
            card.Thumb.value = 70f;
            Assert.That(_avatars.FingerTuning.Value.Bend, Is.EqualTo(1.5f));
            Assert.That(_avatars.FingerTuning.Value.Thumb, Is.EqualTo(0.7f).Within(0.001f));

            _avatars.SetFingerTuning(new FingerTuning { Bend = 1.2f });
            Assert.That(card.Fingers.value, Is.EqualTo(120f).Within(0.01f), "The slider follows the service.");

            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = card.ResetButton;
                card.ResetButton.SendEvent(submit);
            }

            yield return null;
            Assert.That(_avatars.FingerTuning.Value, Is.EqualTo(new FingerTuning()));
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

        [Test]
        public void ThePicture_LooksAtTheAvatarFrame_WhereverAndHoweverLargeItIs()
        {
            // The avatar in the stream's bottom right quarter; values exact in binary.
            var area = new Rect(0.5f, 0f, 0.5f, 0.5f);
            Assert.That(FaceTuningCard.Crop(100f, 100f, area), Is.EqualTo(new Rect(0.5f, 0f, 0.5f, 0.5f)), "100% shows the frame's whole height, about its middle.");
            Assert.That(FaceTuningCard.Crop(100f, 200f, area), Is.EqualTo(new Rect(0.625f, 0.25f, 0.25f, 0.25f)), "The top of the frame, enlarged against its height.");
            Assert.That(FaceTuningCard.Crop(0f, 200f, area), Is.EqualTo(new Rect(0.625f, 0f, 0.25f, 0.25f)), "The bottom of the frame.");
            Assert.That(FaceTuningCard.Crop(100f, 200f, new Rect(0.75f, 0f, 0.25f, 1f)), Is.EqualTo(new Rect(0.5f, 0.5f, 0.5f, 0.5f)), "Kept within the stream at its edge.");
            Assert.That(FaceTuningCard.Crop(100f, 200f, new Rect(0f, 0f, 1f, 1f)), Is.EqualTo(FaceTuningCard.Crop(100f, 200f)), "A full-stream frame looks as without one.");

            var frame = new AvatarFrameStub(new NormalizedRect(0.5, 0.5, 0.5, 0.5));
            Assert.That(FaceTuningCard.AreaOf(frame), Is.EqualTo(area), "The placement, from the top left, as UV from the bottom left.");
            frame.Shown.Value = false;
            Assert.That(FaceTuningCard.AreaOf(frame), Is.Null, "Without the avatar, the whole stream.");
            Assert.That(FaceTuningCard.AreaOf(null), Is.Null);
        }

        /// <summary>Stands in for the avatar's frame (Stage detailed design 7.2).</summary>
        private sealed class AvatarFrameStub : IFrameSource
        {
            private readonly ObservableValue<FrameLayout> _layout;
            private readonly ObservableValue<string> _name = new ObservableValue<string>(string.Empty);

            public AvatarFrameStub(NormalizedRect placement)
            {
                _layout = new ObservableValue<FrameLayout>(FrameLayout.Create(placement));
            }

            public ObservableValue<bool> Shown { get; } = new ObservableValue<bool>(true);

            public string Id => "avatar";

            public string NameKey => null;

            public IReadOnlyObservable<string> DisplayName => _name;

            public IReadOnlyObservable<bool> Visible => Shown;

            public double SourceAspect => 1.0;

            public Texture Video => null;

            public IReadOnlyObservable<FrameLayout> Layout => _layout;

            public void PreviewLayout(FrameLayout layout)
            {
            }

            public Task CommitLayoutAsync(FrameLayout layout, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }

        [UnityTest]
        public IEnumerator TheLightingCard_ChangesTheChosenLight_AndFollowsTheStage()
        {
            LightingCard card = _root.Content.Q<LightingCard>();
            Assert.That(card, Is.Not.Null, "On the avatar page, with the stage.");
            yield return null;
            Assert.That(card.Intensity.value, Is.EqualTo(100f), "100% is the light as made.");
            Assert.That(card.Kind.value, Is.EqualTo(0), "The whole stage, as the light always was.");
            Assert.That(card.Distance.enabledSelf, Is.False, "A whole-stage light has no distance.");
            Assert.That(card.Query<SettingRow>().ToList().Where(row => string.IsNullOrEmpty(row.HelpKey)), Is.Empty, "Every row is explained.");
            Assert.That(card.Query<ValueSlider>().ToList().Where(slider => string.IsNullOrEmpty(slider.HelpKey)), Is.Empty, "Every slider too.");

            // Measured on the real styles: the settings scroll beside the picture, so the avatar stays in view
            // however long the list is (user's report, 2026-11-22).
            Rect picture = card.Preview.worldBound;
            Rect settings = card.Settings.worldBound;
            Assert.That(settings.xMin >= picture.xMax - 0.5f || settings.yMin >= picture.yMax - 0.5f, Is.True, "Beside the picture, or under it on a narrow window.");
            Assert.That(settings.height, Is.LessThanOrEqualTo(480.5f), "Scrolled, not as long as the list.");
            Assert.That(card.Settings.contentContainer.worldBound.height, Is.GreaterThan(settings.height), "The list is longer than what shows.");

            card.Intensity.value = 60f;
            Assert.That(_lighting.Lighting.Value.Lights[0].Intensity, Is.EqualTo(0.6f).Within(1e-5f));
            card.Kind.value = 1;
            Assert.That(_lighting.Lighting.Value.Lights[0].Kind, Is.EqualTo(StageLightKind.Spot));
            Assert.That(card.Distance.enabledSelf, Is.True);
            card.Yaw.value = 40f;
            Assert.That(_lighting.Lighting.Value.Lights[0].Yaw, Is.EqualTo(40f));

            // A stage color has no temperature.
            Assert.That(card.Temperature.enabledSelf, Is.True);
            card.ColorChoice.value = StageLightColors.Pink;
            Assert.That(_lighting.Lighting.Value.Lights[0].ColorId, Is.EqualTo(StageLightColors.Pink));
            Assert.That(card.Temperature.enabledSelf, Is.False);
            Assert.That(card.Temperature.TooltipKey, Is.EqualTo(LightingStrings.WhiteOnly), "Says why.");
            Assert.That(card.Swatch.style.backgroundColor.value, Is.EqualTo(StageLightColors.ColorOf(_lighting.Lighting.Value.Lights[0])));

            _lighting.SetLighting(_lighting.Lighting.Value.WithAmbient(0.4f).WithLight(0, StageLight.Default.WithIntensity(1.2f)));
            Assert.That(card.Intensity.value, Is.EqualTo(120f).Within(1e-3f), "Follows a change made elsewhere.");
            Assert.That(card.Ambient.value, Is.EqualTo(40f).Within(1e-3f));
            Assert.That(card.Kind.value, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator ACustomColor_IsSetByItsCode_OrByItsChannels()
        {
            LightingCard card = _root.Content.Q<LightingCard>();
            yield return null;
            Assert.That(card.CodeField.enabledSelf, Is.False, "Only for a custom color.");
            Assert.That(card.CodeField.value, Is.EqualTo(StageLightColors.ToCode(StageLightColors.ColorOf(StageLight.Default))), "A named color's code can be read.");

            card.ColorChoice.value = StageLightColors.Pink;
            card.ColorChoice.value = StageLightColors.Custom;
            Assert.That(_lighting.Lighting.Value.Lights[0].ColorId, Is.EqualTo(StageLightColors.ToCode(StageLightColors.ColorOf(StageLight.Default.WithColor(StageLightColors.Pink)))), "Starts from the color it had.");
            Assert.That(card.CodeField.enabledSelf, Is.True);
            Assert.That(card.Temperature.enabledSelf, Is.False);

            card.CodeField.value = "#nope";
            PressEnter(card.CodeField);

            yield return null;
            Assert.That(card.CodeField.ErrorKey, Is.EqualTo(LightingStrings.CodeInvalid));
            card.CodeField.value = "#00ff80";
            PressEnter(card.CodeField);

            yield return null;
            Assert.That(card.CodeField.ErrorKey, Is.Null);
            Assert.That(_lighting.Lighting.Value.Lights[0].ColorId, Is.EqualTo("#00FF80"));
            Assert.That(card.GreenSlider.value, Is.EqualTo(255f));

            card.RedSlider.value = 16f;
            Assert.That(_lighting.Lighting.Value.Lights[0].ColorId, Is.EqualTo("#10FF80"));
            Assert.That(LightingCard.WithChannel(new Color(1f, 0f, 0f), 2, 255), Is.EqualTo("#FF00FF"));
        }

        private static void PressEnter(VisualElement target)
        {
            using KeyDownEvent enter = KeyDownEvent.GetPooled('\n', KeyCode.Return, EventModifiers.None);
            enter.target = target;
            target.SendEvent(enter);
        }

        [UnityTest]
        public IEnumerator Lights_AreAddedUpToFour_AndTheLastOneStays()
        {
            LightingCard card = _root.Content.Q<LightingCard>();
            yield return null;
            Assert.That(card.RemoveButton.enabledSelf, Is.False, "Never without a light.");
            Assert.That(card.LightChoice.IsItemEnabled(1), Is.False);

            Submit(card.AddButton);
            Assert.That(_lighting.Lighting.Value.Lights.Count, Is.EqualTo(2));
            Assert.That(card.Selected, Is.EqualTo(1), "The new light is the one being set.");
            Assert.That(card.Kind.value, Is.EqualTo(1), "Added as a spot.");
            card.Intensity.value = 50f;
            Assert.That(_lighting.Lighting.Value.Lights[1].Intensity, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(_lighting.Lighting.Value.Lights[0].Intensity, Is.EqualTo(1f), "The first is left as it was.");

            Submit(card.AddButton);
            Submit(card.AddButton);
            Assert.That(_lighting.Lighting.Value.Lights.Count, Is.EqualTo(StageLighting.MaxLights));
            Assert.That(card.AddButton.enabledSelf, Is.False);
            Assert.That(card.AddButton.TooltipKey, Is.EqualTo(LightingStrings.AddFull));

            Submit(card.RemoveButton);
            Assert.That(_lighting.Lighting.Value.Lights.Count, Is.EqualTo(3));
            Assert.That(card.Selected, Is.EqualTo(2), "The light before the removed one.");

            card.LightChoice.value = 0;
            Submit(card.ResetButton);
            Assert.That(_lighting.Lighting.Value, Is.EqualTo(StageLighting.Default));
            Assert.That(card.Selected, Is.EqualTo(0));
        }

        [Test]
        public void TheChosenLight_IsTheLastOne_WhenItIsGone()
        {
            StageLighting two = StageLighting.Default.WithAdded(StageLight.Added);
            Assert.That(LightingCard.SelectedIn(two, 1), Is.EqualTo(1));
            Assert.That(LightingCard.SelectedIn(two, 3), Is.EqualTo(1));
            Assert.That(LightingCard.SelectedIn(StageLighting.Default, 1), Is.EqualTo(0));
            Assert.That(LightingCard.SelectedIn(StageLighting.Default, -1), Is.EqualTo(0));
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
        public IEnumerator TheSample_IsRegisteredWithoutTheFileScreen_AfterItsTermsAreShown()
        {
            Assert.That(_root.Content.Q<AvatarCard>().SampleButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "Hidden without a sample.");

            Build(sample: "C:\\app\\Samples\\otoha-chan.vrm");
            yield return null;
            AvatarCard card = _root.Content.Q<AvatarCard>();
            Assert.That(card.SampleButton.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));

            // Measured on the real styles: one above the other, both inside the Card (user's report, 2026-11-22).
            Rect register = card.RegisterButton.worldBound;
            Rect sampleButton = card.SampleButton.worldBound;
            Rect body = card.Q<Card>().worldBound;
            Assert.That(sampleButton.yMin - register.yMax, Is.EqualTo(8f).Within(0.5f), "Under the register button, S (8px) apart.");
            Assert.That(sampleButton.xMax, Is.LessThanOrEqualTo(body.xMax + 0.5f), "Not past the Card.");
            Assert.That(sampleButton.xMax, Is.EqualTo(register.xMax).Within(0.5f), "Right-aligned like a row's control.");

            Submit(card.SampleButton);
            yield return null;
            yield return null;
            ConfirmDialog dialog = _root.Dialogs.Current;
            Assert.That(dialog, Is.Not.Null, "Its terms are asked about as for any model.");
            Submit(dialog.Confirm);
            yield return null;
            yield return null;

            Assert.That(_avatars.Imported, Is.EqualTo(new[] { "C:\\app\\Samples\\otoha-chan.vrm" }));
            Assert.That(_files.Opened, Is.Zero, "No file screen.");
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

            public int Opened { get; private set; }

            public Task<string> OpenAsync(FileDialogOptions options)
            {
                Opened++;
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
            private readonly ObservableValue<StageLighting> _lighting = new ObservableValue<StageLighting>(StageLighting.Default);

            public IReadOnlyObservable<StageLighting> Lighting => _lighting;

            public void SetLighting(StageLighting lighting)
            {
                _lighting.Value = lighting.Normalized();
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

            public IReadOnlyObservable<float?> Height { get; } = new ObservableValue<float?>(null);

            public IReadOnlyList<string> Extensions { get; } = new[] { ".vrm" };

            private readonly ObservableValue<FaceTuning> _tuning = new ObservableValue<FaceTuning>(new FaceTuning());

            private readonly ObservableValue<FingerTuning> _fingerTuning = new ObservableValue<FingerTuning>(new FingerTuning());

            public IReadOnlyObservable<FaceTuning> FaceTuning => _tuning;

            public FaceTarget Face { get; set; }

            public BodyTarget Body { get; set; }

            public void SetFaceTuning(FaceTuning tuning)
            {
                _tuning.Value = tuning;
            }

            public IReadOnlyObservable<FingerTuning> FingerTuning => _fingerTuning;

            public void SetFingerTuning(FingerTuning tuning)
            {
                _fingerTuning.Value = tuning;
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
