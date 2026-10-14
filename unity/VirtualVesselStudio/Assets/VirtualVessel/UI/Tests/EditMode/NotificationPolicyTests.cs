using System;
using NUnit.Framework;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Notifications;

namespace VirtualVessel.UI.Tests
{
    /// <summary>Decisions about notices; behavior on a panel is in the PlayMode tests.</summary>
    public sealed class NotificationPolicyTests
    {
        [Test]
        public void AutoHideAfter_DefaultTimes()
        {
            var options = new NotificationOptions();

            Assert.That(NotificationPolicy.AutoHideAfter(NoticeKind.Info, options), Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(NotificationPolicy.AutoHideAfter(NoticeKind.Success, options), Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(NotificationPolicy.AutoHideAfter(NoticeKind.Warning, options), Is.EqualTo(TimeSpan.FromSeconds(8)));
            Assert.That(NotificationPolicy.AutoHideAfter(NoticeKind.Error, options), Is.Null, "Errors stay until closed.");
        }

        [Test]
        public void AutoHideAfter_FollowsTheOptions()
        {
            var options = new NotificationOptions { WarningDuration = null, ErrorDuration = TimeSpan.FromSeconds(30) };

            Assert.That(NotificationPolicy.AutoHideAfter(NoticeKind.Warning, options), Is.Null);
            Assert.That(NotificationPolicy.AutoHideAfter(NoticeKind.Error, options), Is.EqualTo(TimeSpan.FromSeconds(30)));
        }

        [TestCase(0, 3, true)]
        [TestCase(2, 3, true)]
        [TestCase(3, 3, false)]
        [TestCase(0, 0, true)]
        [TestCase(1, 0, false)]
        public void HasRoom_UpToTheLimit_AndAlwaysForOne(int visible, int max, bool expected)
        {
            Assert.That(NotificationPolicy.HasRoom(visible, max), Is.EqualTo(expected));
        }

        [Test]
        public void Priority_PutsErrorsFirst()
        {
            Assert.That(NotificationPolicy.Priority(NoticeKind.Error), Is.GreaterThan(NotificationPolicy.Priority(NoticeKind.Warning)));
            Assert.That(NotificationPolicy.Priority(NoticeKind.Warning), Is.GreaterThan(NotificationPolicy.Priority(NoticeKind.Success)));
            Assert.That(NotificationPolicy.Priority(NoticeKind.Success), Is.GreaterThan(NotificationPolicy.Priority(NoticeKind.Info)));
        }

        [Test]
        public void NextToShow_IsTheHighestPriority_ThenTheEarliest()
        {
            var waiting = new[] { Request(NoticeKind.Info, 1), Request(NoticeKind.Warning, 3), Request(NoticeKind.Warning, 2), Request(NoticeKind.Success, 0) };

            Assert.That(NotificationPolicy.NextToShow(waiting), Is.EqualTo(2), "The earlier of the two warnings.");
            Assert.That(NotificationPolicy.NextToShow(new NoticeRequest[0]), Is.EqualTo(-1));
        }

        [Test]
        public void ToMakeRoomFor_SendsBackTheOldestLowestNotice_OnlyForAHigherOne()
        {
            var shown = new[] { Request(NoticeKind.Warning, 0), Request(NoticeKind.Info, 2), Request(NoticeKind.Info, 1) };

            Assert.That(NotificationPolicy.ToMakeRoomFor(shown, Request(NoticeKind.Error, 3)), Is.EqualTo(2), "The older info goes back.");
            Assert.That(NotificationPolicy.ToMakeRoomFor(shown, Request(NoticeKind.Info, 3)), Is.EqualTo(-1), "Same priority waits its turn.");

            var errors = new[] { Request(NoticeKind.Error, 0), Request(NoticeKind.Error, 1) };
            Assert.That(NotificationPolicy.ToMakeRoomFor(errors, Request(NoticeKind.Error, 2)), Is.EqualTo(-1), "An error is never sent back.");
        }

        [Test]
        public void Corner_DecidesClassAndOrder()
        {
            Assert.That(NotificationPolicy.CornerClass(NoticeCorner.BottomRight), Is.EqualTo("vv-notices--bottom-right"));
            Assert.That(NotificationPolicy.CornerClass(NoticeCorner.TopLeft), Is.EqualTo("vv-notices--top-left"));
            Assert.That(NotificationPolicy.NewestFirst(NoticeCorner.BottomRight), Is.False, "Newest at the bottom, nearest the corner.");
            Assert.That(NotificationPolicy.NewestFirst(NoticeCorner.TopRight), Is.True);
        }

        [Test]
        public void IconFor_GivesEachKindItsOwnShape()
        {
            Assert.That(NotificationPolicy.IconFor(NoticeKind.Info), Is.EqualTo(IconName.Info));
            Assert.That(NotificationPolicy.IconFor(NoticeKind.Success), Is.EqualTo(IconName.Check));
            Assert.That(NotificationPolicy.IconFor(NoticeKind.Warning), Is.EqualTo(IconName.Warning));
            Assert.That(NotificationPolicy.IconFor(NoticeKind.Error), Is.EqualTo(IconName.Close));
        }

        [Test]
        public void WaitingCount_IsEmptyAtZero()
        {
            Assert.That(WaitingCount.Format(null, 0), Is.Empty);
            Assert.That(WaitingCount.Format(null, 2), Is.EqualTo("[notice.more]"));
        }

        private static NoticeRequest Request(NoticeKind kind, long sequence)
        {
            return new NoticeRequest(kind, "k", null, sequence);
        }
    }
}
