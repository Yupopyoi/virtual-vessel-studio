using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.Audio
{
    /// <summary>
    /// The application service of the Audio module (audio detailed design 7): builds the sources of the
    /// project's audio profile in the engine, follows their states and levels every tenth of a second, and
    /// saves the user's changes. Optional: without it there is no sound, and everything else runs.
    /// </summary>
    public sealed class AudioService : IApplicationService, IAudioService
    {
        public const string ServiceName = "Audio";

        /// <summary>How often states and levels are read from the engine (audio detailed design 9).</summary>
        private static readonly TimeSpan s_pollInterval = TimeSpan.FromSeconds(0.1);

        private readonly Func<IAudioEngine> _createEngine;
        private readonly ConfigSaveScheduler<AudioSettings> _saves;
        private readonly IFrameLoop _frameLoop;
        private readonly IMonotonicClock _clock;
        private readonly Func<string> _newId;
        private readonly IPerformanceMetrics _metrics;
        private readonly ILog _log;
        private readonly ObservableValue<IReadOnlyList<AudioDevice>> _devices = new ObservableValue<IReadOnlyList<AudioDevice>>(Array.Empty<AudioDevice>());
        private readonly List<Source> _sources = new List<Source>();
        private readonly ObservableValue<TimeSpan> _streamDelay = new ObservableValue<TimeSpan>(TimeSpan.FromMilliseconds(AudioSettings.DefaultStreamDelayMs));
        private AudioSettings _settings;
        private IAudioEngine _engine;
        private IDisposable _tick;
        private long _nextPoll;

        public AudioService(
            AudioSettings settings,
            ConfigSaveScheduler<AudioSettings> saves,
            Func<IAudioEngine> createEngine,
            IFrameLoop frameLoop,
            IMonotonicClock clock,
            Func<string> newId,
            IPerformanceMetrics metrics,
            ILog log)
        {
            _settings = settings ?? new AudioSettings();
            _saves = saves;
            _createEngine = createEngine ?? throw new ArgumentNullException(nameof(createEngine));
            _frameLoop = frameLoop ?? throw new ArgumentNullException(nameof(frameLoop));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _newId = newId ?? throw new ArgumentNullException(nameof(newId));
            _metrics = metrics;
            _log = log;
        }

        public string Name => ServiceName;

        public IReadOnlyList<IAudioSource> Sources => _sources;

        public IReadOnlyObservable<IReadOnlyList<AudioDevice>> InputDevices => _devices;

        public TimeSpan StreamLatency => _engine?.StreamLatency ?? TimeSpan.Zero;

        public AudioStreamTap StreamTap => _engine?.StreamTap ?? default;

        public IReadOnlyObservable<TimeSpan> StreamDelay => _streamDelay;

        public static ConfigFileStore<AudioSettings> CreateStore(string directory, ISystemClock clock, string fileName = AudioSettings.FileName)
        {
            return new ConfigFileStore<AudioSettings>(directory, fileName, AudioSettings.SchemaVersion, () => new AudioSettings(), clock, AudioSettings.Migrations);
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            _engine = _createEngine();
            IReadOnlyList<AudioDevice> devices = _engine.ListInputs();
            _devices.Set(devices);
            AudioSettings prepared = AudioSettings.Prepared(_settings, devices, _newId);
            if (prepared.Sources.Count != _settings.Sources?.Count)
            {
                _saves?.Changed(prepared.Clone());
            }

            _settings = prepared;
            foreach (AudioSourceSettings saved in _settings.Sources)
            {
                // A role this version does not know is kept in the profile but not played.
                if (AudioSettings.RoleOf(saved.Role) is AudioSourceRole role)
                {
                    var source = new Source(this, saved.Id, role, _engine.AddSource(), saved.ToOptions(), _metrics);
                    Apply(source.Engine, source.Options.Value);
                    _sources.Add(source);
                    _log?.Information($"Audio source {role} on {DeviceName(source.Options.Value.DeviceId, devices)}, gain {source.Options.Value.GainDb:0.#} dB{(source.Options.Value.Muted ? ", muted" : string.Empty)}.");
                }
            }

            _sources.Sort((a, b) => a.Role.CompareTo(b.Role));
            _streamDelay.Set(TimeSpan.FromMilliseconds(AudioSettings.ClampDelayMs(_settings.StreamDelayMs)));
            _engine.SetStreamDelay(_streamDelay.Value);
            _tick = _frameLoop.Register(Tick);
            _log?.Information($"Audio started: {devices.Count} input device(s), stream latency {StreamLatency.TotalMilliseconds:0} ms, recording and streaming {_streamDelay.Value.TotalMilliseconds:0} ms later.");
            return Task.CompletedTask;
        }

        public void SetStreamDelay(TimeSpan delay)
        {
            var clamped = TimeSpan.FromMilliseconds(AudioSettings.ClampDelayMs(delay.TotalMilliseconds));
            if (!_streamDelay.Set(clamped))
            {
                return;
            }

            _engine?.SetStreamDelay(clamped);
            _settings = _settings.Clone();
            _settings.StreamDelayMs = (int)clamped.TotalMilliseconds;
            _saves?.Changed(_settings.Clone());
        }

        public void RefreshDevices()
        {
            if (_engine != null)
            {
                _devices.Set(_engine.ListInputs());
            }
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            _tick?.Dispose();
            _tick = null;
            if (_saves != null)
            {
                await _saves.FlushAsync().ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            _tick?.Dispose();
            _tick = null;

            // The engine's threads stop and the devices are released (audio detailed design 7).
            _engine?.Dispose();
            _engine = null;
        }

        /// <summary>The device's name for the log, without its ID. Pure.</summary>
        public static string DeviceName(string deviceId, IReadOnlyList<AudioDevice> devices)
        {
            if (deviceId == null)
            {
                return "no device";
            }

            foreach (AudioDevice device in devices)
            {
                if (device.Id == deviceId)
                {
                    return device.Name;
                }
            }

            return "a device not connected now";
        }

        /// <summary>Once per frame: states and levels every tenth of a second, and saves when due.</summary>
        internal void Tick()
        {
            _saves?.Tick();
            long now = _clock.GetTimestamp();
            if (now < _nextPoll || _engine == null)
            {
                return;
            }

            _nextPoll = now + (long)(s_pollInterval.TotalSeconds * _clock.Frequency);
            foreach (Source source in _sources)
            {
                AudioEngineStatus status = _engine.GetStatus(source.Engine);
                AudioSourceState previous = source.State.Value;
                source.Update(status);
                if (previous != status.State)
                {
                    LogState(source, status);
                }
            }
        }

        private void LogState(Source source, AudioEngineStatus status)
        {
            string device = DeviceName(source.Options.Value.DeviceId, _devices.Value);
            switch (status.State)
            {
                case AudioSourceState.Running:
                    _log?.Information($"Audio source {source.Role} running on {device}.");
                    break;
                case AudioSourceState.Unavailable:
                    _log?.Warning($"Audio source {source.Role}: {device} is not connected; it opens again when it is back.");
                    break;
                case AudioSourceState.Error:
                    _log?.Warning($"Audio source {source.Role}: {device} cannot be opened ({status.Problem}, HRESULT 0x{status.LastError:X8}).");
                    break;
            }
        }

        private void Apply(int engineSource, AudioSourceOptions options)
        {
            _engine.SetDevice(engineSource, options.DeviceId);
            _engine.SetGain(engineSource, options.GainDb, options.Muted);
            _engine.SetStreamSend(engineSource, options.StreamSendDb);
        }

        private void Changed(Source source, AudioSourceOptions previous, AudioSourceOptions options)
        {
            if (previous.DeviceId != options.DeviceId)
            {
                _engine?.SetDevice(source.Engine, options.DeviceId);
                _log?.Information($"Audio source {source.Role} moves to {DeviceName(options.DeviceId, _devices.Value)}.");
            }

            _engine?.SetGain(source.Engine, options.GainDb, options.Muted);
            _engine?.SetStreamSend(source.Engine, options.StreamSendDb);
            _settings = WithSource(_settings, source.Id, options);
            _saves?.Changed(_settings.Clone());
        }

        /// <summary>A copy of the settings with one source's options. Pure.</summary>
        public static AudioSettings WithSource(AudioSettings settings, string id, AudioSourceOptions options)
        {
            AudioSettings next = settings.Clone();
            for (int i = 0; i < next.Sources.Count; i++)
            {
                if (next.Sources[i].Id == id)
                {
                    next.Sources[i] = next.Sources[i].With(options);
                }
            }

            return next;
        }

        private sealed class Source : IAudioSource
        {
            private readonly AudioService _owner;
            private readonly ObservableValue<AudioSourceState> _state = new ObservableValue<AudioSourceState>(AudioSourceState.Off);
            private readonly ObservableValue<AudioProblem> _problem = new ObservableValue<AudioProblem>(AudioProblem.None);
            private readonly ObservableValue<AudioSourceOptions> _options;
            private readonly PerfCounter _overruns;
            private readonly PerfCounter _underruns;
            private readonly PerfCounter _driftCorrections;
            private AudioEngineStatus _counted;

            public Source(AudioService owner, string id, AudioSourceRole role, int engine, AudioSourceOptions options, IPerformanceMetrics metrics)
            {
                _owner = owner;
                Id = id;
                Role = role;
                Engine = engine;
                _options = new ObservableValue<AudioSourceOptions>(options);
                PeakDb = float.NegativeInfinity;
                _overruns = metrics?.Counter("Audio", role + ".Overruns");
                _underruns = metrics?.Counter("Audio", role + ".Underruns");
                _driftCorrections = metrics?.Counter("Audio", role + ".DriftCorrections");
            }

            public string Id { get; }

            public AudioSourceRole Role { get; }

            public int Engine { get; }

            public IReadOnlyObservable<AudioSourceState> State => _state;

            public IReadOnlyObservable<AudioProblem> Problem => _problem;

            public IReadOnlyObservable<AudioSourceOptions> Options => _options;

            public float PeakDb { get; private set; }

            public void SetOptions(AudioSourceOptions options)
            {
                AudioSourceOptions previous = _options.Value;
                if (_options.Set(options))
                {
                    _owner.Changed(this, previous, options);
                }
            }

            public void Update(AudioEngineStatus status)
            {
                PeakDb = status.PeakDb;
                _problem.Set(status.Problem);
                _state.Set(status.State);
                _overruns?.Add(status.Overruns - _counted.Overruns);
                _underruns?.Add(status.Underruns - _counted.Underruns);
                _driftCorrections?.Add(status.DriftCorrections - _counted.DriftCorrections);
                _counted = status;
            }
        }
    }
}
