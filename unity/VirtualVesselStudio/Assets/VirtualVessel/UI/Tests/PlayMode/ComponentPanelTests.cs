using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Core.State;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Tests
{
    /// <summary>Components on a real runtime panel: keyed text, bindings, and commands.</summary>
    public sealed class ComponentPanelTests
    {
        private GameObject _host;
        private PanelSettings _panelSettings;
        private Localizer _localizer;
        private UiServicesScope _scope;
        private List<string> _failures;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _failures = new List<string>();
            _localizer = new Localizer(
                new[]
                {
                    Table("ja", ("live.start", "配信を開始"), ("demo.on", "オン")),
                    Table("en", ("live.start", "Go live"), ("demo.on", "On")),
                },
                "ja",
                null);
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _host = new GameObject(nameof(ComponentPanelTests));
            UIDocument document = _host.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _scope = new UiServicesScope(new UiServices(_localizer, null, null, key => _failures.Add(key)));
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
        public IEnumerator KeyedText_FollowsTheLanguage()
        {
            var button = new ActionButton("live.start", ButtonVariant.Primary);
            Assert.That(button.DisplayedText, Is.EqualTo("[live.start]"), "Not on a panel yet.");

            _scope.Add(button);
            yield return null;
            Assert.That(button.DisplayedText, Is.EqualTo("配信を開始"));

            _localizer.SetLocale("en");
            Assert.That(button.DisplayedText, Is.EqualTo("Go live"));

            button.RemoveFromHierarchy();
            _localizer.SetLocale("ja");
            Assert.That(button.DisplayedText, Is.EqualTo("Go live"), "A removed component no longer listens.");
        }

        [UnityTest]
        public IEnumerator Bind_AppliesWhileOnAPanelOnly()
        {
            var state = new ObservableValue<int>(1);
            var label = new Label();
            label.BindText(state, value => $"{value} fps");

            _scope.Add(label);
            yield return null;
            Assert.That(label.text, Is.EqualTo("1 fps"));

            state.Value = 2;
            Assert.That(label.text, Is.EqualTo("2 fps"));

            label.RemoveFromHierarchy();
            state.Value = 3;
            Assert.That(label.text, Is.EqualTo("2 fps"), "Unsubscribed when removed.");

            _scope.Add(label);
            yield return null;
            Assert.That(label.text, Is.EqualTo("3 fps"), "Shows the current value again when re-added.");
        }

        [UnityTest]
        public IEnumerator BindValue_PassesUserChangesAndShowsTheRuntimeValue()
        {
            var state = new ObservableValue<bool>(false);
            var requests = new List<bool>();
            var toggle = new SwitchToggle("demo.on");

            // The runtime rejects the change: the state stays false.
            toggle.BindValue<SwitchToggle, bool>(state, value => requests.Add(value));
            _scope.Add(toggle);
            yield return null;

            toggle.value = true;
            yield return null;

            Assert.That(requests, Is.EqualTo(new[] { true }));
            Assert.That(toggle.value, Is.False, "The display returns to the runtime value.");

            state.Value = true;
            Assert.That(toggle.value, Is.True);
            Assert.That(requests, Has.Count.EqualTo(1), "A runtime change is not reported as a user change.");
        }

        [UnityTest]
        public IEnumerator Button_FollowsTheCommand()
        {
            var reason = new ObservableValue<string>("live.need-device");
            var gate = new TaskCompletionSource<bool>();
            int runs = 0;
            var command = new UiCommand(
                async _ =>
                {
                    runs++;
                    await gate.Task;
                },
                reason);
            ActionButton button = new ActionButton("live.start").Bind(command);
            _scope.Add(button);
            yield return null;

            Assert.That(button.enabledSelf, Is.False);
            Assert.That(button.CurrentTooltipKey, Is.EqualTo("live.need-device"), "The tooltip explains why it cannot be pressed.");

            reason.Value = null;
            Assert.That(button.enabledSelf, Is.True);

            Submit(button);
            Submit(button);
            yield return null;
            Assert.That(runs, Is.EqualTo(1), "No double run while the first is still running.");
            Assert.That(button.enabledSelf, Is.False);
            Assert.That(button.ClassListContains(CommandBinding.BusyClass), Is.True);

            gate.SetResult(true);
            yield return null;
            yield return null;
            Assert.That(button.enabledSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator Button_ReportsAFailedCommand()
        {
            var command = new UiCommand(() => throw new UserFacingException("capture.busy"));
            ActionButton button = new ActionButton("live.start").Bind(command);
            _scope.Add(button);
            yield return null;

            LogAssert.ignoreFailingMessages = true;
            Submit(button);
            yield return null;

            Assert.That(_failures, Is.EqualTo(new[] { "capture.busy" }));
            Assert.That(button.enabledSelf, Is.True, "The button can be used again after a failure.");
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator Buttons_InARow_AreAsWideAsTheirText()
        {
            // Regression: the focus ring child stopped the button from measuring its text, so each
            // button collapsed to its padding and the label overflowed or wrapped into vertical text.
            _scope.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/VirtualVessel/UI/Styles/components.uss"));
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.width = 300;
            var buttons = new[] { new ActionButton("live.start"), new ActionButton("live.start"), new ActionButton("live.start") };
            foreach (ActionButton button in buttons)
            {
                row.Add(button);
            }

            var reference = new Label("配信を開始");
            reference.AddToClassList("vv-text-button");
            reference.style.alignSelf = Align.FlexStart;
            _scope.Add(reference);
            _scope.Add(row);
            yield return null;
            yield return null;

            float textWidth = reference.layout.width;
            Assert.That(textWidth, Is.GreaterThan(30f), "Sanity check of the reference text.");
            foreach (ActionButton button in buttons)
            {
                Assert.That(button.layout.width, Is.GreaterThanOrEqualTo(textWidth + 36f - 1f), "Text plus 18px padding on each side.");
                Assert.That(button.layout.height, Is.LessThan(48f), "One line.");
            }
        }

        [UnityTest]
        public IEnumerator ScrollBar_StaysInsideItsBar()
        {
            // Regression: with Unity's default theme the scroller slider stayed 24px wide inside the
            // 12px bar, so the thumb ended up off screen.
            _panelSettings.themeStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/VirtualVessel/UI/Tests/PlayMode/RuntimeDefault.tss");
            _scope.AddToClassList("vv-root");
            _scope.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/VirtualVessel/UI/Styles/components.uss"));
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.height = 300;
            var content = new VisualElement();
            content.style.height = 1200;
            scroll.Add(content);
            _scope.Add(scroll);
            yield return null;
            yield return null;

            Rect bar = scroll.verticalScroller.worldBound;
            Rect slider = scroll.verticalScroller.slider.worldBound;
            VisualElement thumb = scroll.verticalScroller.slider.Q(className: "unity-base-slider__dragger");
            Assert.That(bar.width, Is.EqualTo(12f).Within(0.5f));
            Assert.That(slider.xMax, Is.LessThanOrEqualTo(bar.xMax + 0.5f), "The slider stays inside the bar.");
            Assert.That(thumb.worldBound.xMin, Is.GreaterThanOrEqualTo(bar.xMin - 0.5f));
            Assert.That(thumb.worldBound.xMax, Is.LessThanOrEqualTo(bar.xMax + 0.5f));
            Assert.That(thumb.worldBound.height, Is.GreaterThan(10f), "The thumb is visible.");
        }
#endif

        private static void Submit(VisualElement element)
        {
            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = element;
                element.SendEvent(evt);
            }
        }

        private static StringTable Table(string locale, params (string Key, string Value)[] strings)
        {
            var map = new Dictionary<string, string>();
            foreach ((string key, string value) in strings)
            {
                map[key] = value;
            }

            return new StringTable(new LocaleInfo(locale, locale, null), map);
        }
    }
}
