using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Capture.Game;
using VirtualVessel.Capture.SubScreen;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.Capture
{
    /// <summary>
    /// Owns the capture sources: creates them, updates them every frame, and releases them on
    /// shutdown (capture detailed design 7.1). An Optional application service: its failure stops no
    /// other feature.
    /// </summary>
    public sealed class CaptureService : IApplicationService, ICaptureSources
    {
        public const string ServiceName = "Capture";

        /// <summary>The id of the tracking camera among <see cref="Sources"/>, for Tracking and its setup.</summary>
        public const string TrackingCameraId = Game.WebCamCaptureSource.CameraSourceId;

        private readonly List<CaptureSourceBase> _sources;
        private readonly IReadOnlyList<IVideoCaptureSource> _publicSources;
        private readonly IFrameLoop _frameLoop;
        private readonly IMonotonicClock _clock;
        private readonly ILog _log;
        private readonly ConfigSaveScheduler<CaptureSettings> _saves;
        private CaptureSettings _settings;
        private CaptureChoices _saved;
        private IDisposable _tick;

        /// <param name="settings">What the sources were created with; started again when they were started last time.</param>
        /// <param name="saves">Where changes go; null to save nothing, as in tests.</param>
        internal CaptureService(IEnumerable<CaptureSourceBase> sources, IFrameLoop frameLoop, IMonotonicClock clock, ILog log, CaptureSettings settings = null, ConfigSaveScheduler<CaptureSettings> saves = null)
        {
            _settings = settings ?? new CaptureSettings();
            _saved = CaptureChoices.From(_settings);
            _saves = saves;
            _sources = new List<CaptureSourceBase>(sources ?? throw new ArgumentNullException(nameof(sources)));
            _publicSources = _sources.ConvertAll(source => (IVideoCaptureSource)source);
            _frameLoop = frameLoop ?? throw new ArgumentNullException(nameof(frameLoop));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log;
        }

        public string Name => ServiceName;

        public IReadOnlyList<IVideoCaptureSource> Sources => _publicSources;

        /// <summary>Creates the service with the product's capture sources and the given settings, saving nothing.</summary>
        public static CaptureService Create(CaptureSettings settings, IFrameLoop frameLoop, IMonotonicClock clock, IPerformanceMetrics metrics, ILog log)
        {
            settings ??= new CaptureSettings();
            return new CaptureService(CreateSources(settings, clock, metrics, log), frameLoop, clock, log, settings);
        }

        /// <summary>Creates the service with the settings in <paramref name="store"/>, saving changes back to it.</summary>
        public static CaptureService Create(ConfigFileStore<CaptureSettings> store, IFrameLoop frameLoop, IMonotonicClock clock, IPerformanceMetrics metrics, ILog log)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            ConfigLoadResult<CaptureSettings> loaded = store.Load();
            CaptureSettings settings = loaded.Value ?? new CaptureSettings();
            log?.Information($"Capture settings {loaded.Status} from {store.FilePath}.");
            var saves = new ConfigSaveScheduler<CaptureSettings>(store, clock, TimeSpan.FromSeconds(0.5), failure => log?.Warning($"Failed to save the capture settings to {store.FilePath}.", failure));
            return new CaptureService(CreateSources(settings, clock, metrics, log), frameLoop, clock, log, settings, saves);
        }

        /// <summary>Creates the store of capture-settings.json in the settings folder.</summary>
        public static ConfigFileStore<CaptureSettings> CreateStore(string configDirectory, ISystemClock clock)
        {
            return new ConfigFileStore<CaptureSettings>(configDirectory, CaptureSettings.FileName, CaptureSettings.SchemaVersion, () => new CaptureSettings(), clock);
        }

        public IVideoCaptureSource Find(string id)
        {
            foreach (CaptureSourceBase source in _sources)
            {
                if (string.Equals(source.Id, id, StringComparison.Ordinal))
                {
                    return source;
                }
            }

            return null;
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            _tick = _frameLoop.Register(Tick);
            _log?.Information($"Capture started with {_sources.Count} sources.");

            // Sources the user had started come back (capture detailed design 10).
            foreach (CaptureSourceBase source in _sources)
            {
                if (WasStarted(_settings, source.Id))
                {
                    source.Start();
                }
            }

            _saved = CaptureChoices.Of(_saved, _sources);
            return Task.CompletedTask;
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            // Saved before stopping, so the sources that were running start again next time.
            NoteChanges();
            foreach (CaptureSourceBase source in _sources)
            {
                source.Stop();
            }

            if (_saves != null)
            {
                await _saves.FlushAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Whether a source was started when the settings were saved. Pure.</summary>
        internal static bool WasStarted(CaptureSettings settings, string sourceId)
        {
            return sourceId switch
            {
                Game.WebCamCaptureSource.GameSourceId => settings.GameEnabled,
                Game.WebCamCaptureSource.CameraSourceId => settings.CameraEnabled,
                SubScreen.SubScreenCaptureSource.SourceId => settings.SubScreenEnabled,
                _ => false,
            };
        }

        public void Dispose()
        {
            _tick?.Dispose();
            _tick = null;
            foreach (CaptureSourceBase source in _sources)
            {
                source.Dispose();
            }
        }

        internal void Tick()
        {
            long now = _clock.GetTimestamp();
            foreach (CaptureSourceBase source in _sources)
            {
                source.Tick(now);
            }

            NoteChanges();
            _saves?.Tick();
        }

        private void NoteChanges()
        {
            CaptureChoices now = CaptureChoices.Of(_saved, _sources);
            if (now.Equals(_saved))
            {
                return;
            }

            _saved = now;
            _settings = now.ApplyTo(_settings);
            _saves?.Changed(_settings.Clone());
        }

        private static CaptureSourceBase[] CreateSources(CaptureSettings settings, IMonotonicClock clock, IPerformanceMetrics metrics, ILog log)
        {
            // None touches a device or the native plugin until it is started. The two camera sources
            // keep off each other's device, which WebCamTexture cannot open twice.
            var cameras = new UnityCameraDevices();
            WebCamCaptureSource game = null;
            WebCamCaptureSource camera = null;
            game = WebCamCaptureSource.ForGame(cameras, settings, clock, metrics, log, id => IsOpen(camera, id));
            camera = WebCamCaptureSource.ForTrackingCamera(cameras, settings, clock, metrics, log, id => IsOpen(game, id));
            return new CaptureSourceBase[]
            {
                game,
                new SubScreenCaptureSource(new DesktopDuplicationNative(), settings, clock, metrics, log),
                camera,
            };
        }

        /// <summary>Whether <paramref name="source"/> has the device <paramref name="deviceId"/> open.</summary>
        internal static bool IsOpen(CaptureSourceBase source, string deviceId)
        {
            return source != null && string.Equals(source.OpenDeviceId, deviceId, StringComparison.Ordinal);
        }
    }
}
