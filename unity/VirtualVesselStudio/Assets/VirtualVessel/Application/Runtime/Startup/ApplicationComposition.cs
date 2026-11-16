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
    /// <summary>
    /// The single place that decides which services exist, in what order, and what each depends on.
    /// </summary>
    /// <remarks>
    /// Services are listed explicitly instead of discovered by reflection so that the startup order
    /// and every dependency can be reviewed in one file. Factories run in this order, so a later
    /// factory can use an instance created by an earlier one, such as the log provider.
    /// </remarks>
    internal static partial class ApplicationComposition
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
            var built = new Built();
            descriptors.AddRange(LoggingDescriptors(context, built));
            descriptors.AddRange(MetricsDescriptors(context, built));

            // The project in use, opened when the first service that keeps settings is created (project
            // data detailed design 7). Only the settings folder's own files stay in Config.
            built.Project = context.ConfigDirectory != null
                ? new Lazy<OpenedProject>(() => OpenProject(context, built.Logging.Provider.GetLog("ProjectData", "Projects")))
                : null;

            descriptors.AddRange(CaptureDescriptors(context, built));
            descriptors.AddRange(AudioDescriptors(context, built));
            descriptors.AddRange(AudioPlayerDescriptors(context, built));
            descriptors.AddRange(VoiceDescriptors(context, built));
            descriptors.AddRange(VideoDescriptors(context, built));
            descriptors.AddRange(TrackingDescriptors(context, built));
            descriptors.AddRange(AvatarDescriptors(context, built));
            descriptors.AddRange(UiDescriptors(context, built));
            return descriptors;
        }

        /// <summary>
        /// What follows Developer Mode: detailed metrics, and recording and replay, which stop when it is
        /// turned off (tracking detailed design 12.2, 12.4). Pure.
        /// </summary>
        public static IReadOnlyList<Action<bool>> DeveloperModeReceivers(PerformanceMetricsService metrics, ITrackingRecordings recordings)
        {
            var receivers = new List<Action<bool>>();
            if (metrics != null)
            {
                receivers.Add(metrics.SetDetailedEnabled);
            }

            if (recordings != null)
            {
                receivers.Add(on =>
                {
                    if (!on)
                    {
                        recordings.StopAll();
                    }
                });
            }

            return receivers;
        }

        public static string GetLogDirectory(IDataRoot dataRoot, RuntimeEnvironment environment)
        {
            // Editor Play Mode runs are frequent during development; keeping them apart stops them
            // from pushing logs of real use out of retention (logging detailed design 7.3).
            string folder = environment == RuntimeEnvironment.Editor ? EditorLogFolder : ApplicationLogFolder;
            return Path.Combine(dataRoot.GetDirectory(DataRootDirectory.Logs), folder);
        }

        /// <summary>
        /// The license files for the credits: next to the executable in a built application, at their
        /// sources in the repository in the Editor (development workflow 18.1.2).
        /// </summary>
        /// <param name="dataPath">Unity's <c>Application.dataPath</c>: the project's Assets folder in the Editor, &lt;name&gt;_Data in a build.</param>
        public static CreditFiles CreateCreditFiles(RuntimeEnvironment environment, string dataPath, ILog log)
        {
            return environment == RuntimeEnvironment.Editor
                ? CreditFiles.ForRepository(GetCreditFolder(environment, dataPath), log)
                : CreditFiles.ForPackage(GetCreditFolder(environment, dataPath), log);
        }

        /// <summary>The repository root (Assets → unity/VirtualVesselStudio → unity → root) or the application folder. Pure.</summary>
        public static string GetCreditFolder(RuntimeEnvironment environment, string dataPath)
        {
            string full = Path.GetFullPath(dataPath);
            return environment == RuntimeEnvironment.Editor
                ? Path.GetFullPath(Path.Combine(full, "..", "..", ".."))
                : Path.GetDirectoryName(full);
        }

        /// <summary>
        /// Pauses Voice Lab's preparation while streaming or recording, whichever screen is open (Voice Lab detailed design
        /// 8.1.1): the services' states are watched here, for the app's lifetime, not by the Voice Lab screen.
        /// </summary>
        private static VoiceLabSetupService PauseWhileLive(VoiceLabSetupService setup, StreamingService streaming, RecordingService recording)
        {
            if (setup == null)
            {
                return null;
            }

            void Update() => setup.SetLive(VoiceLabView.IsLive(streaming?.State.Value ?? StreamingState.Idle, recording?.State.Value ?? RecordingState.Idle));
            streaming?.State.Subscribe(_ => Update());
            recording?.State.Subscribe(_ => Update());
            Update();
            return setup;
        }

        /// <summary>
        /// Voice Lab's scripts, download list, and terms: the repository's services/voice-lab in the Editor, VoiceLab/scripts
        /// next to the executable in a build (tools/release/build-release.ps1). Pure.
        /// </summary>
        public static string GetVoiceLabScripts(RuntimeEnvironment environment, string dataPath)
        {
            string folder = GetCreditFolder(environment, dataPath);
            return environment == RuntimeEnvironment.Editor
                ? Path.Combine(folder, "services", "voice-lab")
                : Path.Combine(folder, "VoiceLab", "scripts");
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

        // STEP 3's Test plan runs only in Unity's Editor, for checking the screens quickly; built players never allow it.
        // The voice used for the stream is Voice's to say, so Voice Lab does not delete it.
        private static VoiceLabService Configure(VoiceLabService voiceLab, VoiceService voice)
        {
#if UNITY_EDITOR
            voiceLab.AllowEditorTest = true;
#endif
            voiceLab.VoiceInUse = voiceId => voice != null && voice.InUseVoiceId == voiceId;
            return voiceLab;
        }
    }
}
