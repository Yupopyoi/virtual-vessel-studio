using System;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.State;

namespace VirtualVessel.UI.Commands
{
    /// <summary>An operation started from the UI (UI foundation detailed design 7.5).</summary>
    public interface IUiCommand
    {
        bool CanExecute { get; }

        /// <summary>The string table key explaining why the command cannot run, or null when it can.</summary>
        string DisabledReasonKey { get; }

        /// <summary>Raised when <see cref="CanExecute"/> may have changed. May be raised on any thread.</summary>
        event Action CanExecuteChanged;

        Task ExecuteAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// A command made from a delegate. It can run while <c>disabledReasonKey</c> is null; otherwise the
    /// key says why not, and buttons show it.
    /// </summary>
    public sealed class UiCommand : IUiCommand
    {
        private readonly Func<CancellationToken, Task> _execute;
        private readonly IReadOnlyObservable<string> _disabledReasonKey;
        private Action _canExecuteChanged;
        private IDisposable _subscription;

        public UiCommand(Func<CancellationToken, Task> execute, IReadOnlyObservable<string> disabledReasonKey = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _disabledReasonKey = disabledReasonKey;
        }

        /// <summary>A command that runs a synchronous action.</summary>
        public UiCommand(Action execute, IReadOnlyObservable<string> disabledReasonKey = null)
            : this(ToAsync(execute), disabledReasonKey)
        {
        }

        public bool CanExecute => DisabledReasonKey == null;

        public string DisabledReasonKey => _disabledReasonKey?.Value;

        // The state is watched only while someone listens, so an unused command holds no subscription.
        public event Action CanExecuteChanged
        {
            add
            {
                if (_canExecuteChanged == null && _disabledReasonKey != null)
                {
                    _subscription = _disabledReasonKey.Subscribe(_ => _canExecuteChanged?.Invoke());
                }

                _canExecuteChanged += value;
            }

            remove
            {
                _canExecuteChanged -= value;
                if (_canExecuteChanged == null)
                {
                    _subscription?.Dispose();
                    _subscription = null;
                }
            }
        }

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            return _execute(cancellationToken);
        }

        private static Func<CancellationToken, Task> ToAsync(Action execute)
        {
            if (execute == null)
            {
                throw new ArgumentNullException(nameof(execute));
            }

            return _ =>
            {
                execute();
                return Task.CompletedTask;
            };
        }
    }

    /// <summary>
    /// A failure with a message for the user, given as a string table key. Other exceptions show the
    /// general failure message.
    /// </summary>
    public sealed class UserFacingException : Exception
    {
        public UserFacingException(string messageKey, string developerMessage = null, Exception innerException = null)
            : base(developerMessage ?? messageKey, innerException)
        {
            MessageKey = messageKey ?? throw new ArgumentNullException(nameof(messageKey));
        }

        public string MessageKey { get; }
    }
}
