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
using VirtualVessel.ProjectData.Projects;
using VirtualVessel.Stage;
using VirtualVessel.Tracking;
using VirtualVessel.Tracking.Recording;
#if VV_MEDIAPIPE
using VirtualVessel.Tracking.MediaPipe;
#endif
using VirtualVessel.UI;
using VirtualVessel.UI.Credits;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Screens;
using VirtualVessel.UI.Settings;
using VirtualVessel.Video;
using VirtualVessel.Video.Recording;

namespace VirtualVessel.Application.Startup
{
    /// <summary>How each module's service is made, from its settings and the services it needs.</summary>
    internal static partial class ApplicationComposition
    {
        /// <summary>
        /// Opens the project in use (project data detailed design 7). The settings files of the version
        /// before projects are copied into the first one: tracking and capture as its profiles, the avatar
        /// and the stream screen's layout as the project's own.
        /// </summary>
        private static OpenedProject OpenProject(ApplicationCompositionContext context, ILog log)
        {
            var legacy = new LegacySettings(
                new Dictionary<ProfileKind, string> { [ProfileKind.Tracking] = TrackingSettings.FileName, [ProfileKind.Capture] = CaptureSettings.FileName },
                new[] { AvatarSettings.FileName, StageSettings.FileName });
            var folders = new ProjectFolders(context.ConfigDirectory, context.DataRoot.GetDirectory(DataRootDirectory.Projects), context.DataRoot.GetDirectory(DataRootDirectory.Profiles));
            OpenedProject project = ProjectOpener.Open(folders, legacy, context.SystemClock, ProjectOpener.NewId);
            foreach (string note in project.Notes)
            {
                log?.Information(note);
            }

            log?.Information($"Project {project.ProjectId} ({project.Manifest.DisplayName}) open: tracking profile {project.Manifest.TrackingProfileId}, capture profile {project.Manifest.CaptureProfileId}, streaming profile {project.Manifest.StreamingProfileId}.");
            return project;
        }

        /// <summary>How long after a change settings are saved, so a slider being dragged is written once.</summary>
        private static readonly TimeSpan s_saveDelay = TimeSpan.FromSeconds(0.5);

        /// <summary>
        /// Reads a module's settings from <paramref name="store"/>, logs where they came from, and makes the
        /// scheduler that saves its changes. <paramref name="what"/> names the module in the log.
        /// </summary>
        private static (T Value, ConfigSaveScheduler<T> Saves) LoadSettings<T>(ConfigFileStore<T> store, string what, ApplicationCompositionContext context, ILog log)
            where T : class
        {
            ConfigLoadResult<T> loaded = store.Load();
            log?.Information($"{what} settings {loaded.Status} from {store.FilePath}.");
            var saves = new ConfigSaveScheduler<T>(store, context.MonotonicClock, s_saveDelay, failure => log?.Warning($"Failed to save the {what.ToLowerInvariant()} settings to {store.FilePath}.", failure));
            return (loaded.Value, saves);
        }

        /// <summary>
        /// The project's streaming profile, or its defaults unsaved when there is no settings folder (tests).
        /// A value out of range falls back to its default (<see cref="RecordingSettings.Usable"/>).
        /// </summary>
        private static (RecordingSettings Value, ConfigSaveScheduler<RecordingSettings> Saves) LoadStreaming(ApplicationCompositionContext context, OpenedProject project, ILog log)
        {
            if (project == null)
            {
                return (new RecordingSettings(), null);
            }

            (RecordingSettings settings, ConfigSaveScheduler<RecordingSettings> saves) = LoadSettings(
                RecordingService.CreateStore(project.ProfileDirectory(ProfileKind.Streaming), context.SystemClock, project.ProfileFileName(ProfileKind.Streaming)),
                "Streaming",
                context,
                log);
            return ((settings ?? new RecordingSettings()).Usable(), saves);
        }

        /// <summary>The stream video in the streaming profile's size; the rest as Video's defaults. Pure.</summary>
        public static VideoSettings VideoSettingsOf(RecordingSettings streaming)
        {
            return new VideoSettings { Width = streaming.Width, Height = streaming.Height };
        }

        private static AvatarService CreateAvatar(ApplicationCompositionContext context, OpenedProject project, VideoService video, StageService stage, TrackingService tracking, IPerformanceMetrics metrics, ILog log)
        {
            (AvatarSettings settings, ConfigSaveScheduler<AvatarSettings> saves) = LoadSettings(AvatarService.CreateStore(project.Directory, context.SystemClock), "Avatar", context, log);

            // UniVRM stays behind VrmAvatarLoader; the rest of Avatar sees only IAvatarLoader (avatar detailed design 3).
            return new AvatarService(
                new VrmAvatarLoader(),
                new AvatarStore(context.DataRoot.GetDirectory(DataRootDirectory.Avatars), context.SystemClock),
                settings,
                saves,
                () => new AvatarPlacement(video.AvatarLayer, stage.SpawnPoints.Default),
                context.FrameLoop,
                context.MonotonicClock,
                context.SystemClock,
                metrics,
                log,
                tracking);
        }

        private static TrackingService CreateTracking(ApplicationCompositionContext context, OpenedProject project, CaptureService capture, IPerformanceMetrics metrics, ILog log)
        {
            (TrackingSettings settings, ConfigSaveScheduler<TrackingSettings> saves) = LoadSettings(
                TrackingService.CreateStore(project.ProfileDirectory(ProfileKind.Tracking), context.SystemClock, project.ProfileFileName(ProfileKind.Tracking)),
                "Tracking",
                context,
                log);
            IVideoCaptureSource camera = capture?.Find(CaptureService.TrackingCameraId);
            return new TrackingService(CreateTrackingProvider(context, camera, metrics, log), camera, context.FrameLoop, context.MonotonicClock, settings ?? new TrackingSettings(), saves, metrics, log);
        }

        /// <summary>
        /// The MediaPipe provider when the plugin is installed. Without it the project still compiles
        /// and runs, and tracking reports that it cannot start (tracking detailed design 16.2).
        /// </summary>
        private static ITrackingProvider CreateTrackingProvider(ApplicationCompositionContext context, IVideoCaptureSource camera, IPerformanceMetrics metrics, ILog log)
        {
            if (camera == null)
            {
                return null;
            }

#if VV_MEDIAPIPE
            string models = Path.Combine(UnityEngine.Application.streamingAssetsPath, MediaPipeTracking.ModelFolder);
            return MediaPipeTracking.Create(camera, models, context.MonotonicClock, metrics, log);
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
