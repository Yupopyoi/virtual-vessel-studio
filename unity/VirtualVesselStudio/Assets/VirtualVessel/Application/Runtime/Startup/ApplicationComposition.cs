using System;
using System.Collections.Generic;
using System.IO;
using VirtualVessel.Application.Build;
using VirtualVessel.Application.Hosting;
using VirtualVessel.Application.Logging;
using VirtualVessel.Application.Session;
using VirtualVessel.Core.Threading;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.ProjectData.DataRoot;

namespace VirtualVessel.Application.Startup
{
    /// <summary>
    /// Where the application is running. Decided by the Unity entry point.
    /// </summary>
    public enum RuntimeEnvironment
    {
        /// <summary>Play Mode inside the Unity Editor.</summary>
        Editor,

        /// <summary>A built application.</summary>
        Player,
    }

    /// <summary>
    /// Everything the composition root may hand to the services it creates.
    /// </summary>
    internal sealed class ApplicationCompositionContext
    {
        public ApplicationCompositionContext(
            IDataRoot dataRoot,
            SessionInfo session,
            BuildInfo buildInfo,
            RuntimeEnvironment environment,
            IMonotonicClock monotonicClock,
            ISystemClock systemClock,
            IMainThreadDispatcher mainThreadDispatcher,
            BufferedApplicationLog foundationLog)
        {
            DataRoot = dataRoot ?? throw new ArgumentNullException(nameof(dataRoot));
            Session = session ?? throw new ArgumentNullException(nameof(session));
            BuildInfo = buildInfo ?? throw new ArgumentNullException(nameof(buildInfo));
            Environment = environment;
            MonotonicClock = monotonicClock ?? throw new ArgumentNullException(nameof(monotonicClock));
            SystemClock = systemClock ?? throw new ArgumentNullException(nameof(systemClock));
            MainThreadDispatcher = mainThreadDispatcher ?? throw new ArgumentNullException(nameof(mainThreadDispatcher));
            FoundationLog = foundationLog ?? throw new ArgumentNullException(nameof(foundationLog));
        }

        public IDataRoot DataRoot { get; }

        public SessionInfo Session { get; }

        public BuildInfo BuildInfo { get; }

        public RuntimeEnvironment Environment { get; }

        public IMonotonicClock MonotonicClock { get; }

        public ISystemClock SystemClock { get; }

        public IMainThreadDispatcher MainThreadDispatcher { get; }

        public BufferedApplicationLog FoundationLog { get; }
    }

    /// <summary>
    /// The single place that decides which services exist, in what order, and what each depends on.
    /// </summary>
    /// <remarks>
    /// Services are listed explicitly instead of discovered by reflection so that the startup order
    /// and every dependency can be reviewed in one file. Factories run in this order, so a later
    /// factory can use an instance created by an earlier one, such as the log provider.
    /// </remarks>
    internal static class ApplicationComposition
    {
        public const string EditorLogFolder = "Editor";

        public const string ApplicationLogFolder = "Application";

        public static IReadOnlyList<ApplicationServiceDescriptor> Create(ApplicationCompositionContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var descriptors = new List<ApplicationServiceDescriptor>();

            // Logging starts first and stops last so that every other service's lifecycle is recorded.
            // Services added later keep the created instance in a local, receive its Provider in their
            // factories, and list LoggingService.ServiceName in their dependencies.
            ApplicationLoggingService logging = null;
            descriptors.Add(new ApplicationServiceDescriptor(
                LoggingService.ServiceName,
                ServiceCriticality.Required,
                () => logging = CreateLogging(context)));

            // Metrics come right after logging so that they stop before it and their totals reach the log.
            // Services that measure will keep this instance in a local, as with logging, and pass it
            // to their factories as IPerformanceMetrics.
            descriptors.Add(new ApplicationServiceDescriptor(
                PerformanceMetricsService.ServiceName,
                ServiceCriticality.Optional,
                () => new PerformanceMetricsService(
                    CreatePerformanceSettings(context.Environment),
                    context.MonotonicClock,
                    context.SystemClock,
                    logging.Provider.GetLog("Diagnostics", "Performance")),
                new[] { LoggingService.ServiceName }));

            return descriptors;
        }

        public static string GetLogDirectory(IDataRoot dataRoot, RuntimeEnvironment environment)
        {
            // Editor Play Mode runs are frequent during development; keeping them apart stops them
            // from pushing logs of real use out of retention (logging detailed design 7.3).
            string folder = environment == RuntimeEnvironment.Editor ? EditorLogFolder : ApplicationLogFolder;
            return Path.Combine(dataRoot.GetDirectory(DataRootDirectory.Logs), folder);
        }

        /// <summary>
        /// Detailed metrics default to on in the Editor and off in built applications until the
        /// Developer Mode setting exists (performance metrics detailed design 7.1).
        /// </summary>
        public static PerformanceSettings CreatePerformanceSettings(RuntimeEnvironment environment)
        {
            return new PerformanceSettings
            {
                DetailedEnabled = environment == RuntimeEnvironment.Editor,
            };
        }

        private static ApplicationLoggingService CreateLogging(ApplicationCompositionContext context)
        {
            var header = new LogSessionHeader(
                context.Session.SessionId,
                context.Session.StartedAtUtc,
                context.BuildInfo.ApplicationVersion,
                context.BuildInfo.BuildIdentifier,
                context.BuildInfo.UnityVersion,
                context.BuildInfo.OperatingSystem);

            var service = new LoggingService(
                new LoggingSettings(),
                GetLogDirectory(context.DataRoot, context.Environment),
                header,
                context.SystemClock,
                context.MonotonicClock);

            return new ApplicationLoggingService(service, context.FoundationLog);
        }
    }
}
