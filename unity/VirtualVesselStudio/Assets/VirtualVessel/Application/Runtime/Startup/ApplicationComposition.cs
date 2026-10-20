using System;
using System.Collections.Generic;
using System.IO;
using VirtualVessel.Application.Build;
using VirtualVessel.Application.Hosting;
using VirtualVessel.Application.Logging;
using VirtualVessel.Application.Session;
using VirtualVessel.Avatar;
using VirtualVessel.Avatar.Vrm;
using VirtualVessel.Capture;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Threading;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.ProjectData.DataRoot;
using VirtualVessel.Stage;
using VirtualVessel.Tracking;
#if VV_MEDIAPIPE
using VirtualVessel.Tracking.MediaPipe;
#endif
using VirtualVessel.UI;
using VirtualVessel.UI.Settings;
using VirtualVessel.Video;

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
            BufferedApplicationLog foundationLog,
            string configDirectory = null,
            UiRoot uiRoot = null,
            IFrameLoop frameLoop = null,
            UnityEngine.Camera mainCamera = null)
        {
            DataRoot = dataRoot ?? throw new ArgumentNullException(nameof(dataRoot));
            Session = session ?? throw new ArgumentNullException(nameof(session));
            BuildInfo = buildInfo ?? throw new ArgumentNullException(nameof(buildInfo));
            Environment = environment;
            MonotonicClock = monotonicClock ?? throw new ArgumentNullException(nameof(monotonicClock));
            SystemClock = systemClock ?? throw new ArgumentNullException(nameof(systemClock));
            MainThreadDispatcher = mainThreadDispatcher ?? throw new ArgumentNullException(nameof(mainThreadDispatcher));
            FoundationLog = foundationLog ?? throw new ArgumentNullException(nameof(foundationLog));
            ConfigDirectory = configDirectory;
            UiRoot = uiRoot;
            FrameLoop = frameLoop;
            MainCamera = mainCamera;
        }

        public IDataRoot DataRoot { get; }

        public SessionInfo Session { get; }

        public BuildInfo BuildInfo { get; }

        public RuntimeEnvironment Environment { get; }

        public IMonotonicClock MonotonicClock { get; }

        public ISystemClock SystemClock { get; }

        public IMainThreadDispatcher MainThreadDispatcher { get; }

        public BufferedApplicationLog FoundationLog { get; }

        /// <summary>Folder of the small settings files such as ui-settings.json (system design 6.8).</summary>
        public string ConfigDirectory { get; }

        /// <summary>The UI entry point in the persistent scene; null when there is no UI, as in tests.</summary>
        public UiRoot UiRoot { get; }

        /// <summary>Per-frame work on the main thread; null where nothing drives frames, as in some tests.</summary>
        public IFrameLoop FrameLoop { get; }

        /// <summary>The persistent scene's Main Camera, which renders the stream video; null in tests without one.</summary>
        public UnityEngine.Camera MainCamera { get; }
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
            PerformanceMetricsService metrics = null;
            descriptors.Add(new ApplicationServiceDescriptor(
                PerformanceMetricsService.ServiceName,
                ServiceCriticality.Optional,
                () => metrics = new PerformanceMetricsService(
                    CreatePerformanceSettings(context.Environment),
                    context.MonotonicClock,
                    context.SystemClock,
                    logging.Provider.GetLog("Diagnostics", "Performance")),
                new[] { LoggingService.ServiceName }));

            // Capture is Optional: without a capture board or a monitor everything else keeps working
            // (capture detailed design 7.1). Its sources are updated every frame, so it needs the frame
            // loop, which tests without frames leave out.
            CaptureService capture = null;
            if (context.FrameLoop != null)
            {
                descriptors.Add(new ApplicationServiceDescriptor(
                    CaptureService.ServiceName,
                    ServiceCriticality.Optional,
                    // The settings live in capture-settings.json next to the UI settings (capture
                    // detailed design 10); without a settings folder nothing is saved.
                    () => capture = context.ConfigDirectory != null
                        ? CaptureService.Create(
                            CaptureService.CreateStore(context.ConfigDirectory, context.SystemClock),
                            context.FrameLoop,
                            context.MonotonicClock,
                            metrics,
                            logging.Provider.GetLog("Capture", "Sources"))
                        : CaptureService.Create(
                            new CaptureSettings(),
                            context.FrameLoop,
                            context.MonotonicClock,
                            metrics,
                            logging.Provider.GetLog("Capture", "Sources")),
                    new[] { LoggingService.ServiceName }));
            }

            // Video renders the stream with the persistent scene's Main Camera (video detailed design
            // 7.1). Optional: without it there is no stream video, and everything else runs.
            VideoService video = null;
            StageService stage = null;
            if (context.MainCamera != null && context.FrameLoop != null)
            {
                descriptors.Add(new ApplicationServiceDescriptor(
                    VideoService.ServiceName,
                    ServiceCriticality.Optional,
                    () => video = new VideoService(
                        new VideoSettings(),
                        context.MainCamera,
                        context.FrameLoop,
                        context.MonotonicClock,
                        metrics,
                        logging.Provider.GetLog("Video", "Stream")),
                    new[] { LoggingService.ServiceName }));

                // Stage puts the capture videos on the stream screen (Stage detailed design 7), so it
                // needs both; when either does not start, the host does not create it.
                if (context.ConfigDirectory != null)
                {
                    descriptors.Add(new ApplicationServiceDescriptor(
                        StageService.ServiceName,
                        ServiceCriticality.Optional,
                        () => stage = new StageService(
                            capture,
                            video,
                            context.FrameLoop,
                            StageService.CreateStore(context.ConfigDirectory, context.SystemClock),
                            context.MonotonicClock,
                            logging.Provider.GetLog("Stage", "Screens")),
                        new[] { LoggingService.ServiceName, CaptureService.ServiceName, VideoService.ServiceName }));
                }
            }

            // Tracking runs on the tracking camera, one of the capture sources (tracking detailed design
            // 5.3). Optional: without it the avatar keeps its rest pose and everything else runs.
            TrackingService tracking = null;
            if (context.FrameLoop != null && context.ConfigDirectory != null)
            {
                descriptors.Add(new ApplicationServiceDescriptor(
                    TrackingService.ServiceName,
                    ServiceCriticality.Optional,
                    () => tracking = CreateTracking(context, capture, metrics, logging.Provider.GetLog("Tracking", "Source")),
                    new[] { LoggingService.ServiceName, CaptureService.ServiceName }));
            }

            // Avatar puts the chosen avatar on the stream (avatar detailed design 8.1): on Video's avatar
            // layer at Stage's spawn point, so it needs both. Optional: without it the stream has no avatar.
            AvatarService avatar = null;
            if (context.MainCamera != null && context.FrameLoop != null && context.ConfigDirectory != null)
            {
                descriptors.Add(new ApplicationServiceDescriptor(
                    AvatarService.ServiceName,
                    ServiceCriticality.Optional,
                    () => avatar = CreateAvatar(context, video, stage, metrics, logging.Provider.GetLog("Avatar", "Avatars")),
                    new[] { LoggingService.ServiceName, VideoService.ServiceName, StageService.ServiceName }));
            }

            // The UI comes last so that it is built after every service it shows has started, and it
            // stops first. It is Optional: without a UI the bootstrap shows the startup problem screen.
            if (context.UiRoot != null && context.ConfigDirectory != null)
            {
                descriptors.Add(new ApplicationServiceDescriptor(
                    UiApplicationService.ServiceName,
                    ServiceCriticality.Optional,
                    () => new UiApplicationService(
                        context.UiRoot,
                        new UiRootContext(
                            context.ConfigDirectory,
                            context.MonotonicClock,
                            context.SystemClock,
                            context.MainThreadDispatcher,
                            logging.Provider.GetLog("UI", "Root"),
                            SystemMotionPreference.PrefersReducedMotion(),
                            context.BuildInfo.ApplicationVersion,
                            metrics,

                            // Detailed metrics follow Developer Mode once the UI is up (performance metrics detailed design 7.1).
                            metrics == null ? null : new Action<bool>[] { metrics.SetDetailedEnabled },

                            // The live and setup screens (docs/<lang>/ui/screens/). Services that are not
                            // there leave their part of the screens saying so.
                            video?.Preview,
                            stage?.Screens,
                            capture,
                            tracking)),
                    new[] { LoggingService.ServiceName }));
            }

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

        private static AvatarService CreateAvatar(ApplicationCompositionContext context, VideoService video, StageService stage, IPerformanceMetrics metrics, ILog log)
        {
            ConfigFileStore<AvatarSettings> store = AvatarService.CreateStore(context.ConfigDirectory, context.SystemClock);
            ConfigLoadResult<AvatarSettings> loaded = store.Load();
            log?.Information($"Avatar settings {loaded.Status} from {store.FilePath}.");
            var saves = new ConfigSaveScheduler<AvatarSettings>(store, context.MonotonicClock, TimeSpan.FromSeconds(0.5), failure => log?.Warning($"Failed to save the avatar settings to {store.FilePath}.", failure));

            // UniVRM stays behind VrmAvatarLoader; the rest of Avatar sees only IAvatarLoader (avatar detailed design 3).
            return new AvatarService(
                new VrmAvatarLoader(),
                new AvatarStore(context.DataRoot.GetDirectory(DataRootDirectory.Avatars), context.SystemClock),
                loaded.Value,
                saves,
                () => new AvatarPlacement(video.AvatarLayer, stage.SpawnPoints.Default),
                context.FrameLoop,
                context.MonotonicClock,
                context.SystemClock,
                metrics,
                log);
        }

        private static TrackingService CreateTracking(ApplicationCompositionContext context, CaptureService capture, IPerformanceMetrics metrics, ILog log)
        {
            ConfigFileStore<TrackingSettings> store = TrackingService.CreateStore(context.ConfigDirectory, context.SystemClock);
            ConfigLoadResult<TrackingSettings> loaded = store.Load();
            TrackingSettings settings = loaded.Value ?? new TrackingSettings();
            log?.Information($"Tracking settings {loaded.Status} from {store.FilePath}.");
            var saves = new ConfigSaveScheduler<TrackingSettings>(store, context.MonotonicClock, TimeSpan.FromSeconds(0.5), failure => log?.Warning($"Failed to save the tracking settings to {store.FilePath}.", failure));
            IVideoCaptureSource camera = capture?.Find(CaptureService.TrackingCameraId);
            return new TrackingService(CreateTrackingProvider(context, camera, settings, metrics, log), camera, context.FrameLoop, context.MonotonicClock, settings, saves, metrics, log);
        }

        /// <summary>
        /// The MediaPipe provider when the plugin is installed. Without it the project still compiles
        /// and runs, and tracking reports that it cannot start (tracking detailed design 16.2).
        /// </summary>
        private static ITrackingProvider CreateTrackingProvider(ApplicationCompositionContext context, IVideoCaptureSource camera, TrackingSettings settings, IPerformanceMetrics metrics, ILog log)
        {
            if (camera == null)
            {
                return null;
            }

#if VV_MEDIAPIPE
            string models = Path.Combine(UnityEngine.Application.streamingAssetsPath, MediaPipeTracking.ModelFolder);
            return MediaPipeTracking.Create(camera, models, settings.PoseModel, context.MonotonicClock, metrics, log);
#else
            log?.Warning("MediaPipeUnityPlugin is not installed, so tracking is unavailable. Run tools/deps/fetch-mediapipe.ps1.");
            return null;
#endif
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
