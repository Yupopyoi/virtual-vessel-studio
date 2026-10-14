using System;
using UnityEngine.UIElements;
using VirtualVessel.Core.Threading;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// The services that common components need: text lookup, the main thread, and failure reporting.
    /// Components find them through the nearest <see cref="UiServicesScope"/> above them.
    /// </summary>
    public sealed class UiServices
    {
        /// <param name="dispatcher">Optional; without it, observable changes are applied on the calling thread.</param>
        /// <param name="log">Optional; records command failures for developers.</param>
        /// <param name="showFailure">
        /// Optional; shows a user-facing message (a string table key) when a command fails, normally
        /// <c>NotificationService.ShowFailure</c>.
        /// </param>
        /// <param name="dialogs">Optional; asks for confirmation before a <c>DangerCommand</c> runs.</param>
        public UiServices(ILocalizer localizer, IMainThreadDispatcher dispatcher, ILog log, Action<string> showFailure = null, IDialogService dialogs = null)
        {
            Dialogs = dialogs;
            Localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
            Dispatcher = dispatcher;
            Log = log;
            ShowFailure = showFailure;
        }

        public ILocalizer Localizer { get; }

        public IMainThreadDispatcher Dispatcher { get; }

        public ILog Log { get; }

        public Action<string> ShowFailure { get; }

        public IDialogService Dialogs { get; }

        /// <summary>The services of the nearest scope above <paramref name="element"/>, or null outside any scope.</summary>
        public static UiServices Find(VisualElement element)
        {
            for (VisualElement current = element; current != null; current = current.hierarchy.parent)
            {
                if (current is UiServicesScope scope)
                {
                    return scope.Services;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Makes <see cref="UiServices"/> available to every component inside it. The UI root creates one at
    /// the top of the screen; there is no global instance.
    /// </summary>
    public sealed class UiServicesScope : VisualElement
    {
        public UiServicesScope(UiServices services)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            style.flexGrow = 1;
        }

        public UiServices Services { get; }
    }
}
