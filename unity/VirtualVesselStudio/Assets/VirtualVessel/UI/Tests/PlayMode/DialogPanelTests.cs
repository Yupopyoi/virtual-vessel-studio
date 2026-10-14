using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Tests
{
    /// <summary>Confirmation dialogs and dangerous commands on a real runtime panel.</summary>
    public sealed class DialogPanelTests
    {
        private GameObject _host;
        private PanelSettings _panelSettings;
        private Localizer _localizer;
        private UiServicesScope _scope;
        private DialogService _dialogs;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _localizer = new Localizer(
                new[]
                {
                    Table("ja", ("live.end", "配信を終了"), ("live.end.title", "配信を終了しますか？"), ("live.end.body", "視聴者には配信の終了が表示されます。"), ("common.cancel", "キャンセル")),
                    Table("en", ("live.end", "End stream"), ("live.end.title", "End the stream?"), ("live.end.body", "Viewers will see that the stream has ended."), ("common.cancel", "Cancel")),
                },
                "ja",
                null);
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _host = new GameObject(nameof(DialogPanelTests));
            UIDocument document = _host.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _dialogs = new DialogService();
            _scope = new UiServicesScope(new UiServices(_localizer, null, null, null, _dialogs));
            _scope.AddToClassList("vv-root");
            _scope.style.flexGrow = 1;
            document.rootVisualElement.Add(_scope);
            _dialogs.Attach(_scope);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_host);
            Object.Destroy(_panelSettings);
        }

        [UnityTest]
        public IEnumerator Dialog_ConfirmsAndCancels_AndReturnsFocus()
        {
            var before = new ActionButton("live.end");
            _scope.Add(before);
            yield return null;
            before.Focus();

            Task<bool> confirm = _dialogs.ConfirmAsync("live.end.title", "live.end.body", "live.end", ConfirmStyle.Danger);
            yield return null;
            yield return null;
            Assert.That(_dialogs.IsOpen, Is.True);
            ConfirmDialog dialog = _dialogs.Current;
            Assert.That(dialog.Q<Label>(className: ConfirmDialog.UssClass + "__title").text, Is.EqualTo("配信を終了しますか？"));
            Assert.That(dialog.Confirm.ClassListContains("vv-button--danger"), Is.True);
            Assert.That(_scope.focusController.focusedElement, Is.SameAs(dialog.Cancel), "Cancel gets focus first for a dangerous operation.");

            Move(dialog.Cancel, NavigationMoveEvent.Direction.Next);
            Assert.That(_scope.focusController.focusedElement, Is.SameAs(dialog.Confirm), "Tab stays inside the dialog.");
            Move(dialog.Confirm, NavigationMoveEvent.Direction.Next);
            Assert.That(_scope.focusController.focusedElement, Is.SameAs(dialog.Cancel));

            Submit(dialog.Confirm);
            yield return null;
            Assert.That(confirm.IsCompleted && confirm.Result, Is.True);
            Assert.That(_dialogs.IsOpen, Is.False);
            Assert.That(_scope.Q(className: DialogService.OverlayClass), Is.Null);
            Assert.That(_scope.focusController.focusedElement, Is.SameAs(before), "Focus goes back where it was.");

            Task<bool> escaped = _dialogs.ConfirmAsync("live.end.title", "live.end.body", "live.end");
            yield return null;
            yield return null;
            Assert.That(_scope.focusController.focusedElement, Is.SameAs(_dialogs.Current.Confirm), "A normal dialog focuses its confirm button.");
            PressEscape(_dialogs.Current.Confirm);
            yield return null;
            Assert.That(escaped.IsCompleted && !escaped.Result, Is.True, "Esc cancels.");
        }

        [UnityTest]
        public IEnumerator DangerCommand_RunsOnlyAfterConfirmation()
        {
            int runs = 0;
            var command = new DangerCommand(new UiCommand(() => runs++), new ConfirmationRequest("live.end.title", "live.end.body", "live.end"));
            ActionButton button = new ActionButton("live.end", ButtonVariant.Danger).Bind(command);
            _scope.Add(button);
            yield return null;

            Submit(button);
            yield return null;
            Assert.That(_dialogs.IsOpen, Is.True);
            Assert.That(runs, Is.EqualTo(0), "Nothing runs before the answer.");
            Submit(button);
            Assert.That(_scope.Query(className: DialogService.OverlayClass).ToList().Count, Is.EqualTo(1), "A second press does not open a second dialog.");

            Submit(_dialogs.Current.Cancel);
            yield return null;
            Assert.That(runs, Is.EqualTo(0), "Cancel does not run it.");
            Assert.That(button.enabledSelf, Is.True);

            Submit(button);
            yield return null;
            Submit(_dialogs.Current.Confirm);
            yield return null;
            yield return null;
            Assert.That(runs, Is.EqualTo(1), "Confirm runs it once.");
        }

        [UnityTest]
        public IEnumerator DangerCommand_DoesNotRunWithoutADialogService()
        {
            int runs = 0;
            var scope = new UiServicesScope(new UiServices(_localizer, null, null));
            _scope.Add(scope);
            var command = new DangerCommand(new UiCommand(() => runs++), new ConfirmationRequest("live.end.title", "live.end.body", "live.end"));
            ActionButton button = new ActionButton("live.end", ButtonVariant.Danger).Bind(command);
            scope.Add(button);
            yield return null;

            LogAssert.Expect(LogType.Error, new Regex("No dialog service"));
            Submit(button);
            yield return null;
            Assert.That(runs, Is.EqualTo(0), "A dangerous operation never runs unconfirmed.");
        }

        private static void Submit(VisualElement element)
        {
            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = element;
                element.SendEvent(evt);
            }
        }

        private static void Move(VisualElement target, NavigationMoveEvent.Direction direction)
        {
            using (NavigationMoveEvent evt = NavigationMoveEvent.GetPooled(direction))
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        private static void PressEscape(VisualElement target)
        {
            using (KeyDownEvent evt = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None))
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
