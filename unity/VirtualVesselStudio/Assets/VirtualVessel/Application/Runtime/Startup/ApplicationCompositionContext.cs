using System;
using VirtualVessel.Application.Build;
using VirtualVessel.Application.Logging;
using VirtualVessel.Application.Session;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Threading;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.DataRoot;
using VirtualVessel.UI;

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
}
