using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Core.State;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The step 5-2 components on a real runtime panel.</summary>
    public sealed class InputPanelTests
    {
        private GameObject _host;
        private PanelSettings _panelSettings;
        private Localizer _localizer;
        private UiServicesScope _scope;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _localizer = new Localizer(
                new[]
                {
                    Table("ja", ("q.low", "低"), ("q.high", "高"), ("field.number-invalid", "数字を入力してください。")),
                    Table("en", ("q.low", "Low"), ("q.high", "High"), ("field.number-invalid", "Enter a number.")),
                },
                "ja",
                null);
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _host = new GameObject(nameof(InputPanelTests));
            UIDocument document = _host.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _scope = new UiServicesScope(new UiServices(_localizer, null, null));

            // The themed root: dropdown lists open here.
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
        public IEnumerator SelectField_OpensChoosesAndFollowsTheLanguage()
        {
            var select = new SelectField();
            select.SetChoices(new[] { SelectChoice.FromKey("low", "q.low"), SelectChoice.FromKey("high", "q.high"), SelectChoice.FromText("mic", "USB Mic") });
            select.SetValueWithoutNotify("low");
            var changes = new List<string>();
            select.RegisterValueChangedCallback(evt => changes.Add(evt.newValue));
            _scope.Add(select);
            yield return null;

            Assert.That(select.DisplayedText, Is.EqualTo("低"));
            _localizer.SetLocale("en");
            Assert.That(select.DisplayedText, Is.EqualTo("Low"), "The shown option follows the language.");

            select.Open();
            yield return null;
            Assert.That(select.IsOpen, Is.True);
            VisualElement menu = _scope.Q(className: SelectField.MenuClass);
            Assert.That(menu, Is.Not.Null, "The list opens on the themed root.");
            Assert.That(menu.Query<Label>(className: SelectField.OptionClass).ToList().ConvertAll(l => l.text), Is.EqualTo(new[] { "Low", "High", "USB Mic" }));

            select.Choose(2);
            Assert.That(select.IsOpen, Is.False);
            Assert.That(select.value, Is.EqualTo("mic"));
            Assert.That(changes, Is.EqualTo(new[] { "mic" }));
            Assert.That(_scope.Q(className: SelectField.MenuClass), Is.Null, "The list is removed when closed.");
        }

        [UnityTest]
        public IEnumerator SelectField_ClosesWhenRemoved()
        {
            var select = new SelectField();
            select.SetChoices(new[] { SelectChoice.FromKey("low", "q.low") });
            _scope.Add(select);
            yield return null;
            select.Open();

            select.RemoveFromHierarchy();

            Assert.That(select.IsOpen, Is.False);
            Assert.That(_scope.Q(className: SelectField.MenuClass), Is.Null);
        }

        [UnityTest]
        public IEnumerator NumberInput_CommitsClampsAndShowsErrors()
        {
            var input = new NumberInput { Min = 8000, Max = 48000, Unit = "Hz" };
            input.SetValueWithoutNotify(44100);
            var changes = new List<double>();
            input.RegisterValueChangedCallback(evt => changes.Add(evt.newValue));
            _scope.Add(input);
            yield return null;

            Assert.That(input.Text, Is.EqualTo("44100"));
            input.Q<TextField>().value = "96000";
            Assert.That(changes, Is.Empty, "Typing alone does not change the value.");

            input.Commit();
            Assert.That(input.value, Is.EqualTo(48000), "Brought into the range.");
            Assert.That(input.Text, Is.EqualTo("48000"));

            input.Q<TextField>().value = "abc";
            input.Commit();
            Assert.That(input.value, Is.EqualTo(48000), "The previous value stays.");
            Assert.That(input.ErrorKey, Is.EqualTo(CommonStrings.NumberInvalid));
            Assert.That(input.ClassListContains("vv-field--error"), Is.True);

            input.Q<TextField>().value = "16000";
            input.Commit();
            Assert.That(input.ErrorKey, Is.Null, "A valid number clears the error.");
            Assert.That(changes, Is.EqualTo(new[] { 48000.0, 16000.0 }));
        }

        [UnityTest]
        public IEnumerator TextInput_ReportsItsOwnChanges()
        {
            var input = new TextInput();
            var changes = new List<string>();
            input.RegisterValueChangedCallback(evt => changes.Add(evt.newValue));
            _scope.Add(input);
            yield return null;

            input.value = "雑談配信";

            Assert.That(changes, Is.EqualTo(new[] { "雑談配信" }), "One change, from the input itself.");
            input.ErrorKey = "field.number-invalid";
            Assert.That(input.ClassListContains("vv-field--error"), Is.True);
        }

        [UnityTest]
        public IEnumerator SliderAndTabs_MoveWithArrowKeys()
        {
            var slider = new ValueSlider { LowValue = -60, HighValue = 0, Step = 1, Unit = "dB" };
            slider.SetValueWithoutNotify(-12);
            var tabs = new TabBar("q.low", "q.high");
            _scope.Add(slider);
            _scope.Add(tabs);
            yield return null;

            Move(slider.Q(className: ValueSlider.UssClass + "__track"), NavigationMoveEvent.Direction.Right);
            Move(tabs, NavigationMoveEvent.Direction.Right);

            Assert.That(slider.value, Is.EqualTo(-11f));
            Assert.That(slider.DisplayedValue, Is.EqualTo("−11 dB"));
            Assert.That(tabs.value, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator LevelMeter_ReadsTheSourceOnATimerWhileShown()
        {
            var level = new ObservableValue<float>(0.25f);
            var meter = new LevelMeter();
            meter.BindLevel(level, intervalMs: 20);
            _scope.Add(meter);
            yield return null;
            Assert.That(meter.LitCount, Is.EqualTo(6));

            level.Value = 0.5f;
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(meter.LitCount, Is.EqualTo(12));

            meter.RemoveFromHierarchy();
            level.Value = 1f;
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(meter.LitCount, Is.EqualTo(12), "No polling after the meter leaves the screen.");
        }

        [UnityTest]
        public IEnumerator ArrowKeys_ChangeTheValue_WithoutMovingFocusToAnotherRow()
        {
            // Rows above and below: Unity's own arrow-key navigation would move focus to them.
            var above = new Button { text = "above" };
            var select = new SelectField();
            select.SetChoices(new[] { SelectChoice.FromText("a", "A"), SelectChoice.FromText("b", "B") });
            select.SetValueWithoutNotify("a");
            var segmented = new Segmented("q.low", "q.high");
            var slider = new ValueSlider { LowValue = 0, HighValue = 10 };
            var tabs = new TabBar("q.low", "q.high");
            var below = new Button { text = "below" };
            foreach (VisualElement row in new VisualElement[] { above, select, segmented, slider, tabs, below })
            {
                _scope.Add(row);
            }

            yield return null;

            VisualElement box = select.Q(className: SelectField.UssClass + "__box");
            yield return Press(box, NavigationMoveEvent.Direction.Down);
            Assert.That(select.value, Is.EqualTo("b"));
            Assert.That(HasFocus(select), Is.True, "Down changes the dropdown and keeps focus there.");

            yield return Press(segmented, NavigationMoveEvent.Direction.Right);
            Assert.That(segmented.value, Is.EqualTo(1));
            Assert.That(HasFocus(segmented), Is.True);

            VisualElement track = slider.Q(className: ValueSlider.UssClass + "__track");
            float before = slider.value;
            yield return Press(track, NavigationMoveEvent.Direction.Right);
            Assert.That(slider.value, Is.GreaterThan(before));
            Assert.That(HasFocus(slider), Is.True);

            yield return Press(tabs, NavigationMoveEvent.Direction.Right);
            Assert.That(tabs.value, Is.EqualTo(1));
            Assert.That(HasFocus(tabs), Is.True);
        }

        private IEnumerator Press(VisualElement target, NavigationMoveEvent.Direction direction)
        {
            target.Focus();
            yield return null;
            Move(target, direction);
            yield return null;
        }

        /// <summary>Focus is on the control or inside it; Contains alone excludes the control itself.</summary>
        private bool HasFocus(VisualElement control)
        {
            var focused = _scope.focusController.focusedElement as VisualElement;
            return focused == control || control.Contains(focused);
        }

        private static void Move(VisualElement target, NavigationMoveEvent.Direction direction)
        {
            using (NavigationMoveEvent evt = NavigationMoveEvent.GetPooled(direction))
            {
                evt.target = target;
                target.SendEvent(evt);
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
