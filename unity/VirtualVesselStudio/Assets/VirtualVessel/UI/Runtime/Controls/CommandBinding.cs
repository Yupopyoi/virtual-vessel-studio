using System;
using UnityEngine.UIElements;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Commands;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// Connects a button to a command: enabled only when the command can run, disabled while it runs
    /// (no double press), and the reason it cannot run kept for the tooltip (UI foundation detailed design 7.5).
    /// </summary>
    internal sealed class CommandBinding
    {
        public const string BusyClass = "vv-busy";

        private readonly Button _button;
        private IUiCommand _command;
        private IDisposable _lifetime;
        private bool _running;

        public CommandBinding(Button button)
        {
            _button = button;
            _button.clicked += OnClicked;
        }

        /// <summary>The reason shown instead of the normal tooltip, or null.</summary>
        public string DisabledReasonKey { get; private set; }

        public void Set(IUiCommand command)
        {
            _lifetime?.Dispose();
            _command = command;
            _lifetime = command == null ? null : PanelLifetime.WhileAttached(_button, services =>
            {
                Action onChanged = () => MainThread.Run(services, Refresh);
                command.CanExecuteChanged += onChanged;
                Refresh();
                return new DisposeAction(() => command.CanExecuteChanged -= onChanged);
            });
            Refresh();
        }

        /// <summary>Whether the button can be pressed. Pure.</summary>
        public static bool IsEnabled(bool hasCommand, bool canExecute, bool running)
        {
            return !hasCommand || (canExecute && !running);
        }

        private void Refresh()
        {
            bool canExecute = _command?.CanExecute ?? true;
            DisabledReasonKey = _command != null && !canExecute ? _command.DisabledReasonKey : null;
            _button.SetEnabled(IsEnabled(_command != null, canExecute, _running));
            _button.EnableInClassList(BusyClass, _running);
        }

        private async void OnClicked()
        {
            IUiCommand command = _command;
            if (command == null || _running || !command.CanExecute)
            {
                return;
            }

            _running = true;
            Refresh();
            try
            {
                await CommandRunner.RunAsync(command, UiServices.Find(_button));
            }
            finally
            {
                _running = false;
                Refresh();
            }
        }
    }
}
