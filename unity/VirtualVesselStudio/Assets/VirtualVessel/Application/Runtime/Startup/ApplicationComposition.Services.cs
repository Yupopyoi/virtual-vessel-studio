using System;
using System.Collections.Generic;
using System.IO;
using VirtualVessel.Application.Build;
using VirtualVessel.Application.Hosting;
using VirtualVessel.Application.Logging;
using VirtualVessel.Application.Session;
using VirtualVessel.Audio;
using VirtualVessel.Avatar;
using VirtualVessel.Capture;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Threading;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.ProjectData.Credentials;
using VirtualVessel.ProjectData.DataRoot;
using VirtualVessel.ProjectData.Projects;
using VirtualVessel.Stage;
using VirtualVessel.Streaming;
using VirtualVessel.Tracking;
using VirtualVessel.Tracking.Recording;
using VirtualVessel.UI;
using VirtualVessel.UI.Credits;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Recording;
using VirtualVessel.UI.Screens;
using VirtualVessel.UI.Settings;
using VirtualVessel.UI.Streaming;
using VirtualVessel.UI.VoiceLab;
using VirtualVessel.Video;
using VirtualVessel.Video.Recording;
using VirtualVessel.VoiceLab;
using VirtualVessel.VoiceLab.Activation;
using VirtualVessel.Voice;
using VirtualVessel.Voice.Models;

namespace VirtualVessel.Application.Startup
{
    // One method per service, or group of services, that Create lists, in its order.
    internal static partial class ApplicationComposition
    {
        /// <summary>
        /// The services as their factories make them. Factories run in the order they are listed, so a later one reads
        /// an earlier one's instance here; one that did not start stays null.
        /// </summary>
        private sealed class Built
        {
            public ApplicationLoggingService Logging;
            public PerformanceMetricsService Metrics;
            public Lazy<OpenedProject> Project;
            public CaptureService Capture;
            public AudioService Audio;
            public VoiceService Voice;
            public VideoService Video;
            public StageService Stage;
            public RecordingService Recording;
            public StreamingService Streaming;
            public TrackingService Tracking;
            public TrackingRecordings Recordings;
            public AvatarService Avatar;

            public ILog Log(string module, string category)
            {
                return Logging.Provider.GetLog(module, category);
            }
        }

        // Logging starts first and stops last so that every other service's lifecycle is recorded. Services added later
        // receive its Provider in their factories and list LoggingService.ServiceName in their dependencies.
        private static void AddLogging(List<ApplicationServiceDescriptor> descriptors, ApplicationCompositionContext context, Built built)
        {
            descriptors.Add(new ApplicationServiceDescriptor(
                LoggingService.ServiceName,
                ServiceCriticality.Required,
                () => built.Logging = CreateLogging(context)));
        }

        // Metrics come right after logging so that they stop before it and their totals reach the log. Services that
        // measure pass this instance to their factories as IPerformanceMetrics.
        private static void AddMetrics(List<ApplicationServiceDescriptor> descriptors, ApplicationCompositionContext context, Built built)
        {
            descriptors.Add(new ApplicationServiceDescriptor(
                PerformanceMetricsService.ServiceName,
                ServiceCriticality.Optional,
                () => built.Metrics = new PerformanceMetricsService(
                    CreatePerformanceSettings(context.Environment),
                    context.MonotonicClock,
                    context.SystemClock,
                    built.Log("Diagnostics", "Performance")),
                new[] { LoggingService.ServiceName }));
        }

        // Capture is Optional: without a capture board or a monitor everything else keeps working (capture detailed
        // design 7.1). Its sources are updated every frame, so it needs the frame loop, which tests without frames
        // leave out.
        private static void AddCapture(List<ApplicationServiceDescriptor> descriptors, ApplicationCompositionContext context, Built built)
        {
            if (context.FrameLoop == null)
            {
                return;
            }

            descriptors.Add(new ApplicationServiceDescriptor(
                CaptureService.ServiceName,
                ServiceCriticality.Optional,
                // The settings are the project's capture profile (project data detailed design 6); without a settings
                // folder nothing is saved.
                () => built.Capture = built.Project != null
                    ? CaptureService.Create(
                        CaptureService.CreateStore(built.Project.Value.ProfileDirectory(ProfileKind.Capture), context.SystemClock, built.Project.Value.ProfileFileName(ProfileKind.Capture)),
                        context.FrameLoop,
                        context.MonotonicClock,
                        built.Metrics,
                        built.Log("Capture", "Sources"))
                    : CaptureService.Create(
                        new CaptureSettings(),
                        context.FrameLoop,
                        context.MonotonicClock,
                        built.Metrics,
                        built.Log("Capture", "Sources")),
                new[] { LoggingService.ServiceName }));
        }

        // Audio captures the microphone and the game's audio and mixes them for recording (audio detailed design 7),
        // before Video so its stream bus is there for recording. Optional: without the plugin or a device everything
        // else runs, and recordings are video only.
        private static void AddAudio(List<ApplicationServiceDescriptor> descriptors, ApplicationCompositionContext context, Built built)
        {
            if (context.FrameLoop == null || built.Project == null)
            {
                return;
            }

            descriptors.Add(new ApplicationServiceDescriptor(
                AudioService.ServiceName,
                ServiceCriticality.Optional,
                () => built.Audio = CreateAudio(context, built.Project.Value, built.Metrics, built.Log("Audio", "Sources")),
                new[] { LoggingService.ServiceName }));
        }

        // The voice conversion, a stage on the microphone. Its models stay outside the repository, in the Data Root;
        // without them the Card says so.
        private static void AddVoice(List<ApplicationServiceDescriptor> descriptors, ApplicationCompositionContext context, Built built)
        {
            if (context.FrameLoop == null || built.Project == null)
            {
                return;
            }

            descriptors.Add(new ApplicationServiceDescriptor(
                VoiceService.ServiceName,
                ServiceCriticality.Optional,
                () =>
                {
                    ILog voiceLog = built.Log("Voice", "Conversion");
                    OpenedProject project = built.Project.Value;

                    // The voice for the stream is the project's voice profile; the user's measured pitch is this PC's.
                    VoiceStorage storage = VoiceStorage.Open(project.ProfileDirectory(ProfileKind.Voice), project.ProfileFileName(ProfileKind.Voice), context.ConfigDirectory, context.SystemClock, context.MonotonicClock, context.FrameLoop, voiceLog);
                    string voiceModels = context.DataRoot.GetDirectory(DataRootDirectory.VoiceModels);
                    return built.Voice = new VoiceService(built.Audio.MicrophoneStage, context.FrameLoop, VoiceModelLibrary.Create(voiceModels, new VoiceModelStore(voiceModels, context.SystemClock)), voiceLog, built.Metrics, storage);
                },
                new[] { LoggingService.ServiceName, AudioService.ServiceName }));
        }

        // Video renders the stream with the persistent scene's Main Camera (video detailed design 7.1), and recording,
        // streaming, and the stage build on it. Optional: without it there is no stream video, and everything else runs.
        private static void AddVideo(List<ApplicationServiceDescriptor> descriptors, ApplicationCompositionContext context, Built built)
        {
            if (context.MainCamera == null || context.FrameLoop == null)
            {
                return;
            }

            // The project's streaming profile gives the stream video its size and the recordings their encoding
            // (recording detailed design 10), so it is read before Video starts.
            var streaming = new Lazy<(RecordingSettings Value, ConfigSaveScheduler<RecordingSettings> Saves)>(
                () => LoadStreaming(context, built.Project?.Value, built.Log("Video", "Recording")));
            descriptors.Add(new ApplicationServiceDescriptor(
                VideoService.ServiceName,
                ServiceCriticality.Optional,
                () => built.Video = new VideoService(
                    VideoSettingsOf(streaming.Value.Value),
                    context.MainCamera,
                    context.FrameLoop,
                    context.MonotonicClock,
                    built.Metrics,
                    built.Log("Video", "Stream")),
                new[] { LoggingService.ServiceName }));

            // Recording reads the stream video back and encodes it (recording detailed design 7). Optional: without the
            // encoder plugin or its service everything else runs. After Video, so a recording in progress is finished
            // before the stream video stops.
            descriptors.Add(new ApplicationServiceDescriptor(
                RecordingService.ServiceName,
                ServiceCriticality.Optional,
                () => built.Recording = new RecordingService(
                    streaming.Value.Value,
                    streaming.Value.Saves,
                    built.Video.Size,
                    context.FrameLoop,
                    context.MonotonicClock,
                    RecordingEnvironment.Native(built.Video.Video, context.MonotonicClock, () => RecordingAudioOf(built.Audio)),
                    built.Metrics,
                    built.Log("Video", "Recording")),
                new[] { LoggingService.ServiceName, VideoService.ServiceName }));

            // Streaming reads the stream video back as NV12 and sends it (streaming detailed design 7). Optional, like
            // recording; a stream in progress disconnects before the stream video stops. Its settings share the
            // profile's file, so Recording writes them.
            descriptors.Add(new ApplicationServiceDescriptor(
                StreamingService.ServiceName,
                ServiceCriticality.Optional,
                () => built.Streaming = new StreamingService(
                    streaming.Value.Value.Stream,
                    settings => built.Recording?.SaveStream(settings),
                    built.Video.Size,
                    streaming.Value.Value.FrameRate,
                    streaming.Value.Value.AudioBitrate,
                    context.FrameLoop,
                    context.MonotonicClock,
                    new WindowsCredentialStore(),
                    StreamingEnvironment.Native(built.Video.Video, context.MonotonicClock, () => StreamingAudioOf(built.Audio)),
                    built.Metrics,
                    built.Log("Streaming", "Service")),
                new[] { LoggingService.ServiceName, VideoService.ServiceName, RecordingService.ServiceName }));

            // Stage puts the capture videos on the stream screen (Stage detailed design 7), so it needs both; when
            // either does not start, the host does not create it.
            if (context.ConfigDirectory != null)
            {
                descriptors.Add(new ApplicationServiceDescriptor(
                    StageService.ServiceName,
                    ServiceCriticality.Optional,
                    () => built.Stage = new StageService(
                        built.Capture,
                        built.Video,
                        context.FrameLoop,
                        StageService.CreateStore(built.Project.Value.Directory, context.SystemClock),
                        context.MonotonicClock,
                        built.Log("Stage", "Screens")),
                    new[] { LoggingService.ServiceName, CaptureService.ServiceName, VideoService.ServiceName }));
            }
        }

        // Tracking runs on the tracking camera, one of the capture sources (tracking detailed design 5.3). Optional:
        // without it the avatar keeps its rest pose and everything else runs. The developers' recordings of tracking
        // (tracking detailed design 12) come after it, in the Data Root's Cache, so they stop first and close a
        // recording in progress.
        private static void AddTracking(List<ApplicationServiceDescriptor> descriptors, ApplicationCompositionContext context, Built built)
        {
            if (context.FrameLoop == null || context.ConfigDirectory == null)
            {
                return;
            }

            descriptors.Add(new ApplicationServiceDescriptor(
                TrackingService.ServiceName,
                ServiceCriticality.Optional,
                () => built.Tracking = CreateTracking(context, built.Project.Value, built.Capture, built.Metrics, built.Log("Tracking", "Source")),
                new[] { LoggingService.ServiceName, CaptureService.ServiceName }));
            descriptors.Add(new ApplicationServiceDescriptor(
                TrackingRecordings.ServiceName,
                ServiceCriticality.Optional,
                () => built.Recordings = new TrackingRecordings(
                    built.Tracking,
                    Path.Combine(context.DataRoot.GetDirectory(DataRootDirectory.Cache), TrackingRecordings.FolderName),
                    () => context.SystemClock.UtcNow.UtcDateTime,
                    () => context.MonotonicClock.GetTimestamp() / (double)context.MonotonicClock.Frequency,
                    built.Log("Tracking", "Recordings")),
                new[] { LoggingService.ServiceName, TrackingService.ServiceName }));
        }

        // Avatar puts the chosen avatar on the stream (avatar detailed design 8.1): on Video's avatar layer at Stage's
        // spawn point, so it needs both. Optional: without it the stream has no avatar.
        private static void AddAvatar(List<ApplicationServiceDescriptor> descriptors, ApplicationCompositionContext context, Built built)
        {
            if (context.MainCamera == null || context.FrameLoop == null || context.ConfigDirectory == null)
            {
                return;
            }

            descriptors.Add(new ApplicationServiceDescriptor(
                AvatarService.ServiceName,
                ServiceCriticality.Optional,
                // Tracking, when it started, was created just before: its frame work runs first each frame.
                () => built.Avatar = CreateAvatar(context, built.Project.Value, built.Video, built.Stage, built.Tracking, built.Metrics, built.Log("Avatar", "Avatars")),
                new[] { LoggingService.ServiceName, VideoService.ServiceName, StageService.ServiceName }));
        }

        // The UI comes last so that it is built after every service it shows has started, and it stops first. It is
        // Optional: without a UI the bootstrap shows the startup problem screen.
        private static void AddUi(List<ApplicationServiceDescriptor> descriptors, ApplicationCompositionContext context, Built built)
        {
            if (context.UiRoot == null || context.ConfigDirectory == null)
            {
                return;
            }

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
                        built.Log("UI", "Root"),
                        SystemMotionPreference.PrefersReducedMotion(),
                        context.BuildInfo.ApplicationVersion,
                        built.Metrics,

                        // Detailed metrics follow Developer Mode once the UI is up (performance metrics detailed design 7.1).
                        DeveloperModeReceivers(built.Metrics, built.Recordings),
                        CreateScreenServices(context, built))),
                new[] { LoggingService.ServiceName }));
        }

        // The live and setup screens (docs/<lang>/ui/screens/). Services that are not there leave their part of the
        // screens saying so.
        private static ScreenServices CreateScreenServices(ApplicationCompositionContext context, Built built)
        {
            VoiceLabService voiceLab = Configure(VoiceLabService.Create(context.ConfigDirectory, context.DataRoot.GetDirectory(DataRootDirectory.VoiceLab), context.DataRoot.GetDirectory(DataRootDirectory.VoiceModels), context.SystemClock, built.Log("VoiceLab", "Design")), built.Voice);
            return new ScreenServices
            {
                StreamPreview = built.Video?.Preview,
                FrameSources = built.Stage?.Screens,
                Captures = built.Capture,
                Audio = built.Audio,
                Voice = built.Voice,
                VoiceTrial = built.Voice == null ? null : new VoiceTrialService(context.FrameLoop, built.Voice.Library, built.Log("Voice", "Trial")),
                VoiceLab = voiceLab,
                VoiceLabLibrary = voiceLab.Library,
                VoiceLabSetup = PauseWhileLive(CreateVoiceLabSetup(context, voiceLab, built.Log("VoiceLab", "Activation")), built.Streaming, built.Recording),
                AudioPreview = built.Audio,
                SystemSettings = new WindowsSystemSettings(),
                Recording = built.Recording,
                Streaming = built.Streaming,
                Explorer = new WindowsFileExplorer(),
                Tracking = built.Tracking,
                TrackingRecordings = built.Recordings,
                Avatars = built.Avatar,
                FileDialog = new WindowsFileDialog(),
                Credits = CreateCreditFiles(context.Environment, UnityEngine.Application.dataPath, built.Log("UI", "Credits")),
                Lighting = built.Stage,
            };
        }

        // Voice Lab's preparation (Voice Lab detailed design 8.1): its scripts, the shared models' folder in the Data
        // Root, and the logs of the check of the PC.
        private static VoiceLabSetupService CreateVoiceLabSetup(ApplicationCompositionContext context, VoiceLabService voiceLab, ILog log)
        {
            return VoiceLabSetupService.Create(
                context.ConfigDirectory,
                GetVoiceLabScripts(context.Environment, UnityEngine.Application.dataPath),
                Path.Combine(context.DataRoot.GetDirectory(DataRootDirectory.VoiceModels), "Shared", "1"),
                Path.Combine(GetLogDirectory(context.DataRoot, context.Environment), "voice-lab"),
                voiceLab,
                context.SystemClock,
                log);
        }
    }
}
