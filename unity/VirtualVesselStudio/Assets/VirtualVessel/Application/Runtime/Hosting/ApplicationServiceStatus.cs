using System;

namespace VirtualVessel.Application.Hosting
{
    /// <summary>
    /// Read-only view of one service's lifecycle, exposed to diagnostics.
    /// </summary>
    public sealed class ApplicationServiceStatus
    {
        internal ApplicationServiceStatus(string name, ServiceCriticality criticality)
        {
            Name = name;
            Criticality = criticality;
            State = ApplicationServiceState.NotCreated;
        }

        public string Name { get; }

        public ServiceCriticality Criticality { get; }

        public ApplicationServiceState State { get; internal set; }

        public TimeSpan InitializeDuration { get; internal set; }

        /// <summary>Why the service failed or was skipped; null otherwise.</summary>
        public string FailureReason { get; internal set; }
    }
}
