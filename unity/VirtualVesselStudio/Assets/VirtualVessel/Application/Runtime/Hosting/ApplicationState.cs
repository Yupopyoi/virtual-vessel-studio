namespace VirtualVessel.Application.Hosting
{
    public enum ApplicationState
    {
        NotStarted,
        Starting,
        Running,
        ShuttingDown,
        Stopped,
        Failed,
    }

    public enum ServiceCriticality
    {
        /// <summary>The application cannot be diagnosed or recovered without this service.</summary>
        Required,

        /// <summary>The application keeps running in a degraded state if this service fails.</summary>
        Optional,
    }

    public enum ApplicationServiceState
    {
        NotCreated,
        Initializing,
        Running,
        Failed,
        Skipped,
        Stopped,
    }
}
