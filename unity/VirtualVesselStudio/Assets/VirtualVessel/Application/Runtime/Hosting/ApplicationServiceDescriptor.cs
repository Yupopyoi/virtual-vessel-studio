using System;
using System.Collections.Generic;
using VirtualVessel.Core.Lifecycle;

namespace VirtualVessel.Application.Hosting
{
    /// <summary>
    /// Describes how the host creates one service and what it depends on.
    /// </summary>
    /// <remarks>
    /// Factories run in composition order, so a factory may use instances created by earlier factories.
    /// Listing those services in <see cref="DependsOn"/> lets the host skip this service when a
    /// dependency failed instead of handing it a broken instance.
    /// </remarks>
    internal sealed class ApplicationServiceDescriptor
    {
        public static readonly TimeSpan DefaultInitializeTimeout = TimeSpan.FromSeconds(10);

        public ApplicationServiceDescriptor(
            string name,
            ServiceCriticality criticality,
            Func<IApplicationService> factory,
            IReadOnlyList<string> dependsOn = null,
            TimeSpan? initializeTimeout = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Service name must not be empty.", nameof(name));
            }

            Name = name;
            Criticality = criticality;
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            DependsOn = dependsOn ?? Array.Empty<string>();
            InitializeTimeout = initializeTimeout ?? DefaultInitializeTimeout;
        }

        public string Name { get; }

        public ServiceCriticality Criticality { get; }

        public Func<IApplicationService> Factory { get; }

        public IReadOnlyList<string> DependsOn { get; }

        public TimeSpan InitializeTimeout { get; }
    }
}
