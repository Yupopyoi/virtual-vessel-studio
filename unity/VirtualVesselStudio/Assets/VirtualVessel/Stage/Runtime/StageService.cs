using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VirtualVessel.Capture;
using VirtualVessel.Core.Geometry;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.UI.Frames;
using VirtualVessel.Video;

namespace VirtualVessel.Stage
{
    /// <summary>
    /// Puts each capture video on the stream screen where the placement editor put it, and saves the
    /// placements (Stage detailed design 7). An Optional application service.
    /// </summary>
    public sealed class StageService : IApplicationService
    {
        public const string ServiceName = "Stage";

        /// <summary>The shader of the quads, in a Resources folder so that builds include it.</summary>
        public const string ScreenShaderPath = "StreamScreen";

        private static readonly TimeSpan s_saveDelay = TimeSpan.FromSeconds(0.5);

        private readonly ICaptureSources _captures;
        private readonly IStreamOutput _output;
        private readonly IFrameLoop _frameLoop;
        private readonly ConfigFileStore<StageSettings> _store;
        private readonly ConfigSaveScheduler<StageSettings> _saves;
        private readonly ILog _log;
        private readonly List<CaptureScreen> _screens = new List<CaptureScreen>();
        private StageSettings _settings = new StageSettings();
        private IDisposable _tick;

        public StageService(ICaptureSources captures, IStreamOutput output, IFrameLoop frameLoop, ConfigFileStore<StageSettings> store, IMonotonicClock clock, ILog log)
        {
            _captures = captures ?? throw new ArgumentNullException(nameof(captures));
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _frameLoop = frameLoop ?? throw new ArgumentNullException(nameof(frameLoop));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _log = log;
            _saves = new ConfigSaveScheduler<StageSettings>(store, clock, s_saveDelay, failure => _log?.Warning($"Failed to save the stage settings to {store.FilePath}.", failure));
        }

        public string Name => ServiceName;

        /// <summary>The placement editor's videos, in source order.</summary>
        public IReadOnlyList<IFrameSource> Screens => _screens;

        /// <summary>Creates the store of stage-settings.json in the settings folder.</summary>
        public static ConfigFileStore<StageSettings> CreateStore(string configDirectory, ISystemClock clock)
        {
            return new ConfigFileStore<StageSettings>(configDirectory, StageSettings.FileName, StageSettings.SchemaVersion, () => new StageSettings(), clock);
        }

        /// <summary>The placement to use for a screen: the saved one when it can be used, else the default. Pure.</summary>
        public static FrameLayout LayoutFor(StageSettings settings, string screenId)
        {
            if (settings?.Screens != null && settings.Screens.TryGetValue(screenId, out ScreenSettings saved) && saved?.ToLayout() is FrameLayout layout)
            {
                return layout;
            }

            return StageDefaults.For(screenId);
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            if (_output.Screens?.Root == null)
            {
                throw new InvalidOperationException("The stream video has no flat video layer.");
            }

            Shader shader = Resources.Load<Shader>(ScreenShaderPath);
            if (shader == null)
            {
                throw new InvalidOperationException($"The shader '{ScreenShaderPath}' is missing from Resources.");
            }

            ConfigLoadResult<StageSettings> loaded = _store.Load();
            _settings = loaded.Value?.Clone() ?? new StageSettings();
            Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            foreach (IVideoCaptureSource source in _captures.Sources)
            {
                _screens.Add(new CaptureScreen(source, LayoutFor(_settings, source.Id), _output.Screens, quad, shader, OnCommitted));
            }

            _tick = _frameLoop.Register(_saves.Tick);
            _log?.Information($"Stage started with {_screens.Count} stream screens; placements {loaded.Status} from {_store.FilePath}.");
            return Task.CompletedTask;
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            await _saves.FlushAsync().ConfigureAwait(false);
        }

        public void Dispose()
        {
            _tick?.Dispose();
            _tick = null;
            foreach (CaptureScreen screen in _screens)
            {
                screen.Dispose();
            }

            _screens.Clear();
        }

        private void OnCommitted(CaptureScreen screen)
        {
            // A copy: the save runs on another thread while editing goes on.
            StageSettings next = _settings.Clone();
            next.Screens[screen.Id] = ScreenSettings.From(screen.Layout.Value);
            _settings = next;
            _saves.Changed(next.Clone());
        }
    }
}
