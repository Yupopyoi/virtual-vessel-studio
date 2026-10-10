using System;
using System.Collections.Generic;
using VirtualVessel.Application.Build;
using VirtualVessel.Application.Hosting;
using VirtualVessel.Application.Session;
using VirtualVessel.Core.Threading;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.DataRoot;

namespace VirtualVessel.Application.Startup
{
    /// <summary>
    /// Everything the composition root may hand to the services it creates.
    /// </summary>
    internal sealed class ApplicationCompositionContext
    {
        public ApplicationCompositionContext(
            IDataRoot dataRoot,
            SessionInfo session,
            BuildInfo buildInfo,
            IMonotonicClock monotonicClock,
            ISystemClock systemClock,
            IMainThreadDispatcher mainThreadDispatcher)
        {
            DataRoot = dataRoot ?? throw new ArgumentNullException(nameof(dataRoot));
            Session = session ?? throw new ArgumentNullException(nameof(session));
            BuildInfo = buildInfo ?? throw new ArgumentNullException(nameof(buildInfo));
            MonotonicClock = monotonicClock ?? throw new ArgumentNullException(nameof(monotonicClock));
            SystemClock = systemClock ?? throw new ArgumentNullException(nameof(systemClock));
            MainThreadDispatcher = mainThreadDispatcher ?? throw new ArgumentNullException(nameof(mainThreadDispatcher));
        }

        public IDataRoot DataRoot { get; }

        public SessionInfo Session { get; }

        public BuildInfo BuildInfo { get; }

        public IMonotonicClock MonotonicClock { get; }

        public ISystemClock SystemClock { get; }

        public IMainThreadDispatcher MainThreadDispatcher { get; }
    }

    /// <summary>
    /// The single place that decides which services exist, in what order, and what each depends on.
    /// </summary>
    /// <remarks>
    /// Services are listed explicitly instead of discovered by reflection so that the startup order
    /// and every dependency can be reviewed in one file. Modules add their services here as they are
    /// implemented; the logging service will be the first required entry.
    /// </remarks>
    internal static class ApplicationComposition
    {
        public static IReadOnlyList<ApplicationServiceDescriptor> Create(ApplicationCompositionContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return Array.Empty<ApplicationServiceDescriptor>();
        }
    }
}
