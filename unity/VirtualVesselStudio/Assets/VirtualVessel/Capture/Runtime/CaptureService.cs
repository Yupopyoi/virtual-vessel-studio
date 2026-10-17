using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;

namespace VirtualVessel.Capture
{
    /// <summary>What the capture sources start with (capture detailed design 10). Where it is saved is still open.</summary>
    public sealed class CaptureSettings
    {
        /// <summary>The capture board's camera name; the first device when not set.</summary>
        public string GameDeviceId { get; set; }

        public int GameWidth { get; set; } = 1920;

        public int GameHeight { get; set; } = 1080;

        public int GameFrameRate { get; set; } = 60;

        /// <summary>The monitor's device name; the first monitor that is not the main one when not set.</summary>
        public string SubScreenMonitorId { get; set; }
    }

    /// <summary>
    /// Owns the capture sources: creates them, updates them every frame, and releases them on
    /// shutdown (capture detailed design 7.1). An Optional application service: its failure stops no
    /// other feature.
    /// </summary>
    public sealed class CaptureService : IApplicationService, ICaptureSources
    {
        public const string ServiceName = "Capture";

        private readonly List<CaptureSourceBase> _sources;
        private readonly IReadOnlyList<IVideoCaptureSource> _publicSources;
        private readonly IFrameLoop _frameLoop;
        private readonly IMonotonicClock _clock;
        private readonly ILog _log;
        private IDisposable _tick;

        internal CaptureService(IEnumerable<CaptureSourceBase> sources, IFrameLoop frameLoop, IMonotonicClock clock, ILog log)
        {
            _sources = new List<CaptureSourceBase>(sources ?? throw new ArgumentNullException(nameof(sources)));
            _publicSources = _sources.ConvertAll(source => (IVideoCaptureSource)source);
            _frameLoop = frameLoop ?? throw new ArgumentNullException(nameof(frameLoop));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log;
        }

        public string Name => ServiceName;

        public IReadOnlyList<IVideoCaptureSource> Sources => _publicSources;

        /// <summary>Creates the service with the product's capture sources.</summary>
        public static CaptureService Create(CaptureSettings settings, IFrameLoop frameLoop, IMonotonicClock clock, IPerformanceMetrics metrics, ILog log)
        {
            return new CaptureService(CreateSources(settings ?? new CaptureSettings(), clock, metrics, log), frameLoop, clock, log);
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
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            foreach (CaptureSourceBase source in _sources)
            {
                source.Stop();
            }

            return Task.CompletedTask;
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
        }

        private static IEnumerable<CaptureSourceBase> CreateSources(CaptureSettings settings, IMonotonicClock clock, IPerformanceMetrics metrics, ILog log)
        {
            // The game and sub-screen sources are added with their implementations (capture detailed design 18).
            yield break;
        }
    }
}
