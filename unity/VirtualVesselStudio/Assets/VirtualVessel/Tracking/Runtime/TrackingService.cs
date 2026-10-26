using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
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
    /// The tracking camera is used only for tracking, so it starts and stops with tracking. For
    /// developers' recordings (tracking detailed design 12), it hands raw observations to
    /// <see cref="Observed"/> and can replay a recording in place of the provider. Main thread only.
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
        private readonly TrackingFrame _corrected = new TrackingFrame();
        private readonly TrackingFrame _current = new TrackingFrame();
        private readonly ObservableValue<TrackingState> _state = new ObservableValue<TrackingState>(TrackingState.Stopped);
        private readonly ObservableValue<TrackingProblem> _problem = new ObservableValue<TrackingProblem>(TrackingProblem.None);
        private readonly ObservableValue<TrackingOptions> _options;
        private readonly ObservableValue<float> _front;
        private readonly PerfTimer _latency;
        private readonly PerfCounter _outliers;
        private long _outliersCounted;
        private TrackingSettings _settings;
        private bool _wanted;
        private EventRate _rate;
        private double _lastImage = double.NegativeInfinity;
        private double _now;
        private IDisposable _tick;
        private ITrackingProvider _replay;
        private bool _wantedBeforeReplay;
        private ObservationCorrections _corrections = ObservationCorrections.None;
        private Quaternion _turnBack = Quaternion.identity;

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
            _options = new ObservableValue<TrackingOptions>(_settings.GetOptions());
            _front = new ObservableValue<float>(FacingCorrection.Next(_settings.FrontDegrees, 0f));
            _turnBack = FacingCorrection.TurnBack(_front.Value);
            _latency = metrics?.Timer("Tracking", "Latency");
            _outliers = metrics?.Counter("Tracking", "Outliers");
            ChooseCorrections();
        }

        public string Name => ServiceName;

        public IReadOnlyObservable<TrackingState> State => _state;

        public IReadOnlyObservable<TrackingProblem> Problem => _problem;

        public TrackingRegions Supported => Active?.Supported ?? TrackingRegions.None;

        public IReadOnlyObservable<TrackingOptions> Options => _options;

        public TrackingFrame Current => _current;

        public IReadOnlyObservable<float> Front => _front;

        public double ObservationsPerSecond => _rate.RateAt(_now);

        /// <summary>
        /// Receives each raw observation from the provider, before smoothing, for recording (tracking
        /// detailed design 12.2). Not called during replay. The frame is reused: copy what is kept.
        /// </summary>
        internal Action<TrackingFrame> Observed { get; set; }

        /// <summary>Raised when tracking is turned off, before it stops, so a recording can end.</summary>
        internal event Action Stopping;

        /// <summary>Raised when a replay ends, whether asked or because tracking was turned off.</summary>
        internal event Action ReplayEnded;

        internal bool IsReplaying => _replay != null;

        /// <summary>What kind of observation the active provider makes; recordings keep it so a replay is corrected alike.</summary>
        internal ObservationTraits Traits => Active?.Traits ?? ObservationTraits.None;

        /// <summary>The provider observations come from now: the replay while one runs.</summary>
        private ITrackingProvider Active => _replay ?? _provider;

        /// <summary>Creates the store of tracking-settings.json in the settings folder.</summary>
        public static ConfigFileStore<TrackingSettings> CreateStore(string configDirectory, ISystemClock clock)
        {
            return new ConfigFileStore<TrackingSettings>(configDirectory, TrackingSettings.FileName, TrackingSettings.SchemaVersion, () => new TrackingSettings(), clock, TrackingSettings.Migrations);
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
            _replay?.Dispose();
            _replay = null;
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
            _provider?.Start(_options.Value);
            _log?.Information($"Tracking on: {_options.Value}.");
            Refresh();
        }

        public void Stop()
        {
            if (!_wanted)
            {
                return;
            }

            Stopping?.Invoke();
            bool replaying = _replay != null;
            StopReplayProvider();
            _wanted = false;
            StopProvider();
            _log?.Information("Tracking off.");
            Refresh();
            if (replaying)
            {
                ReplayEnded?.Invoke();
            }
        }

        /// <summary>
        /// Replays <paramref name="replay"/> in place of the camera and the provider, starting tracking
        /// for it if it was off; a replay already running is replaced. Starting tracking for a replay is
        /// not saved (tracking detailed design 12.4).
        /// </summary>
        internal void BeginReplay(ITrackingProvider replay)
        {
            if (replay == null)
            {
                throw new ArgumentNullException(nameof(replay));
            }

            if (_replay == null)
            {
                _wantedBeforeReplay = _wanted;
                if (_wanted)
                {
                    StopProvider();
                }
            }
            else
            {
                StopReplayProvider();
            }

            _replay = replay;
            ChooseCorrections();
            _wanted = true;
            _processor.Reset();
            _replay.Start(_options.Value);
            _log?.Information("Tracking replays a recording.");
            Refresh();
        }

        /// <summary>Ends the replay and goes back to the state before it: the camera and the provider if tracking was on.</summary>
        internal void EndReplay()
        {
            if (_replay == null)
            {
                return;
            }

            StopReplayProvider();
            _wanted = false;
            _processor.Reset();
            _log?.Information("Tracking stopped replaying.");
            if (_wantedBeforeReplay)
            {
                Start();
            }
            else
            {
                Refresh();
            }

            ReplayEnded?.Invoke();
        }

        public void SetOptions(TrackingOptions options)
        {
            PoseModel before = _options.Value.PoseModel;
            if (!_options.Set(options) || !_wanted)
            {
                return;
            }

            if (options.PoseModel != before && _replay == null && _provider != null)
            {
                // The model is loaded when the provider starts; the camera keeps running.
                _provider.Stop();
                _processor.Reset();
                _provider.Start(options);
                _log?.Information($"Tracking restarts with the {options.PoseModel} Pose model.");
                return;
            }

            Active?.SetOptions(options);
        }

        public bool TakeFront()
        {
            if (!_wanted || !FacingCorrection.TryTurn(_current, _options.Value.BodyDepth == BodyDepth.Flat, out float seen))
            {
                return false;
            }

            SetFront(FacingCorrection.Next(_front.Value, seen));
            return true;
        }

        public void ResetFront()
        {
            SetFront(0f);
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

        /// <summary>The newest part time of an observation: how old the image behind it is. Pure.</summary>
        internal static double Newest(TrackingFrame frame)
        {
            double newest = Math.Max(Math.Max(TimeOf(frame.Body), TimeOf(frame.Face)), Math.Max(TimeOf(frame.LeftHand), TimeOf(frame.RightHand)));
            return double.IsNegativeInfinity(newest) ? frame.Time : newest;
        }

        internal void Tick()
        {
            _now = _clock.GetTimestamp() / (double)_clock.Frequency;
            ITrackingProvider active = Active;
            if (_wanted && active != null)
            {
                active.Tick();
                bool observed = active.TryTakeLatest(_raw);
                if (observed && _replay == null)
                {
                    Observed?.Invoke(_raw);
                }

                // Corrections and the 2D body work on a copy, so recordings keep what the provider observed
                // (tracking detailed design 8.4, 8.6, 12.2).
                TrackingOptions options = _options.Value;
                TrackingFrame observation = null;
                if (observed)
                {
                    observation = _raw;
                    bool flat = options.BodyDepth == BodyDepth.Flat;
                    if (_corrections.Any || flat)
                    {
                        _corrections.Apply(_raw, _corrected);
                        if (flat)
                        {
                            BodyFlattening.Flatten(_corrected.Body);
                        }

                        observation = _corrected;
                    }

                    if (_front.Value != 0f)
                    {
                        if (observation == _raw)
                        {
                            _corrected.CopyFrom(_raw);
                            observation = _corrected;
                        }

                        FacingCorrection.Turn(observation, _turnBack, body: !flat);
                    }
                }

                _processor.Process(observation, _now, active.Supported, options.Regions, options.Smoothing, _current);
                CountOutliers();
                if (observed)
                {
                    NoteObservation(Newest(_raw));
                }
            }

            Refresh();
            NoteChanges();
            _saves?.Tick();
        }

        private static double TimeOf(TrackedPart part)
        {
            return part.IsUsable ? part.Timestamp : double.NegativeInfinity;
        }

        private void NoteObservation(double imageTime)
        {
            _latency?.Record(TimeSpan.FromSeconds(Math.Max(0, _now - imageTime)));

            // Parts of one image arrive separately; count each image once.
            if (imageTime > _lastImage)
            {
                _lastImage = imageTime;
                _rate = _rate.Next(_now);
            }
        }

        /// <summary>Adds the values the processor dropped as outliers since the last frame to the Diagnostics table (tracking detailed design 11).</summary>
        private void CountOutliers()
        {
            long dropped = _processor.Outliers;
            if (dropped > _outliersCounted)
            {
                _outliers?.Add(dropped - _outliersCounted);
                _outliersCounted = dropped;
            }
        }

        private void SetFront(float degrees)
        {
            if (_front.Set(degrees))
            {
                _turnBack = FacingCorrection.TurnBack(degrees);
                _log?.Information($"Tracking front: {degrees:0.#} degrees from the camera's.");
            }
        }

        private void StopProvider()
        {
            _provider?.Stop();
            _camera?.Stop();
            _processor.Reset();
        }

        private void StopReplayProvider()
        {
            _replay?.Stop();
            _replay?.Dispose();
            _replay = null;
            ChooseCorrections();
        }

        /// <summary>The corrections that suit the active provider's observations.</summary>
        private void ChooseCorrections()
        {
            _corrections = ObservationCorrections.For(Traits, ObservationCorrections.Catalog);
        }

        private void Refresh()
        {
            bool personSeen = _current.Body.IsUsable || _current.Face.IsUsable || _current.LeftHand.IsUsable || _current.RightHand.IsUsable;
            (TrackingState state, TrackingProblem problem) = Describe(
                _wanted,
                Active != null,
                _camera != null && _replay == null,
                _camera?.State.Value ?? CaptureState.Stopped,
                _camera?.Problem.Value ?? CaptureProblem.None,
                Active?.State.Value ?? TrackingState.Stopped,
                personSeen);
            if (_state.Set(state))
            {
                _log?.Information($"Tracking is {state}{(problem == TrackingProblem.None ? string.Empty : $" ({problem})")}.");
            }

            _problem.Set(problem);
        }

        private void NoteChanges()
        {
            // Compared every frame, so nothing is allocated unless something changed. A replay keeps
            // the saved running state from before it.
            bool running = _replay != null ? _wantedBeforeReplay : _wanted;
            if (_options.Value.Equals(_settings.GetOptions()) && running == _settings.Running && _front.Value == _settings.FrontDegrees)
            {
                return;
            }

            _settings = _settings.With(_options.Value, running, _front.Value);
            _saves?.Changed(_settings.Clone());
        }
    }
}
