using VirtualVessel.Application.Hosting;
using VirtualVessel.UI;

namespace VirtualVessel.Application.Startup
{
    /// <summary>
    /// Decides whether the user must see the startup problem screen (application foundation detailed
    /// design 8.1, UI foundation detailed design 7.9).
    /// </summary>
    internal static class StartupProblemPolicy
    {
        /// <param name="state">The application state after startup finished.</param>
        /// <param name="dataRootUnavailable">Startup stopped because the Data Root could not be used.</param>
        /// <param name="uiState">The UI service's state, or null when it was never registered.</param>
        /// <returns>The kind of screen to show, or null when the normal UI is up or shutdown has begun.</returns>
        public static StartupProblemKind? Decide(ApplicationState state, bool dataRootUnavailable, ApplicationServiceState? uiState)
        {
            if (dataRootUnavailable)
            {
                return StartupProblemKind.DataRootUnavailable;
            }

            if (state == ApplicationState.Failed)
            {
                return StartupProblemKind.StartupFailed;
            }

            if (state == ApplicationState.Running && uiState != ApplicationServiceState.Running)
            {
                return StartupProblemKind.StartupFailed;
            }

            return null;
        }
    }
}
