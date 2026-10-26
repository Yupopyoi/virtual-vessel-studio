using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Screens;
using VirtualVessel.UI.Settings;
using VirtualVessel.UI.Theme;
using Object = UnityEngine.Object;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The navigation and screens on a real runtime panel (UI foundation detailed design 7.7).</summary>
    public sealed class AppShellPanelTests
    {
        private GameObject _host;
        private PanelSettings _panelSettings;
        private UiServicesScope _scope;
        private Dictionary<string, int> _created;
        private RecordingScreen _recording;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _created = new Dictionary<string, int>();
            _recording = new RecordingScreen();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _host = new GameObject(nameof(AppShellPanelTests));
            UIDocument document = _host.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            var localizer = new Localizer(new[] { new StringTable(new LocaleInfo("ja", "日本語", null), new Dictionary<string, string>()) }, "ja", null);
            _scope = new UiServicesScope(new UiServices(localizer, null, null));
            _scope.AddToClassList("vv-root");
            document.rootVisualElement.Add(_scope);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_host);
            Object.Destroy(_panelSettings);
        }

        [UnityTest]
        public IEnumerator ATextField_KeepsItsCaret_WhenEnterIsPressed()
        {
            // Unity's field moved the caret to the start on Enter, which Japanese input uses to commit a
            // word, so what was typed next went in near the start (the user's report).
            var input = new TextInput();
            _scope.Add(input);
            yield return null;
            TextField field = input.Q<TextField>();
            field.Focus();
            input.SetValueWithoutNotify("じゃんけん");
            field.textSelection.cursorIndex = 5;
            field.textSelection.selectIndex = 5;
            yield return null;

            foreach (KeyCode key in new[] { KeyCode.Return, KeyCode.KeypadEnter })
            {
                using (KeyDownEvent down = KeyDownEvent.GetPooled('\n', key, EventModifiers.None))
                {
                    down.target = field.Q(className: "unity-text-element") ?? field;
                    field.SendEvent(down);
                }

                yield return null;
                Assert.That(field.textSelection.cursorIndex, Is.EqualTo(5), $"{key} leaves the caret at the end.");
                Assert.That(field.value, Is.EqualTo("じゃんけん"));
            }

            Assert.That(TextInput.IsEnter(KeyCode.None, '\n'), Is.True, "The line break it types.");
            Assert.That(TextInput.IsEnter(KeyCode.A, 'a'), Is.False);
        }

        [UnityTest]
        public IEnumerator ATextField_TakesEnglishOnly_AndSaysSo()
        {
            // Japanese input left the caret short of each committed word (the user's video, 2026-10-25).
            var input = new TextInput();
            _scope.Add(input);
            var changes = new List<string>();
            input.RegisterValueChangedCallback(evt => changes.Add(evt.newValue));
            yield return null;
            TextField field = input.Q<TextField>();
            input.SetValueWithoutNotify("記録");
            field.value = "記録 v2";
            field.value = "記録 v2です";
            Assert.That(input.value, Is.EqualTo("記録 v2"), "What was there stays; Japanese typed in is dropped.");
            Assert.That(changes, Is.EqualTo(new[] { "記録 v2" }), "A dropped edit is no change.");
            Assert.That(field.textSelection.cursorIndex, Is.EqualTo(5));
            Label hint = input.Q<Label>(className: "vv-field__hint");
            Assert.That(hint.text, Does.Contain(CommonStrings.EnglishOnly), "The field says it takes English only (the key, with this test's empty table).");
        }

        [UnityTest]
        public IEnumerator Screens_AreCreatedOnFirstOpen_KeptAndToldWhenShownOrHidden()
        {
            AppShell shell = Shell();
            var opened = new List<string>();
            shell.Opened += opened.Add;
            yield return null;

            shell.Open("live");
            Assert.That(shell.Host.IsCreated("setup"), Is.False, "Not created before it opens.");
            Click(shell.Items[1]);
            shell.Open("live");
            Click(shell.Items[1]);

            Assert.That(_created["setup"], Is.EqualTo(1), "Kept after the first open.");
            Assert.That(_recording.Log, Is.EqualTo(new[] { "shown", "hidden", "shown" }));
            Assert.That(shell.Host.GetPage("live").style.display, Is.EqualTo((StyleEnum<DisplayStyle>)DisplayStyle.None), "Hidden screens are kept with display: none.");
            Assert.That(shell.Items[1].Selected, Is.True);
            Assert.That(shell.Items[0].Selected, Is.False);
            Assert.That(opened, Is.EqualTo(new[] { "live", "setup", "live", "setup" }));
        }

        [UnityTest]
        public IEnumerator DeveloperMode_ShowsDiagnostics_AndTurningItOffLeavesIt()
        {
            AppShell shell = Shell();
            shell.Open("live");
            yield return null;
            Assert.That(shell.Items[3].style.display, Is.EqualTo((StyleEnum<DisplayStyle>)DisplayStyle.None));

            shell.Open("diagnostics");
            Assert.That(shell.Host.Current, Is.EqualTo("live"), "Hidden screens do not open.");

            shell.DeveloperMode = true;
            Assert.That(shell.Items[3].style.display, Is.EqualTo((StyleEnum<DisplayStyle>)DisplayStyle.Flex));
            shell.Open("diagnostics");
            Assert.That(shell.Host.Current, Is.EqualTo("diagnostics"));

            shell.DeveloperMode = false;
            Assert.That(shell.Host.Current, Is.EqualTo("live"), "Turning Developer Mode off leaves Diagnostics.");
            Assert.That(shell.Items[0].Selected, Is.True);
        }

        [UnityTest]
        public IEnumerator FailingScreen_ShowsAMessage_AndTheOthersStillWork()
        {
            LogAssert.ignoreFailingMessages = true;
            var screens = new[]
            {
                Screen("live"),
                Screen("lab", create: () => throw new InvalidOperationException("broken screen")),
            };
            var shell = new AppShell(screens, null, null);
            _scope.Add(shell);
            yield return null;

            shell.Open("lab");
            Assert.That(shell.Host.GetScreen("lab"), Is.Null);
            Assert.That(shell.Host.GetPage("lab").Q(className: ScreenHost.FailedClass), Is.Not.Null);

            shell.Open("live");
            Assert.That(shell.Host.Current, Is.EqualTo("live"));
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        public IEnumerator UpAndDownKeys_MoveBetweenVisibleItems()
        {
            AppShell shell = Shell();
            shell.Open("live");
            yield return null;

            shell.Items[0].Focus();
            Key(shell.Items[0], KeyCode.DownArrow);
            Assert.That(shell.Items[0].focusController.focusedElement, Is.SameAs(shell.Items[1]));
            Key(shell.Items[1], KeyCode.UpArrow);
            Key(shell.Items[0], KeyCode.UpArrow);
            Assert.That(shell.Items[0].focusController.focusedElement, Is.SameAs(shell.Items[0]), "Stays at the top.");

            shell.Items[2].Focus();
            Key(shell.Items[2], KeyCode.DownArrow);
            Assert.That(shell.Items[0].focusController.focusedElement, Is.SameAs(shell.Items[2]), "The hidden Diagnostics item is skipped.");
        }

        private AppShell Shell()
        {
            var screens = new[]
            {
                Counted("live", () => new VisualElement()),
                Counted("setup", () => _recording),
                Counted("lab", () => new VisualElement()),
                Counted("diagnostics", () => new VisualElement(), developerOnly: true),
            };
            var shell = new AppShell(screens, "0.1.0", null);
            _scope.Add(shell);
            return shell;
        }

        private ScreenDefinition Counted(string id, Func<VisualElement> create, bool developerOnly = false)
        {
            return Screen(id, developerOnly, () =>
            {
                _created[id] = _created.TryGetValue(id, out int count) ? count + 1 : 1;
                return create();
            });
        }

        private static ScreenDefinition Screen(string id, bool developerOnly = false, Func<VisualElement> create = null)
        {
            return new ScreenDefinition(id, IconName.Live, "nav." + id, "tip.nav." + id, create ?? (() => new VisualElement()), developerOnly);
        }

        private static void Click(VisualElement target)
        {
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = target;
                target.SendEvent(submit);
            }
        }

        private static void Key(VisualElement target, KeyCode key)
        {
            using (NavigationMoveEvent evt = NavigationMoveEvent.GetPooled(key == KeyCode.UpArrow ? NavigationMoveEvent.Direction.Up : NavigationMoveEvent.Direction.Down))
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        private sealed class RecordingScreen : VisualElement, IScreen
        {
            public List<string> Log { get; } = new List<string>();

            public void OnShown()
            {
                Log.Add("shown");
            }

            public void OnHidden()
            {
                Log.Add("hidden");
            }
        }
    }

    /// <summary>The shell inside the real UI root: settings, Developer Mode, and Diagnostics.</summary>
    public sealed class UiRootScreensPanelTests
    {
        private string _directory;
        private GameObject _host;
        private UiRoot _root;
        private PerformanceMetricsService _metrics;
        private List<bool> _detailed;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _host = new GameObject(nameof(UiRootScreensPanelTests));
            _host.AddComponent<UIDocument>();
            _root = _host.AddComponent<UiRoot>();
            _root.SetAssets(UnityEditor.AssetDatabase.LoadAssetAtPath<UiAssets>("Assets/VirtualVessel/UI/UiAssets.asset"));
            _metrics = new PerformanceMetricsService(new PerformanceSettings { DetailedEnabled = true, BuiltinMetricsEnabled = false }, new StopwatchMonotonicClock(), new UtcSystemClock(), null);
            _detailed = new List<bool>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_host);
            _metrics.Dispose();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [UnityTest]
        public IEnumerator Build_ShowsTheNavigation_OpensLive_AndTellsDeveloperMode()
        {
            Build();
            yield return null;

            AppShell shell = _root.Shell;
            Assert.That(shell.Items.Select(item => item.TextKey), Is.EqualTo(new[] { ScreenStrings.Live, ScreenStrings.Setup, ScreenStrings.Lab, ScreenStrings.Diagnostics, GalleryStrings.Nav }));
            Assert.That(shell.Host.Current, Is.EqualTo("live"));
            Assert.That(shell.Version.text, Is.EqualTo("v0.1.0-test"));
            Assert.That(shell.Items[3].style.display, Is.EqualTo((StyleEnum<DisplayStyle>)DisplayStyle.None));
            Assert.That(shell.Items[4].style.display, Is.EqualTo((StyleEnum<DisplayStyle>)DisplayStyle.None), "The gallery is for Developer Mode too.");
            Assert.That(_detailed, Is.EqualTo(new[] { false }), "Detailed metrics follow Developer Mode from the start.");
        }

        [UnityTest]
        public IEnumerator OpeningAScreen_IsSaved_AndOpensAtTheNextBuild()
        {
            Build();
            yield return null;

            _root.Shell.Open("setup");
            Assert.That(_root.Settings.Current.LastScreen, Is.EqualTo("setup"));
            var flush = _root.FlushSettingsAsync();
            yield return new WaitUntil(() => flush.IsCompleted);
            _root.Teardown();

            Build();
            yield return null;
            Assert.That(_root.Shell.Host.Current, Is.EqualTo("setup"));
        }

        [UnityTest]
        public IEnumerator AppSettings_ChangeTheSettings_AndFollowChangesFromElsewhere()
        {
            Build();
            yield return null;
            _root.Shell.Open("setup");
            yield return null;
            var card = _root.Shell.Host.GetScreen("setup").Q<AppSettingsCard>();

            card.Scale.value = "1.25";
            card.Background.value = 1;
            card.Motion.value = "on";
            card.Palette.value = "mint";
            card.Language.value = "en";
            card.Developer.value = true;

            ResolvedUiSettings current = _root.Settings.Current;
            Assert.That(current.UiScale, Is.EqualTo(1.25f));
            Assert.That(current.Theme, Is.EqualTo(new ThemeSettings("mint", ThemeBackground.Light, reducedMotion: true)));
            Assert.That(current.Language, Is.EqualTo("en"));
            Assert.That(current.DeveloperMode, Is.True);
            Assert.That(_root.Shell.Items[3].style.display, Is.EqualTo((StyleEnum<DisplayStyle>)DisplayStyle.Flex), "Diagnostics shows at once.");
            Assert.That(_detailed.Last(), Is.True);

            _root.Settings.SetUiScale(1f);
            Assert.That(card.Scale.value, Is.EqualTo("1"), "The card follows a change made elsewhere.");

            card.Scale.value = "2";
            Assert.That(_root.Settings.Current.UiScale, Is.EqualTo(2f), "The last step is 200%.");
            Assert.That(_root.Panel.scale, Is.EqualTo(UiPanelScale.PanelScale(2f, Screen.dpi, Screen.width, Screen.height)).Within(1e-5f), "The UI size, on the display scale fitted to the window.");
        }

        [UnityTest]
        public IEnumerator Diagnostics_ShowsTheMetrics_AndRefreshesOnlyWhileShown()
        {
            _metrics.Counter("Audio", "Underruns").Add(3);
            _metrics.Timer("Voice", "Convert").Record(TimeSpan.FromMilliseconds(5));
            WriteSettings("{ \"schemaVersion\": 1, \"developerMode\": true, \"lastScreen\": \"diagnostics\" }");
            Build();
            yield return null;

            Assert.That(_root.Shell.Host.Current, Is.EqualTo("diagnostics"), "The saved Diagnostics screen opens in Developer Mode.");
            var screen = (DiagnosticsScreen)_root.Shell.Host.GetScreen("diagnostics");
            Assert.That(screen.RowCount, Is.EqualTo(2));
            Assert.That(screen.Status.text, Is.EqualTo(_root.Localizer.Get(ScreenStrings.DetailedOn)));
            Assert.That(screen.IsRefreshing, Is.True);
            yield return null;

            // The header and the rows share the column definitions, so every column lines up.
            for (int column = 0; column < DiagnosticsFormat.ColumnCount; column++)
            {
                Rect header = screen.Table.HeaderCells[column].worldBound;
                Rect cell = screen.Table.GetCell(0, column).worldBound;
                Assert.That(cell.xMin, Is.EqualTo(header.xMin).Within(0.5f), $"Column {column} starts with its header.");
                Assert.That(cell.xMax, Is.EqualTo(header.xMax).Within(0.5f), $"Column {column} ends with its header.");
            }

            _root.Shell.Open("live");
            int count = screen.RefreshCount;
            Assert.That(screen.IsRefreshing, Is.False);
            yield return new WaitForSecondsRealtime(1.3f);
            Assert.That(screen.RefreshCount, Is.EqualTo(count), "Nothing is taken while hidden.");

            _root.Shell.Open("diagnostics");
            yield return new WaitForSecondsRealtime(1.3f);
            Assert.That(screen.RefreshCount, Is.GreaterThanOrEqualTo(count + 2), "Refreshed on showing and every second.");
        }

        [UnityTest]
        public IEnumerator EveryScreen_HasTheSamePaddingFromTheNavigationAndTheTop()
        {
            WriteSettings("{ \"schemaVersion\": 1, \"developerMode\": true }");
            Build();
            yield return null;

            // Measured on the real styles, so a selector that silently stops matching fails here.
            foreach (string id in new[] { "live", "setup", "lab", "diagnostics", "gallery" })
            {
                _root.Shell.Open(id);
                yield return null;
                yield return null;
                VisualElement title = _root.Shell.Host.GetPage(id).Q(className: ScreenHost.TitleClass);
                Rect nav = _root.Shell.Navigation.worldBound;
                Assert.That(title.worldBound.xMin - nav.xMax, Is.EqualTo(40f).Within(0.5f), $"{id}: XL from the navigation.");
                Assert.That(title.worldBound.yMin - _root.Shell.worldBound.yMin, Is.EqualTo(40f).Within(0.5f), $"{id}: XL from the top.");
            }
        }

        [UnityTest]
        public IEnumerator AtTheSmallestWindow_NothingSpillsOutOfAnyScreen()
        {
            _metrics.Counter("Audio", "Underruns").Add(1);
            WriteSettings("{ \"schemaVersion\": 1, \"developerMode\": true }");
            Build();
            _root.Shell.style.width = 1024;
            yield return null;

            foreach (string id in new[] { "live", "setup", "lab", "diagnostics", "gallery" })
            {
                _root.Shell.Open(id);
                yield return null;
                yield return null;
                VisualElement column = _root.Shell.Host.GetPage(id).Q(className: ScreenHost.ColumnClass);
                float right = column.worldBound.xMax - column.resolvedStyle.paddingRight;
                foreach (VisualElement element in Visible(column))
                {
                    Assert.That(element.worldBound.xMax, Is.LessThanOrEqualTo(right + 0.5f), $"{id}: {element.GetType().Name} '{string.Join(" ", element.GetClasses())}' spills out.");
                }
            }
        }

        [UnityTest]
        public IEnumerator Dropdown_IsAsWideAsItsLongestChoice_AndItsListNoWider()
        {
            Build();
            yield return null;
            _root.Settings.SetReducedMotion(ReducedMotionMode.On);
            _root.Shell.Open("setup");
            yield return null;
            yield return null;
            var card = _root.Shell.Host.GetScreen("setup").Q<AppSettingsCard>();
            card.Q<Card>().Expanded = true;
            yield return null;
            VisualElement box = card.Motion.Q(className: SelectField.UssClass + "__box");
            float widthOn = box.worldBound.width;

            _root.Settings.SetReducedMotion(ReducedMotionMode.System);
            yield return null;
            Assert.That(box.worldBound.width, Is.EqualTo(widthOn).Within(0.5f), "The field does not change width with the selection.");

            _root.Settings.SetReducedMotion(ReducedMotionMode.On);
            card.Motion.Open();
            yield return null;
            yield return null;
            VisualElement menu = _root.Content.parent.Q(className: SelectField.MenuClass);
            Assert.That(menu.worldBound.xMax, Is.LessThanOrEqualTo(box.worldBound.xMax + 0.5f), "With \"On\" selected, the list is no wider than the field.");
            card.Motion.Close();
        }

        [UnityTest]
        public IEnumerator Gallery_HasEverySection_AndItsSamplesOpen()
        {
            WriteSettings("{ \"schemaVersion\": 1, \"developerMode\": true, \"lastScreen\": \"gallery\" }");
            Build();
            yield return null;
            yield return null;

            Assert.That(_root.Shell.Host.Current, Is.EqualTo("gallery"));
            var gallery = (ComponentGalleryScreen)_root.Shell.Host.GetScreen("gallery");
            Assert.That(gallery.Sections.Keys, Is.EquivalentTo(new[]
            {
                GalleryStrings.Buttons, GalleryStrings.Tooltips, GalleryStrings.Toggles, GalleryStrings.Status, GalleryStrings.Fields,
                GalleryStrings.Tabs, GalleryStrings.Notices, GalleryStrings.Dialog, GalleryStrings.Menu, GalleryStrings.Table,
            }));

            foreach (ActionButton button in gallery.Sections[GalleryStrings.Notices].Query<ActionButton>().ToList())
            {
                Submit(button);
            }

            Assert.That(_root.Notifications.Visible.Count + _root.Notifications.Waiting.Count, Is.EqualTo(4), "One sample notice of each kind.");

            Submit(gallery.Sections[GalleryStrings.Dialog].Q<ActionButton>());
            yield return null;
            Assert.That(_root.Dialogs.IsOpen, Is.True, "The sample dialog opens.");
            Submit(_root.Dialogs.Current.Cancel);
            yield return null;
            Assert.That(_root.Dialogs.IsOpen, Is.False);

            Submit(gallery.Sections[GalleryStrings.Menu].Q<ActionButton>());
            yield return null;
            Assert.That(_root.Content.parent.Q<PopupMenu>(), Is.Not.Null, "The sample menu opens.");
        }

        [UnityTest]
        public IEnumerator Diagnostics_WithoutMetrics_SaysSo()
        {
            WriteSettings("{ \"schemaVersion\": 1, \"developerMode\": true, \"lastScreen\": \"diagnostics\" }");
            _root.Build(new UiRootContext(_directory, new StopwatchMonotonicClock(), new UtcSystemClock(), null, null, false));
            yield return null;

            var screen = (DiagnosticsScreen)_root.Shell.Host.GetScreen("diagnostics");
            Assert.That(screen.Status.text, Is.EqualTo(_root.Localizer.Get(ScreenStrings.MetricsUnavailable)));
            Assert.That(screen.IsRefreshing, Is.False);
        }

        private static void Submit(VisualElement target)
        {
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = target;
                target.SendEvent(submit);
            }
        }

        /// <summary>Every shown element under <paramref name="root"/>; a table's own sideways scroll is not entered.</summary>
        private static IEnumerable<VisualElement> Visible(VisualElement root)
        {
            foreach (VisualElement child in root.hierarchy.Children())
            {
                if (child.resolvedStyle.display == DisplayStyle.None)
                {
                    continue;
                }

                yield return child;
                if (child.ClassListContains(DataTable.ScrollClass))
                {
                    continue;
                }

                foreach (VisualElement descendant in Visible(child))
                {
                    yield return descendant;
                }
            }
        }

        private void Build()
        {
            _root.Build(new UiRootContext(
                _directory,
                new StopwatchMonotonicClock(),
                new UtcSystemClock(),
                null,
                null,
                false,
                "0.1.0-test",
                _metrics,
                new Action<bool>[] { _detailed.Add }));
        }

        private void WriteSettings(string text)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, UiSettings.FileName), text);
        }
    }
}
