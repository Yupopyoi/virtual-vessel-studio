using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Notifications;
using Object = UnityEngine.Object;

namespace VirtualVessel.UI.Tests
{
    /// <summary>Notices on a real runtime panel.</summary>
    public sealed class NotificationPanelTests
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
                    Table(
                        "ja",
                        ("q.1", "通知1"),
                        ("q.2", "通知2"),
                        ("q.3", "通知3"),
                        ("q.4", "通知4"),
                        ("common.close", "閉じる"),
                        ("notice.failure.title", "操作を完了できませんでした"),
                        ("notice.more.other", "ほかに {count} 件の通知"),
                        ("capture.busy", "キャプチャーが使用中です。")),
                    Table(
                        "en",
                        ("q.1", "Notice 1"),
                        ("q.2", "Notice 2"),
                        ("q.3", "Notice 3"),
                        ("q.4", "Notice 4"),
                        ("common.close", "Close"),
                        ("notice.failure.title", "Couldn't complete the action"),
                        ("notice.more.other", "{count} more notices"),
                        ("capture.busy", "The capture device is in use.")),
                },
                "ja",
                null);
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _host = new GameObject(nameof(NotificationPanelTests));
            UIDocument document = _host.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _scope = new UiServicesScope(new UiServices(_localizer, null, null));
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
        public IEnumerator Notices_HideOnTime_ExceptErrors()
        {
            var notices = new NotificationService(new NotificationOptions
            {
                InfoDuration = TimeSpan.FromMilliseconds(100),
                TransitionDuration = TimeSpan.Zero,
            });
            notices.Attach(_scope);

            notices.Show(NoticeKind.Info, "q.1");
            notices.Show(NoticeKind.Error, "capture.busy");
            yield return null;
            Assert.That(notices.Visible.Count, Is.EqualTo(2));
            Assert.That(notices.Visible[0].Title.text, Is.EqualTo("通知1"));
            Assert.That(notices.Visible[0].Body.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "No empty body line.");

            yield return new WaitForSecondsRealtime(0.4f);
            Assert.That(notices.Visible.Count, Is.EqualTo(1));
            Assert.That(notices.Visible[0].Kind, Is.EqualTo(NoticeKind.Error), "Errors stay until closed.");

            Submit(notices.Visible[0].CloseButton);
            Assert.That(notices.Visible.Count, Is.EqualTo(0));
            Assert.That(_scope.Q(className: Notice.UssClass), Is.Null);
        }

        [UnityTest]
        public IEnumerator Notices_BeyondTheLimit_WaitAndShowLater()
        {
            var notices = new NotificationService(new NotificationOptions { TransitionDuration = TimeSpan.Zero });
            notices.Attach(_scope);
            VisualElement stack = _scope.Q(className: NotificationService.StackClass);
            WaitingCount waiting = stack.Q<WaitingCount>();

            notices.Show(NoticeKind.Error, "q.1");
            notices.Show(NoticeKind.Error, "q.2");
            notices.Show(NoticeKind.Error, "q.3");
            notices.Show(NoticeKind.Error, "q.4");
            yield return null;

            Assert.That(stack.ClassListContains("vv-notices--bottom-right"), Is.True);
            Assert.That(Titles(notices), Is.EqualTo(new[] { "通知1", "通知2", "通知3" }), "Errors that stay are never pushed out.");
            Assert.That(notices.Waiting.Count, Is.EqualTo(1));
            Assert.That(waiting.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(waiting.Text, Is.EqualTo("ほかに 1 件の通知"));
            Assert.That(stack.IndexOf(waiting), Is.EqualTo(stack.childCount - 1), "The count sits nearest the corner.");

            Submit(notices.Visible[0].CloseButton);
            yield return null;
            Assert.That(Titles(notices), Is.EqualTo(new[] { "通知2", "通知3", "通知4" }), "The waiting notice shows when there is room.");
            Assert.That(waiting.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator Notices_ShowErrorsFirst()
        {
            var notices = new NotificationService(new NotificationOptions { TransitionDuration = TimeSpan.Zero });
            notices.Attach(_scope);

            notices.Show(NoticeKind.Info, "q.1");
            notices.Show(NoticeKind.Warning, "q.2");
            notices.Show(NoticeKind.Info, "q.3");
            notices.Show(NoticeKind.Error, "capture.busy");
            yield return null;

            Assert.That(Titles(notices), Is.EqualTo(new[] { "通知2", "通知3", "キャプチャーが使用中です。" }), "An error shows at once; the oldest info waits.");
            Assert.That(notices.Waiting[0].TitleKey, Is.EqualTo("q.1"), "Sent back, not dropped.");

            notices.Show(NoticeKind.Info, "q.4");
            Assert.That(notices.Waiting.Count, Is.EqualTo(2), "An info does not push out another info.");
            notices.Show(NoticeKind.Success, "q.3");
            Assert.That(notices.Visible[2].Kind, Is.EqualTo(NoticeKind.Success), "A success outranks the info shown.");

            Submit(notices.Visible[0].CloseButton);
            yield return null;
            Assert.That(notices.Visible[2].Title.text, Is.EqualTo("通知1"), "Of the waiting infos, the earliest goes first.");
        }

        [UnityTest]
        public IEnumerator Notices_AtATopCorner_PutTheNewestOnTop()
        {
            var notices = new NotificationService(new NotificationOptions { Corner = NoticeCorner.TopLeft });
            notices.Attach(_scope);
            VisualElement stack = _scope.Q(className: NotificationService.StackClass);

            notices.Show(NoticeKind.Success, "q.1");
            notices.Show(NoticeKind.Success, "q.2");
            yield return null;

            Assert.That(stack.ClassListContains("vv-notices--top-left"), Is.True);
            Assert.That(stack.Q<Notice>().Title.text, Is.EqualTo("通知2"));
        }

        [UnityTest]
        public IEnumerator ShowFailure_IsAnErrorNoticeThatFollowsTheLanguage()
        {
            var notices = new NotificationService();
            notices.Attach(_scope);

            notices.ShowFailure("capture.busy");
            yield return null;

            Notice notice = notices.Visible[0];
            Assert.That(notice.Kind, Is.EqualTo(NoticeKind.Error));
            Assert.That(notice.ClassListContains("vv-notice--error"), Is.True);
            Assert.That(notice.Q<Icon>().Name, Is.EqualTo(IconName.Close), "Error is shown by shape as well as color.");
            Assert.That(notice.Title.text, Is.EqualTo("操作を完了できませんでした"));
            Assert.That(notice.Body.text, Is.EqualTo("キャプチャーが使用中です。"));
            _localizer.SetLocale("en");
            Assert.That(notice.Body.text, Is.EqualTo("The capture device is in use."));
        }

        private static string[] Titles(NotificationService notices)
        {
            var titles = new string[notices.Visible.Count];
            for (int i = 0; i < titles.Length; i++)
            {
                titles[i] = notices.Visible[i].Title.text;
            }

            return titles;
        }

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
