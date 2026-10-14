using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VirtualVessel.Core.State;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Dialogs;

namespace VirtualVessel.UI.Tests
{
    /// <summary>Pure parts of confirmation; the dialog on a panel is in the PlayMode tests.</summary>
    public sealed class DialogAndDangerCommandTests
    {
        [Test]
        public void Dialog_FocusesCancelFirstOnlyForDangerousOperations()
        {
            Assert.That(DialogService.FocusCancelFirst(ConfirmStyle.Danger), Is.True);
            Assert.That(DialogService.FocusCancelFirst(ConfirmStyle.Normal), Is.False);
        }

        [Test]
        public async Task DangerCommand_DelegatesToTheInnerCommand()
        {
            int runs = 0;
            var reason = new ObservableValue<string>("live.not-started");
            var inner = new UiCommand(() => runs++, reason);
            var command = new DangerCommand(inner, new ConfirmationRequest("live.end.title", "live.end.body", "live.end"));
            int changes = 0;
            command.CanExecuteChanged += () => changes++;

            Assert.That(command.CanExecute, Is.False);
            Assert.That(command.DisabledReasonKey, Is.EqualTo("live.not-started"));
            reason.Value = null;
            await command.ExecuteAsync(CancellationToken.None);

            Assert.That(changes, Is.EqualTo(1));
            Assert.That(command.CanExecute, Is.True);
            Assert.That(runs, Is.EqualTo(1));
            Assert.That(command.Confirmation.ConfirmKey, Is.EqualTo("live.end"));
        }
    }
}
