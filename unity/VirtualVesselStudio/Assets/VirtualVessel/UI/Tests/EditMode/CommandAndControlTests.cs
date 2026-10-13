using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;
using VirtualVessel.Core.State;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Tests
{
    /// <summary>Pure parts of commands and components; behavior on a panel is in the PlayMode tests.</summary>
    public sealed class CommandAndControlTests
    {
        [Test]
        public void MessageKeyFor_ChoosesWhatTheUserSees()
        {
            Assert.That(CommandRunner.MessageKeyFor(new UserFacingException("audio.device.lost")), Is.EqualTo("audio.device.lost"));
            Assert.That(CommandRunner.MessageKeyFor(new AggregateException(new UserFacingException("x.y"))), Is.EqualTo("x.y"));
            Assert.That(CommandRunner.MessageKeyFor(new InvalidOperationException()), Is.EqualTo(CommonStrings.UnexpectedError));
            Assert.That(CommandRunner.MessageKeyFor(new OperationCanceledException()), Is.Null, "A cancel was asked for; it is not a failure.");
        }

        [Test]
        public async Task RunAsync_ReportsFailureWithItsMessageKey()
        {
            string shown = null;
            var services = new UiServices(new Localizer(new[] { new StringTable(new LocaleInfo("en", "English", null), new System.Collections.Generic.Dictionary<string, string>()) }, "en", null), null, new NullLog(), key => shown = key);

            bool ok = await CommandRunner.RunAsync(() => Task.CompletedTask, services);
            bool failed = await CommandRunner.RunAsync(() => throw new UserFacingException("capture.busy"), services);

            Assert.That(ok, Is.True);
            Assert.That(failed, Is.False);
            Assert.That(shown, Is.EqualTo("capture.busy"));
        }

        [Test]
        public void UiCommand_CanExecuteFollowsTheReason_AndListensOnlyWhileObserved()
        {
            var reason = new ObservableValue<string>("live.need-device");
            var command = new UiCommand(() => { }, reason);
            int changes = 0;
            Action onChanged = () => changes++;

            Assert.That(command.CanExecute, Is.False);
            Assert.That(command.DisabledReasonKey, Is.EqualTo("live.need-device"));
            command.CanExecuteChanged += onChanged;
            reason.Value = null;
            command.CanExecuteChanged -= onChanged;
            reason.Value = "again";

            Assert.That(changes, Is.EqualTo(1), "No notification after the last listener left.");
            Assert.That(command.CanExecute, Is.False);
        }

        [Test]
        public void CommandBinding_IsEnabled()
        {
            Assert.That(CommandBinding.IsEnabled(hasCommand: false, canExecute: false, running: false), Is.True, "A button without a command is not disabled by it.");
            Assert.That(CommandBinding.IsEnabled(true, true, false), Is.True);
            Assert.That(CommandBinding.IsEnabled(true, false, false), Is.False);
            Assert.That(CommandBinding.IsEnabled(true, true, true), Is.False, "No double press while running.");
        }

        [Test]
        public void KeyedText_Resolve()
        {
            Assert.That(KeyedText.Resolve(null, "live.start"), Is.EqualTo("[live.start]"), "Outside a scope the key is shown.");
            Assert.That(KeyedText.Resolve(null, null), Is.Empty);
        }

        [Test]
        public void Segmented_SplitKeysAndClamp()
        {
            Assert.That(Segmented.SplitKeys(" a.b, c.d ,,e.f "), Is.EqualTo(new[] { "a.b", "c.d", "e.f" }));
            Assert.That(Segmented.SplitKeys(null), Is.Empty);
            Assert.That(Segmented.Clamp(5, 3), Is.EqualTo(2));
            Assert.That(Segmented.Clamp(-1, 3), Is.EqualTo(0));
            Assert.That(Segmented.Clamp(4, 0), Is.EqualTo(0));
        }

        [Test]
        public void Segmented_KeepsAValueSetBeforeItsItems()
        {
            // UXML may set attributes in any order.
            var segmented = new Segmented { value = 1 };
            segmented.ItemKeys = "a.x,a.y,a.z";

            Assert.That(segmented.value, Is.EqualTo(1));
            Assert.That(segmented.Query<Label>(className: Segmented.SelectedClass).ToList(), Has.Count.EqualTo(1));
        }

        [Test]
        public void StatusChip_ShapeAndBlink()
        {
            Assert.That(StatusChip.IconFor(StatusKind.Idle), Is.EqualTo(IconName.Ring));
            Assert.That(StatusChip.IconFor(StatusKind.Live), Is.EqualTo(IconName.Dot));
            Assert.That(StatusChip.IconFor(StatusKind.Success), Is.EqualTo(IconName.Check));
            Assert.That(StatusChip.IconFor(StatusKind.Warning), Is.EqualTo(IconName.Warning));
            Assert.That(StatusChip.IconFor(StatusKind.Error), Is.EqualTo(IconName.Close));
            Assert.That(StatusChip.ShouldBlink(StatusKind.Live, reducedMotion: false), Is.True);
            Assert.That(StatusChip.ShouldBlink(StatusKind.Live, reducedMotion: true), Is.False);
            Assert.That(StatusChip.ShouldBlink(StatusKind.Error, reducedMotion: false), Is.False);

            var chip = new StatusChip(StatusKind.Warning, "trk.unstable");
            Assert.That(chip.ClassListContains("vv-chip--warning"), Is.True);
            chip.Status = StatusKind.Success;
            Assert.That(chip.ClassListContains("vv-chip--warning"), Is.False);
            Assert.That(chip.ClassListContains("vv-chip--success"), Is.True);
        }

        [Test]
        public void ActionButton_VariantClassesAndDefaultLookRemoved()
        {
            var button = new ActionButton("live.start", ButtonVariant.Primary);

            Assert.That(button.ClassListContains(Button.ussClassName), Is.False);
            Assert.That(button.ClassListContains("vv-button--primary"), Is.True);
            button.Variant = ButtonVariant.Danger;
            Assert.That(button.ClassListContains("vv-button--primary"), Is.False);
            Assert.That(button.ClassListContains("vv-button--danger"), Is.True);
            Assert.That(button.DisplayedText, Is.EqualTo("[live.start]"));
        }

        [Test]
        public void Card_ChildrenGoToTheBody()
        {
            var card = new Card("voice.title", alternate: true);
            var child = new Label("x");
            card.Add(child);

            Assert.That(child.hierarchy.parent.ClassListContains("vv-card__body"), Is.True);
            Assert.That(child.parent, Is.SameAs(card), "The logical parent is still the card.");
            Assert.That(card.ClassListContains(Card.AlternateClass), Is.True);
        }

        [Test]
        public void FocusRing_OnlyForKeyboardDirections()
        {
            Assert.That(FocusRing.IsFromKeyboard(FocusChangeDirection.unspecified), Is.False);
            Assert.That(FocusRing.IsFromKeyboard(FocusChangeDirection.none), Is.False);
            Assert.That(FocusRing.IsFromKeyboard(null), Is.False);
        }

        private sealed class NullLog : Diagnostics.Logging.ILog
        {
            public bool IsEnabled(Diagnostics.Logging.LogLevel level)
            {
                return true;
            }

            public void Write(Diagnostics.Logging.LogLevel level, string message, Exception exception = null)
            {
            }

            public void Write(Diagnostics.Logging.LogLevel level, string message, System.Collections.Generic.IReadOnlyList<Diagnostics.Logging.LogProperty> properties, Exception exception = null)
            {
            }

            public Diagnostics.Logging.ILog WithContext(string key, string value)
            {
                return this;
            }
        }
    }
}
