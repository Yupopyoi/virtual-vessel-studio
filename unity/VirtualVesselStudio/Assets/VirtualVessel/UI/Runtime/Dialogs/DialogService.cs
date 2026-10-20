using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Dialogs
{
    public enum ConfirmStyle
    {
        Normal,

        /// <summary>For operations that are hard to undo: the confirm button is Danger and Cancel gets focus.</summary>
        Danger,
    }

    /// <summary>Asks the user to confirm an operation (components.md 3.11, UI foundation detailed design 6.4).</summary>
    public interface IDialogService
    {
        /// <summary>Returns true when the user confirms; false for Cancel, Esc, or a canceled token.</summary>
        /// <param name="details">Read-only content under the body, such as a model's terms of use; null for none.</param>
        Task<bool> ConfirmAsync(string titleKey, string bodyKey, string confirmKey, ConfirmStyle style = ConfirmStyle.Normal, CancellationToken cancellationToken = default, VisualElement details = null);
    }

    /// <summary>
    /// A modal confirmation dialog. While it is open, a scrim blocks the screen behind it, Tab moves only
    /// between its buttons, and Esc cancels. Focus returns to where it was when it closes. Requests made
    /// while a dialog is open wait their turn.
    /// </summary>
    public sealed class DialogService : IDialogService
    {
        public const string OverlayClass = "vv-dialog-overlay";

        private readonly SemaphoreSlim _turn = new SemaphoreSlim(1, 1);
        private VisualElement _root;

        public bool IsOpen => Current != null;

        /// <summary>The dialog on screen, or null.</summary>
        public ConfirmDialog Current { get; private set; }

        /// <summary>Which button gets focus first. Pure.</summary>
        public static bool FocusCancelFirst(ConfirmStyle style)
        {
            return style == ConfirmStyle.Danger;
        }

        public void Attach(VisualElement root)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
        }

        public async Task<bool> ConfirmAsync(string titleKey, string bodyKey, string confirmKey, ConfirmStyle style = ConfirmStyle.Normal, CancellationToken cancellationToken = default, VisualElement details = null)
        {
            if (_root == null)
            {
                throw new InvalidOperationException("The dialog service is not attached to a root.");
            }

            await _turn.WaitAsync(cancellationToken);
            try
            {
                return await ShowAsync(titleKey, bodyKey, confirmKey, style, details, cancellationToken);
            }
            finally
            {
                _turn.Release();
            }
        }

        private async Task<bool> ShowAsync(string titleKey, string bodyKey, string confirmKey, ConfirmStyle style, VisualElement details, CancellationToken cancellationToken)
        {
            var result = new TaskCompletionSource<bool>();
            Focusable previousFocus = _root.focusController?.focusedElement;
            var overlay = new VisualElement();
            overlay.AddToClassList(OverlayClass);
            var dialog = new ConfirmDialog(titleKey, bodyKey, confirmKey, style, details);
            overlay.Add(dialog);

            dialog.Confirm.clicked += () => result.TrySetResult(true);
            dialog.Cancel.clicked += () => result.TrySetResult(false);
            EventCallback<KeyDownEvent> onKey = evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    result.TrySetResult(false);
                    evt.StopPropagation();
                }
            };
            EventCallback<NavigationCancelEvent> onCancel = evt =>
            {
                result.TrySetResult(false);
                evt.StopPropagation();
            };
            EventCallback<NavigationMoveEvent> onMove = evt => TrapFocus(evt, dialog);
            _root.RegisterCallback(onKey, TrickleDown.TrickleDown);
            _root.RegisterCallback(onCancel, TrickleDown.TrickleDown);
            _root.RegisterCallback(onMove, TrickleDown.TrickleDown);

            using (cancellationToken.Register(() => result.TrySetResult(false)))
            {
                _root.Add(overlay);
                Current = dialog;
                overlay.schedule.Execute(() => (FocusCancelFirst(style) ? dialog.Cancel : dialog.Confirm).Focus());
                try
                {
                    return await result.Task;
                }
                finally
                {
                    _root.UnregisterCallback(onKey, TrickleDown.TrickleDown);
                    _root.UnregisterCallback(onCancel, TrickleDown.TrickleDown);
                    _root.UnregisterCallback(onMove, TrickleDown.TrickleDown);
                    overlay.RemoveFromHierarchy();
                    Current = null;
                    (previousFocus as VisualElement)?.Focus();
                }
            }
        }

        /// <summary>Keeps Tab and Shift+Tab between the dialog's buttons.</summary>
        private void TrapFocus(NavigationMoveEvent evt, ConfirmDialog dialog)
        {
            if (evt.direction != NavigationMoveEvent.Direction.Next && evt.direction != NavigationMoveEvent.Direction.Previous
                && evt.direction != NavigationMoveEvent.Direction.Left && evt.direction != NavigationMoveEvent.Direction.Right)
            {
                return;
            }

            bool onConfirm = dialog.Confirm.focusController?.focusedElement == dialog.Confirm;
            (onConfirm ? dialog.Cancel : dialog.Confirm).Focus();
            evt.StopPropagation();
            _root.focusController?.IgnoreEvent(evt);
        }
    }

    /// <summary>The confirmation dialog: title, what will happen, Cancel, and the confirm button.</summary>
    public sealed class ConfirmDialog : VisualElement
    {
        public const string UssClass = "vv-dialog";

        public const string DetailsClass = UssClass + "__details";

        /// <param name="details">Read-only content under the body; focus stays on the buttons.</param>
        public ConfirmDialog(string titleKey, string bodyKey, string confirmKey, ConfirmStyle style, VisualElement details = null)
        {
            AddToClassList(UssClass);
            var title = new Label();
            title.AddToClassList(UssClass + "__title");
            title.AddToClassList("vv-bold");
            var body = new Label();
            body.AddToClassList(UssClass + "__body");
            body.AddToClassList("vv-text-body");
            new KeyedText(title, text => title.text = text) { Key = titleKey };
            new KeyedText(body, text => body.text = text) { Key = bodyKey };
            Add(title);
            Add(body);
            if (details != null)
            {
                details.AddToClassList(DetailsClass);
                Add(details);
            }

            var buttons = new VisualElement();
            buttons.AddToClassList(UssClass + "__buttons");
            Cancel = new ActionButton(CommonStrings.Cancel, ButtonVariant.Secondary);
            Confirm = new ActionButton(confirmKey, style == ConfirmStyle.Danger ? ButtonVariant.Danger : ButtonVariant.Primary);
            buttons.Add(Cancel);
            buttons.Add(Confirm);
            Add(buttons);
        }

        public ActionButton Cancel { get; }

        public ActionButton Confirm { get; }
    }
}
