using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Core.Time;
using VirtualVessel.UI.Notifications;
using VirtualVessel.UI.Settings;
using VirtualVessel.UI.Theme;
using Object = UnityEngine.Object;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The UI root on a real runtime panel, with the real UiAssets (UI foundation detailed design 7.9).</summary>
    public sealed class UiRootPanelTests
    {
        private string _directory;
        private GameObject _host;
        private UiRoot _root;
        private UiAssets _assets;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _assets = UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset");
            _host = new GameObject(nameof(UiRootPanelTests));
            _host.AddComponent<UIDocument>();
            _root = _host.AddComponent<UiRoot>();
            _root.SetAssets(_assets);
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
        public IEnumerator Build_WithoutFile_UsesDefaults_OnACopyOfThePanelSettings()
        {
            _root.Build(Context());
            yield return null;

            Assert.That(_root.IsBuilt, Is.True);
            Assert.That(_root.Panel, Is.Not.SameAs(_assets.PanelSettings), "The asset is never changed at run time.");
            Assert.That(_host.GetComponent<UIDocument>().panelSettings, Is.SameAs(_root.Panel));
            Assert.That(_root.Panel.scaleMode, Is.EqualTo(PanelScaleMode.ConstantPhysicalSize), "Follows the Windows display scale.");
            Assert.That(_root.Panel.scale, Is.EqualTo(UiPanelScale.PanelScale(1f, Screen.dpi, Screen.width, Screen.height)).Within(1e-5f), "The UI size, on the display scale fitted to the window.");
            Assert.That(_root.ThemeService.Current, Is.EqualTo(ThemeSettings.Default));
            Assert.That(_root.FontService.Current, Is.EqualTo(FontService.DefaultFontSet));
            Assert.That(_root.Content, Is.Not.Null);
            Assert.That(_root.Content.panel, Is.Not.Null);
            Assert.That(_root.Content.parent.ClassListContains(ThemeService.RootClass), Is.True);
            Assert.That(_root.Content.parent.Q(className: NotificationService.StackClass), Is.Not.Null, "Notifications are attached.");

            var fallbacks = _root.Panel.textSettings.fallbackFontAssets;
            Assert.That(fallbacks.Any(font => font != null && font.HasCharacter('あ', searchFallbacks: false, tryAddCharacter: true)), Is.True, "Japanese glyphs fall back to Noto Sans JP.");
            Assert.That(File.Exists(Path.Combine(_directory, UiSettings.FileName)), Is.False, "Nothing is written without a change.");
        }

        [UnityTest]
        public IEnumerator Build_AppliesTheSavedSettings()
        {
            WriteSettings("{ \"schemaVersion\": 1, \"language\": \"en\", \"palette\": \"mint\", \"background\": \"light\", \"uiScale\": 1.25, \"reducedMotion\": \"on\" }");

            _root.Build(Context());
            yield return null;

            Assert.That(_root.Localizer.Locale.Code, Is.EqualTo("en"));
            Assert.That(_root.ThemeService.Current, Is.EqualTo(new ThemeSettings("mint", ThemeBackground.Light, reducedMotion: true)));
            Assert.That(_root.Panel.scale, Is.EqualTo(UiPanelScale.PanelScale(1.25f, Screen.dpi, Screen.width, Screen.height)).Within(1e-5f), "The UI size, on the display scale fitted to the window.");
            Assert.That(_assets.PanelSettings.scale, Is.EqualTo(1f), "The asset keeps its value.");
        }

        [UnityTest]
        public IEnumerator Changes_ApplyAtOnce_AndAreSaved()
        {
            _root.Build(Context());
            yield return null;

            _root.Settings.SetUiScale(1.5f);
            _root.Settings.SetLanguage("en");
            _root.Settings.SetPalette("sky");
            Assert.That(_root.Panel.scale, Is.EqualTo(UiPanelScale.PanelScale(1.5f, Screen.dpi, Screen.width, Screen.height)).Within(1e-5f), "The UI size, on the display scale fitted to the window.");
            Assert.That(_root.Localizer.Locale.Code, Is.EqualTo("en"));
            Assert.That(_root.ThemeService.Current.Palette, Is.EqualTo("sky"));

            var flush = _root.FlushSettingsAsync();
            yield return new WaitUntil(() => flush.IsCompleted);
            string text = File.ReadAllText(Path.Combine(_directory, UiSettings.FileName));
            Assert.That(text, Does.Contain("\"uiScale\": 1.5"));
            Assert.That(text, Does.Contain("\"language\": \"en\""));
        }

        [UnityTest]
        public IEnumerator BrokenFile_IsMovedAside_AndTheUserIsTold()
        {
            WriteSettings("{ broken");

            _root.Build(Context());
            yield return null;

            Assert.That(_root.Notifications.Visible, Has.Count.EqualTo(1));
            Notice notice = _root.Notifications.Visible[0];
            Assert.That(notice.Kind, Is.EqualTo(NoticeKind.Warning));
            Assert.That(notice.Title.text, Is.EqualTo(_root.Localizer.Get(UiRootStrings.SettingsRecoveredTitle)));
            Assert.That(Directory.GetFiles(_directory, "ui-settings.broken-*"), Has.Length.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Teardown_DestroysTheCopies()
        {
            _root.Build(Context());
            yield return null;
            PanelSettings copy = _root.Panel;
            var textSettings = copy.textSettings;

            _root.Teardown();
            yield return null;

            Assert.That(_root.IsBuilt, Is.False);
            Assert.That(copy == null, Is.True, "The panel settings copy is destroyed.");
            Assert.That(textSettings == null, Is.True, "The text settings copy is destroyed.");
            Assert.That(_assets.PanelSettings != null, Is.True, "The asset itself stays.");
        }

        [UnityTest]
        public IEnumerator StartupProblem_DataRoot_ShowsThePathAndQuits()
        {
            int quits = 0;
            _root.ShowStartupProblem(StartupProblemKind.DataRootUnavailable, @"D:\VirtualVesselStudioData", _directory, () => quits++);
            yield return null;

            StartupProblemView view = _root.StartupProblem;
            Assert.That(view.panel, Is.Not.Null);
            Assert.That(view.Title.text, Is.EqualTo(_root.Localizer.Get(UiRootStrings.DataRootTitle)));
            Assert.That(view.Body.text, Is.EqualTo(_root.Localizer.Get(UiRootStrings.DataRootBody)));
            Assert.That(view.PathLabel.text, Is.EqualTo(@"D:\VirtualVesselStudioData"));
            Assert.That(view.focusController.focusedElement, Is.SameAs(view.QuitButton), "Enter quits right away.");

            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = view.QuitButton;
                view.QuitButton.SendEvent(submit);
            }

            Assert.That(quits, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator StartupProblem_Other_ShowsNoInternals()
        {
            _root.ShowStartupProblem(StartupProblemKind.StartupFailed, @"D:\ignored", _directory, () => { });
            yield return null;

            StartupProblemView view = _root.StartupProblem;
            Assert.That(view.Title.text, Is.EqualTo(_root.Localizer.Get(UiRootStrings.FailedTitle)));
            Assert.That(view.PathLabel, Is.Null, "The path is only for the Data Root problem.");
            Assert.That(_root.IsBuilt, Is.False, "No settings are read for this screen.");
        }

        [UnityTest]
        public IEnumerator StartupProblem_UsesTheSavedLanguage()
        {
            WriteSettings("{ \"schemaVersion\": 1, \"language\": \"en\" }");

            _root.ShowStartupProblem(StartupProblemKind.StartupFailed, null, _directory, () => { });
            yield return null;

            Assert.That(_root.Localizer.Locale.Code, Is.EqualTo("en"));
            Assert.That(_root.StartupProblem.Title.text, Is.EqualTo("Virtual Vessel Studio couldn't start"));
        }

        [UnityTest]
        public IEnumerator StartupProblem_AfterTheUi_KeepsItsLanguage()
        {
            WriteSettings("{ \"schemaVersion\": 1, \"language\": \"ja\" }");
            _root.Build(Context());
            yield return null;
            _root.Settings.SetLanguage("en");

            _root.ShowStartupProblem(StartupProblemKind.StartupFailed, null, _directory, () => { });
            yield return null;

            Assert.That(_root.Localizer.Locale.Code, Is.EqualTo("en"), "The language just shown wins over the saved one.");
        }

        [UnityTest]
        public IEnumerator StartupProblem_BrokenSettings_AreLeftAlone()
        {
            WriteSettings("{ broken");

            _root.ShowStartupProblem(StartupProblemKind.StartupFailed, null, _directory, () => { });
            yield return null;

            Assert.That(_root.StartupProblem.panel, Is.Not.Null, "Shown in the OS language.");
            Assert.That(File.ReadAllText(Path.Combine(_directory, UiSettings.FileName)), Is.EqualTo("{ broken"));
            Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1), "Nothing is moved aside or written.");
        }

        [UnityTest]
        public IEnumerator ProblemScreen_AfterTheUiWasTornDown_Renders()
        {
            // As after a build that failed part way: the next panel must not touch what the first one left.
            _root.Build(Context());
            yield return null;
            _root.Teardown();
            yield return null;

            _root.ShowStartupProblem(StartupProblemKind.StartupFailed, null, _directory, () => { });
            yield return null;
            yield return null;

            Assert.That(_root.StartupProblem.panel, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator Build_AfterTheProblemScreen_IsRejected()
        {
            _root.ShowStartupProblem(StartupProblemKind.StartupFailed, null, _directory, () => { });
            yield return null;

            Assert.Throws<InvalidOperationException>(() => _root.Build(Context()));
        }

        private UiRootContext Context()
        {
            return new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, systemPrefersReducedMotion: false);
        }

        private void WriteSettings(string text)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, UiSettings.FileName), text);
        }
    }
}
