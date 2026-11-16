using System;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Audio.Clips;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.ProjectData.Records;

namespace VirtualVessel.Audio.Playback
{
    /// <summary>
    /// The BGM player (BGM and SE detailed design 7, 8.1): asks the sequence for a track, has the engine's deck play it,
    /// and follows the deck every tenth of a second to start the next when one ends. Main thread.
    /// </summary>
    public sealed class BgmPlayer : IApplicationService, IBgmPlayer
    {
        public const string ServiceName = "BGM";

        private static readonly TimeSpan s_pollInterval = TimeSpan.FromSeconds(0.1);

        private readonly Func<IAudioPlayerEngine> _engine;
        private readonly IAudioClipLibrary _library;
        private readonly IFrameLoop _frameLoop;
        private readonly IMonotonicClock _clock;
        private readonly ILog _log;
        private readonly PerfCounter _underruns;
        private readonly ObservableValue<BgmStatus> _status = new ObservableValue<BgmStatus>(new BgmStatus(BgmPlayState.Idle, null));
        private IBgmSequence _sequence;
        private BgmCue? _cue;
        private long _generation;
        private long _countedUnderruns;
        private long _nextPoll;
        private float _gainDb;
        private BgmEngineStatus _engineStatus;
        private IDisposable _tick;

        /// <param name="engine">The engine's players, once the audio service has started.</param>
        public BgmPlayer(Func<IAudioPlayerEngine> engine, IAudioClipLibrary library, IFrameLoop frameLoop, IMonotonicClock clock, ILog log, IPerformanceMetrics metrics = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _frameLoop = frameLoop ?? throw new ArgumentNullException(nameof(frameLoop));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log;
            _underruns = metrics?.Counter("Audio", "Music.Underruns");
        }

        public string Name => ServiceName;

        public IReadOnlyObservable<BgmStatus> Status => _status;

        public float? Progress => _status.Value.State == BgmPlayState.Playing ? ProgressOf(_engineStatus.PlayedFrames, _engineStatus.LengthFrames, _cue?.Loop ?? false) : null;

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            _library.Changed += OnLibraryChanged;
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            _library.Changed -= OnLibraryChanged;
            _tick?.Dispose();
            _tick = null;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _tick?.Dispose();
            _tick = null;
        }

        public void Play(IBgmSequence sequence)
        {
            _sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
            Start(_sequence.Next(null));
        }

        public void Stop()
        {
            _sequence = null;
            if (_status.Value.State == BgmPlayState.Idle || _status.Value.State == BgmPlayState.Failed)
            {
                _cue = null;
                _status.Set(new BgmStatus(BgmPlayState.Idle, null));
                return;
            }

            _engine()?.StopBgm();
            _status.Set(new BgmStatus(BgmPlayState.Stopping, _status.Value.TrackId));
            _log?.Information($"BGM {_status.Value.TrackId} stops.");
        }

        /// <summary>
        /// How far through a track it is: a looped track goes round, so it is the place in the current round. Null when
        /// the length is unknown. Pure.
        /// </summary>
        public static float? ProgressOf(long played, long length, bool loop)
        {
            if (length <= 0)
            {
                return null;
            }

            long place = loop ? played % length : Math.Min(played, length);
            return place / (float)length;
        }

        /// <summary>What the player shows for the deck's status, for the play it asked for. Pure.</summary>
        public static BgmPlayState StateOf(BgmEngineStatus status, long generation)
        {
            if (status.Generation != generation)
            {
                // The deck has not taken this play yet.
                return BgmPlayState.Starting;
            }

            return status.State switch
            {
                BgmEngineState.Starting => BgmPlayState.Starting,
                BgmEngineState.Playing => BgmPlayState.Playing,
                BgmEngineState.Stopping => BgmPlayState.Stopping,
                BgmEngineState.Failed => BgmPlayState.Failed,
                _ => BgmPlayState.Idle,
            };
        }

        private void Start(BgmCue? cue)
        {
            IAudioPlayerEngine engine = _engine();
            if (cue == null || engine == null)
            {
                _cue = null;
                _sequence = null;
                engine?.StopBgm();
                _status.Set(new BgmStatus(engine == null ? BgmPlayState.Failed : BgmPlayState.Idle, null));
                return;
            }

            CrudResult<AudioClipRecord> track = _library.Get(cue.Value.TrackId);
            if (!track.Ok)
            {
                _log?.Warning($"BGM {cue.Value.TrackId} cannot be played ({track.Error}): {track.Detail}");
                _cue = cue;
                _status.Set(new BgmStatus(BgmPlayState.Failed, cue.Value.TrackId));
                return;
            }

            _cue = cue;
            _gainDb = track.Value.GainDb;
            _generation = engine.PlayBgm(track.Value.FilePath, cue.Value.Loop, _gainDb);

            // Followed on the frames only while a track is on: with none, the player costs the frames nothing.
            _tick ??= _frameLoop.Register(Tick);
            _status.Set(new BgmStatus(BgmPlayState.Starting, cue.Value.TrackId));
            _log?.Information($"BGM {cue.Value.TrackId} starts{(cue.Value.Loop ? ", looped" : string.Empty)}.");
        }

        private void Tick()
        {
            long now = _clock.GetTimestamp();
            if (now < _nextPoll)
            {
                return;
            }

            _nextPoll = now + (long)(s_pollInterval.TotalSeconds * _clock.Frequency);
            IAudioPlayerEngine engine = _engine();
            if (engine == null || _cue == null)
            {
                return;
            }

            _engineStatus = engine.GetBgmStatus();
            _underruns?.Add(_engineStatus.Underruns - _countedUnderruns);
            _countedUnderruns = _engineStatus.Underruns;

            // A track not looped played to its end: the sequence says what is next.
            if (_engineStatus.FinishedGeneration == _generation && _generation != 0 && _sequence != null)
            {
                BgmCue finished = _cue.Value;
                _log?.Information($"BGM {finished.TrackId} ended.");
                Start(_sequence.Next(finished));
                return;
            }

            BgmPlayState state = StateOf(_engineStatus, _generation);
            if (state == BgmPlayState.Failed && _status.Value.State != BgmPlayState.Failed)
            {
                _log?.Warning($"BGM {_cue.Value.TrackId} cannot be played (HRESULT 0x{_engineStatus.Error:X8}).");
            }

            if (state == BgmPlayState.Idle)
            {
                _cue = null;
                _sequence = null;
                _status.Set(new BgmStatus(BgmPlayState.Idle, null));
                _tick?.Dispose();
                _tick = null;
                return;
            }

            _status.Set(new BgmStatus(state, _cue.Value.TrackId));
        }

        // A change to the playing track's volume is heard at once. Raised on any thread; the engine's call is safe from any.
        private void OnLibraryChanged(AudioClipKind kind)
        {
            if (kind != AudioClipKind.Bgm || _cue == null)
            {
                return;
            }

            CrudResult<AudioClipRecord> track = _library.Get(_cue.Value.TrackId);
            if (track.Ok && track.Value.GainDb != _gainDb)
            {
                _gainDb = track.Value.GainDb;
                _engine()?.SetBgmGain(_gainDb);
            }
        }
    }
}
