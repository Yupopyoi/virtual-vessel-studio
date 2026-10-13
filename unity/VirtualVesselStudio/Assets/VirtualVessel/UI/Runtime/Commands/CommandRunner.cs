using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Commands
{
    /// <summary>
    /// Runs UI operations and reports failures: a message for the user and details for developers
    /// (UI foundation detailed design 7.5).
    /// </summary>
    public static class CommandRunner
    {
        /// <summary>Runs <paramref name="command"/>. Returns false if it failed; the failure has been reported.</summary>
        public static Task<bool> RunAsync(IUiCommand command, UiServices services)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            return RunAsync(() => command.ExecuteAsync(CancellationToken.None), services);
        }

        /// <summary>Runs <paramref name="operation"/>. Returns false if it failed; the failure has been reported.</summary>
        public static async Task<bool> RunAsync(Func<Task> operation, UiServices services)
        {
            try
            {
                await operation();
                return true;
            }
            catch (Exception exception)
            {
                Report(exception, services);
                return false;
            }
        }

        /// <summary>
        /// The message to show for a failure: the key of a <see cref="UserFacingException"/>, the general
        /// failure message for anything else, or null for a cancellation, which the user asked for.
        /// </summary>
        public static string MessageKeyFor(Exception exception)
        {
            if (exception is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
            {
                exception = aggregate.InnerExceptions[0];
            }

            return exception switch
            {
                OperationCanceledException _ => null,
                UserFacingException userFacing => userFacing.MessageKey,
                _ => CommonStrings.UnexpectedError,
            };
        }

        private static void Report(Exception exception, UiServices services)
        {
            string messageKey = MessageKeyFor(exception);
            if (messageKey == null)
            {
                return;
            }

            if (services?.Log != null)
            {
                services.Log.Error($"A UI operation failed; showing '{messageKey}'.", exception);
            }
            else
            {
                // Outside a scope (tests, tools) a failure must still be visible somewhere.
                Debug.LogException(exception);
            }

            services?.ShowFailure?.Invoke(messageKey);
        }
    }
}
