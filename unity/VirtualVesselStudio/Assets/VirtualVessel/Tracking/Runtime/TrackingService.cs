using System;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Capture;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.Tracking.Processing;

namespace VirtualVessel.Tracking
{
    /// <summary>
    /// The tracking of one person (tracking detailed design 5.1, 5.3): runs the provider on the
    /// tracking camera, smooths its observations every frame, and saves the user's choices. An
    /// Optional application service; without it the avatar keeps its rest pose.
    /// </summary>
    /// <remarks>
    /// The tracking camera is used only for tracking, so it starts and stops with tracking. Main
    /// thread only.
    /// </remarks>
    public sealed class TrackingService : IApplicationService, ITrackingSource
    {
        public const string ServiceName = "Tracking";

        private readonly ITrackingProvider _provider;
        private readonly IVideoCaptureSource _camera;
        private readonly IFrameLoop _frameLoop;
        private readonly IMonotonicClock _clock;
        private readonly ILog _log;
        private readonly ConfigSaveScheduler<TrackingSettings> _saves;
        private readonly TrackingProcessor _processor = new TrackingProcessor();
        private readonly TrackingFrame _raw = new TrackingFrame();
        private readonly TrackingFrame _current = new TrackingFrame();
        private readonly ObservableValue<TrackingState> _state = new ObservableValue<TrackingState>(TrackingState.Stopped);
        private readonly ObservableValue<TrackingProblem> _problem = new ObservableValue<TrackingProblem>(TrackingProblem.None);
        private readonly ObservableValue<TrackingRegions> _enabled;
        private readonly ObservableValue<BodyRange> _bodyRange;
        private readonly ObservableValue<SmoothingPreset> _smoothing;
        private readonly PerfTimer _latency;
        private TrackingSettings _settings;
        private TrackingChoices _saved;
        private bool _wanted;
        private IDisposable _tick;

        /// <param name="provider">Null when no provider can run, such as without the MediaPipe plugin; tracking then reports StartFailed.</param>
        /// <param name="camera">The tracking camera, or null for a provider that needs none, such as replay.</param>
        /// <param name="saves">Where changes go; null to save nothing, as in tests.</param>
        public TrackingService(ITrackingProvider provider, IVideoCaptureSource camera, IFrameLoop frameLoop, IMonotonicClock clock, TrackingSettings settings, ConfigSaveScheduler<TrackingSettings> saves, IPerformanceMetrics metrics, ILog log)
        {
            _provider = provider;
            _camera = camera;
            _frameLoop = frameLoop ?? throw new ArgumentNullException(nameof(frameLoop));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _settings = settings?.Clone() ?? new TrackingSettings();
            _saves = saves;
            _log = log;
            _saved = TrackingChoices.From(_settings);
            _enabled = new ObservableValue<TrackingRegions>(_settings.Regions);
            _bodyRange = new ObservableValue<BodyRange>(Available(_settings.BodyRange));
            _smoothing = new ObservableValue<SmoothingPreset>(_settings.Smoothing);
            _latency = metrics?.Timer("Tracking", "Latency");
        }

        public string Name => ServiceName;

        public IReadOnlyObservable<TrackingState> State => _state;

        public IReadOnlyObservable<TrackingProblem> Problem => _problem;

        public TrackingRegions Supported => _provider?.Supported ?? TrackingRegions.None;

        public IReadOnlyObservable<TrackingRegions> Enabled => _enabled;

        public IReadOnlyObservable<BodyRange> BodyRange => _bodyRange;

        public IReadOnlyObservable<SmoothingPreset> Smoothing => _smoothing;

        public TrackingFrame Current => _current;

        /// <summary>The settings as last saved, for the provider factory.</summary>
        public TrackingSettings Settings => _settings.Clone();

        /// <summary>Creates the store of tracking-settings.json in the settings folder.</summary>
        public static ConfigFileStore<TrackingSettings> CreateStore(string configDirectory, ISystemClock clock)
        {
            return new ConfigFileStore<TrackingSettings>(configDirectory, TrackingSettings.FileName, TrackingSettings.SchemaVersion, () => new TrackingSettings(), clock);
        }

        /// <summary>
        /// Full body is shown as coming soon and cannot be chosen yet (tracking detailed design 6.5);
        /// a saved value from a later version falls back to the upper body. Pure.
        /// </summary>
        public static BodyRange Available(BodyRange range)
        {
            return range == Tracking.BodyRange.UpperBody ? range : Tracking.BodyRange.UpperBody;
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            _tick = _frameLoop.Register(Tick);
            _log?.Information($"Tracking started with provider {_settings.Provider}{(_provider == null ? " (not available)" : string.Empty)}.");
            if (_settings.Running)
            {
                Start();
            }

            return Task.CompletedTask;
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            // Saved before stopping, so tracking starts again next time if it was running.
            NoteChanges();
            StopProvider();
            if (_saves != null)
            {
                await _saves.FlushAsync().ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            _tick?.Dispose();
            _tick = null;
            StopProvider();
            _provider?.Dispose();
        }

        public void Start()
        {
            if (_wanted)
            {
                return;
            }

            _wanted = true;
            _processor.Reset();
            _camera?.Start();
            _provider?.Start(_enabled.Value);
            _log?.Information($"Tracking on: {_enabled.Value}, {_bodyRange.Value}, {_smoothing.Value}.");
            Refresh();
        }

        public void Stop()
        {
            if (!_wanted)
            {
                return;
            }

            _wanted = false;
            StopProvider();
            _log?.Information("Tracking off.");
            Refresh();
        }

        public void SetEnabled(TrackingRegions regions)
        {
            regions &= TrackingRegions.All;
            if (_enabled.Set(regions) && _wanted)
            {
                _provider?.SetRegions(regions);
            }
        }

        public void SetBodyRange(BodyRange range)
        {
            _bodyRange.Set(Available(range));
        }

        public void SetSmoothing(SmoothingPreset preset)
        {
            _smoothing.Set(preset);
        }

        /// <summary>
        /// The state and problem to show, from the camera, the provider, and whether anyone is seen
        /// (tracking detailed design 7.1, 11). Pure.
        /// </summary>
        internal static (TrackingState State, TrackingProblem Problem) Describe(
            bool wanted,
            bool hasProvider,
            bool needsCamera,
            CaptureState cameraState,
            CaptureProblem cameraProblem,
            TrackingState providerState,
            bool personSeen)
        {
            if (!wanted)
            {
                return (TrackingState.Stopped, TrackingProblem.None);
            }

            if (!hasProvider)
            {
                return (TrackingState.Error, TrackingProblem.StartFailed);
            }

            if (needsCamera)
            {
                switch (cameraState)
                {
                    case CaptureState.Stopped when cameraProblem == CaptureProblem.DeviceNotChosen:
                        return (TrackingState.Unavailable, TrackingProblem.CameraNotChosen);
                    case CaptureState.Unavailable:
                        return (TrackingState.Unavailable, TrackingProblem.CameraNotFound);
                    case CaptureState.Error:
                        return (TrackingState.Error, cameraProblem == CaptureProblem.DeviceInUse ? TrackingProblem.CameraInUse : TrackingProblem.StartFailed);
                    case CaptureState.Stopped:
                    case CaptureState.Starting:
                    case CaptureState.Ready:
                        return (TrackingState.Starting, TrackingProblem.None);
                }
            }

            switch (providerState)
            {
                case TrackingState.Error:
                    return (TrackingState.Error, TrackingProblem.StartFailed);
                case TrackingState.Running:
                    return (TrackingState.Running, personSeen ? TrackingProblem.None : TrackingProblem.NoPerson);
                default:
                    return (TrackingState.Starting, TrackingProblem.None);
            }
        }

        internal void Tick()
        {
            double now = Seconds(_clock.GetTimestamp());
            if (_wanted && _provider != null)
            {
                _provider.Tick();
                bool observed = _provider.TryTakeLatest(_raw);
                _processor.Process(observed ? _raw : null, now, _provider.Supported, _enabled.Value, _smoothing.Value, _current);
                if (observed)
                {
                    _latency?.Record(TimeSpan.FromSeconds(Math.Max(0, now - Newest(_raw))));
                }
            }

            Refresh();
            NoteChanges();
            _saves?.Tick();
        }

        /// <summary>The newest part time of an observation: how old the image behind it is.</summary>
        internal static double Newest(TrackingFrame frame)
        {
            double newest = Math.Max(Math.Max(TimeOf(frame.Body), TimeOf(frame.Face)), Math.Max(TimeOf(frame.LeftHand), TimeOf(frame.RightHand)));
            return double.IsNegativeInfinity(newest) ? frame.Time : newest;
        }

        private static double TimeOf(TrackedPart part)
        {
            return part.IsUsable ? part.Timestamp : double.NegativeInfinity;
        }

        private void StopProvider()
        {
            _provider?.Stop();
            _camera?.Stop();
            _processor.Reset();
        }

        private void Refresh()
        {
            bool personSeen = _current.Body.IsUsable || _current.Face.IsUsable || _current.LeftHand.IsUsable || _current.RightHand.IsUsable;
            (TrackingState state, TrackingProblem problem) = Describe(
                _wanted,
                _provider != null,
                _camera != null,
                _camera?.State.Value ?? CaptureState.Stopped,
                _camera?.Problem.Value ?? CaptureProblem.None,
                _provider?.State.Value ?? TrackingState.Stopped,
                personSeen);
            if (_state.Set(state))
            {
                _log?.Information($"Tracking is {state}{(problem == TrackingProblem.None ? string.Empty : $" ({problem})")}.");
            }

            _problem.Set(problem);
        }

        private void NoteChanges()
        {
            var now = new TrackingChoices(_enabled.Value, _bodyRange.Value, _smoothing.Value, _wanted);
            if (now.Equals(_saved))
            {
                return;
            }

            _saved = now;
            _settings = now.ApplyTo(_settings);
            _saves?.Changed(_settings.Clone());
        }

        private double Seconds(long ticks)
        {
            return ticks / (double)_clock.Frequency;
        }
    }
}
