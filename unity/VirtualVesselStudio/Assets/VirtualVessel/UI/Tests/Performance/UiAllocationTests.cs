using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using UnityEngine.UIElements;
using VirtualVessel.Core.State;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace VirtualVessel.UI.Tests.Performance
{
    /// <summary>
    /// UI foundation detailed design 14 and 16: updating a binding or a level meter must not allocate,
    /// because these run many times a second while streaming.
    /// </summary>
    public sealed class UiAllocationTests
    {
        private GameObject _host;
        private PanelSettings _panelSettings;
        private UiServicesScope _scope;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _host = new GameObject(nameof(UiAllocationTests));
            UIDocument document = _host.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            var table = new StringTable(new LocaleInfo("en", "English", null), new Dictionary<string, string>());
            _scope = new UiServicesScope(new UiServices(new Localizer(new[] { table }, "en", null), null, null));
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
        public IEnumerator BindingUpdate_DoesNotAllocate()
        {
            var state = new ObservableValue<float>(0f);
            var element = new VisualElement();
            float shown = 0f;
            element.Bind(state, value => shown = value);
            _scope.Add(element);
            yield return null;

            // Warm up, then measure one change on the main thread.
            state.Value = 0.5f;
            float next = 0.5f;
            Assert.That(() =>
            {
                next += 0.01f;
                state.Value = next;
            }, Is.Not.AllocatingGCMemory());
            Assert.That(shown, NUnit.Framework.Is.EqualTo(next), "The binding applied the change.");
        }

        [UnityTest]
        public IEnumerator LevelMeterUpdate_DoesNotAllocate()
        {
            var meter = new LevelMeter();
            _scope.Add(meter);
            yield return null;

            meter.Level = 0.9f;
            float level = 0f;
            Assert.That(() =>
            {
                level = level > 0.95f ? 0f : level + 0.05f;
                meter.Level = level;
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
