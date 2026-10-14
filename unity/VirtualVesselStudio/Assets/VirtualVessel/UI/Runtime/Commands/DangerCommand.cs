using System;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualVessel.UI.Commands
{
    /// <summary>What a confirmation dialog says before a command runs.</summary>
    public sealed class ConfirmationRequest
    {
        public ConfirmationRequest(string titleKey, string bodyKey, string confirmKey)
        {
            TitleKey = titleKey ?? throw new ArgumentNullException(nameof(titleKey));
            BodyKey = bodyKey ?? throw new ArgumentNullException(nameof(bodyKey));
            ConfirmKey = confirmKey ?? throw new ArgumentNullException(nameof(confirmKey));
        }

        public string TitleKey { get; }

        /// <summary>What will happen when the user confirms (components.md 3.11).</summary>
        public string BodyKey { get; }

        public string ConfirmKey { get; }
    }

    /// <summary>A command that must be confirmed before it runs from a button.</summary>
    public interface IConfirmedCommand : IUiCommand
    {
        ConfirmationRequest Confirmation { get; }
    }

    /// <summary>
    /// An operation that is hard to undo, such as ending a stream. Buttons ask for confirmation with a
    /// Danger dialog before running it (UI foundation detailed design 6.4).
    /// </summary>
    public sealed class DangerCommand : IConfirmedCommand
    {
        private readonly IUiCommand _inner;

        public DangerCommand(IUiCommand inner, ConfirmationRequest confirmation)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            Confirmation = confirmation ?? throw new ArgumentNullException(nameof(confirmation));
        }

        public ConfirmationRequest Confirmation { get; }

        public bool CanExecute => _inner.CanExecute;

        public string DisabledReasonKey => _inner.DisabledReasonKey;

        public event Action CanExecuteChanged
        {
            add => _inner.CanExecuteChanged += value;
            remove => _inner.CanExecuteChanged -= value;
        }

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            return _inner.ExecuteAsync(cancellationToken);
        }
    }
}
