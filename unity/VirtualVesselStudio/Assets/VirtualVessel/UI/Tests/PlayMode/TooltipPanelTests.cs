using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Core.State;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Tooltips;
using Object = UnityEngine.Object;

namespace VirtualVessel.UI.Tests
{
    /// <summary>Tooltips on a real runtime panel.</summary>
    public sealed class TooltipPanelTests
    {
        private GameObject _host;
        private PanelSettings _panelSettings;
        private UiServicesScope _scope;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var localizer = new Localizer(
                new[]
                {
                    Table("ja", ("live.start.tip", "配信を始めます"), ("live.need-device", "カメラを接続すると押せます"), ("common.close", "閉じる")),
                    Table("en", ("live.start.tip", "Starts the stream"), ("live.need-device", "Connect a camera to use this"), ("common.close", "Close")),
                },
                "ja",
                null);
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _host = new GameObject(nameof(TooltipPanelTests));
            UIDocument document = _host.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _scope = new UiServicesScope(new UiServices(localizer, null, null));
            _scope.AddToClassList("vv-root");
            _scope.style.flexGrow = 1;
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
        public IEnumerator Tooltip_AppearsAfterTheDelay_AndExplainsADisabledButton()
        {
            var tooltips = new TooltipService(new TooltipOptions { ShowDelay = TimeSpan.FromMilliseconds(100) });
            tooltips.Attach(_scope);
            var reason = new ObservableValue<string>("live.need-device");
            ActionButton button = new ActionButton("live.start") { TooltipKey = "live.start.tip", Shortcut = "Ctrl+L" }.Bind(new UiCommand(() => { }, reason));
            _scope.Add(button);
            yield return null;

            tooltips.Hover(button);
            Assert.That(tooltips.IsVisible, Is.False, "Not before the delay.");
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(tooltips.IsVisible, Is.True);
            Assert.That(tooltips.ShownText, Is.EqualTo("カメラを接続すると押せます"), "A disabled button says why it cannot be pressed.");

            tooltips.Hide();
            reason.Value = null;
            tooltips.Show(button);
            Assert.That(tooltips.ShownText, Is.EqualTo("配信を始めます"));
            Assert.That(tooltips.Element.Q<KeyHint>().Keys, Is.EqualTo("Ctrl+L"));

            tooltips.Hover(null);
            Assert.That(tooltips.IsVisible, Is.False, "Leaving the part hides it.");
            tooltips.Dispose();
            Assert.That(_scope.Q(className: TooltipService.UssClass), Is.Null);
        }

        [UnityTest]
        public IEnumerator Tooltip_ComesFromTheNearestPartWithOne()
        {
            var tooltips = new TooltipService();
            tooltips.Attach(_scope);
            var button = new IconButton(IconName.Close) { TooltipKey = "common.close" };
            _scope.Add(button);
            yield return null;

            Assert.That(TooltipService.FindSource(button.Q<Icon>()), Is.SameAs(button), "Resting on the icon shows the button's tooltip.");
            Assert.That(TooltipService.FindSource(_scope), Is.Null);
            tooltips.Show(button.Q<Icon>());
            Assert.That(tooltips.ShownText, Is.EqualTo("閉じる"));
            tooltips.Dispose();
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
