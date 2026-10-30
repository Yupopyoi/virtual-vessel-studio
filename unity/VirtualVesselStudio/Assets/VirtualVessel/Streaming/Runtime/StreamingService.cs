using System;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.ProjectData.Credentials;
using VirtualVessel.UI.Streaming;
using VirtualVessel.Video;
using VirtualVessel.Video.Recording;

namespace VirtualVessel.Streaming
{
    /// <summary>
    /// Streams the stream video (streaming detailed design 7): reads the key, opens the plugin, reads frames
    /// while streaming, follows the plugin's connection state, and closes it on a worker thread, so the main
    /// thread never waits for the network. An Optional application service after Video: a stream that fails
    /// stops nothing else. The key is read only to open the plugin, and is never logged or returned.
    /// </summary>
    public sealed class StreamingService : IApplicationService, IStreamingService
    {
        public const string ServiceName = "Streaming";

        /// <summary>How long stopping waits for readbacks still on their way.</summary>
        private static readonly TimeSpan s_readbackWait = TimeSpan.FromSeconds(1);

        private static readonly TimeSpan s_statusInterval = TimeSpan.FromMilliseconds(250);

        /// <summary>The window of the actual bitrate; one sample a status.</summary>
        private const int BitrateSamples = 20;

        private readonly VideoSize _size;
        private readonly int _frameRate;
        private readonly int _audioBitrate;
        private readonly Action<StreamSettings> _save;
        private readonly IFrameLoop _frameLoop;
        private readonly IMonotonicClock _clock;
        private readonly ICredentialStore _credentials;
        private readonly StreamingEnvironment _environment;
        private readonly ILog _log;
        private readonly ObservableValue<StreamingState> _state = new ObservableValue<StreamingState>(StreamingState.Idle);
        private readonly ObservableValue<StreamingProblem> _problem = new ObservableValue<StreamingProblem>(StreamingProblem.None);
        private readonly ObservableValue<StreamingOptions> _options;
        private readonly PerfGauge _bitrate;
        private readonly PerfGauge _queue;
        private readonly PerfGauge _encodeTime;
        private readonly PerfGauge _avOffset;
        private readonly PerfCounter _dropped;
        private readonly PerfCounter _reconnects;
        private StreamSettings _settings;
        private Session _session;
        private IDisposable _tick;

        public StreamingService(
            StreamSettings settings,
            Action<StreamSettings> save,
            VideoSize size,
            int frameRate,
            int audioBitrate,
            IFrameLoop frameLoop,
            IMonotonicClock clock,
            ICredentialStore credentials,
            StreamingEnvironment environment,
            IPerformanceMetrics metrics,
            ILog log)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _save = save;
            _size = size;
            _frameRate = frameRate;
            _audioBitrate = audioBitrate;
            _frameLoop = frameLoop ?? throw new ArgumentNullException(nameof(frameLoop));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _log = log;
            _options = new ObservableValue<StreamingOptions>(StreamDestination.OptionsOf(_settings, _credentials.Exists(_settings.CredentialId), environment.Av1Available));
            _bitrate = metrics?.Gauge("Streaming", "Bitrate", "kbit/s");
            _queue = metrics?.Gauge("Streaming", "QueueMs", "ms");
            _encodeTime = metrics?.Gauge("Streaming", "EncodeTime", "ms");
            _avOffset = metrics?.Gauge("Streaming", "AvOffset", "ms");
            _dropped = metrics?.Counter("Streaming", "FramesDropped");
            _reconnects = metrics?.Counter("Streaming", "Reconnects");
        }

        public string Name => ServiceName;

        public IReadOnlyObservable<StreamingState> State => _state;

        public IReadOnlyObservable<StreamingProblem> Problem => _problem;

        public TimeSpan Elapsed => _session != null && _session.LiveAt != 0 && _state.Value != StreamingState.Stopping
            ? _clock.GetElapsed(_session.LiveAt, _clock.GetTimestamp())
            : TimeSpan.Zero;

        public IReadOnlyObservable<StreamingOptions> Options => _options;

        public StreamingStats Stats { get; private set; }

        /// <summary>The state and problem the user sees for what the plugin says; <paramref name="whenWell"/> is the problem while connected. Pure.</summary>
        public static (StreamingState State, StreamingProblem Problem) Follow(PublisherState state, PublisherProblem problem, StreamingProblem whenWell)
        {
            switch (state)
            {
                case PublisherState.Live:
                    return (StreamingState.Live, whenWell);
                case PublisherState.Reconnecting:
                    return (StreamingState.Reconnecting, StreamingProblem.ConnectionLost);
                case PublisherState.Failed:
                    return (StreamingState.Failed, ProblemOf(problem));
                default:
                    return (StreamingState.Connecting, whenWell);
            }
        }

        public static StreamingProblem ProblemOf(PublisherProblem problem)
        {
            switch (problem)
            {
                case PublisherProblem.Unreachable:
                    return StreamingProblem.ServerUnreachable;
                case PublisherProblem.Rejected:
                    return StreamingProblem.Rejected;
                case PublisherProblem.ConnectionLost:
                    return StreamingProblem.ConnectionLost;
                case PublisherProblem.EncoderFailed:
                    return StreamingProblem.EncoderFailed;
                default:
                    return StreamingProblem.None;
            }
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            _tick = _frameLoop.Register(Tick);
            StreamingOptions options = _options.Value;
            _log?.Information($"Streaming ready: {options.Destination} at {StreamDestination.HostOf(options.ServerUrl)}, {options.Codec}, {options.VideoBitrate / 1000} kbit/s, stream key {(options.KeyRegistered ? "registered" : "not registered")}.");
            return Task.CompletedTask;
        }

        public void SetOptions(StreamingOptions options)
        {
            if (_session != null)
            {
                return;
            }

            _settings = StreamDestination.With(_settings, options, _environment.Av1Available);
            _options.Set(StreamDestination.OptionsOf(_settings, _options.Value.KeyRegistered, _environment.Av1Available));
            _save?.Invoke(_settings.Clone());
        }

        public bool SetStreamKey(string key)
        {
            if (_session != null)
            {
                return false;
            }

            string cleaned = StreamDestination.CleanKey(key);
            if (cleaned.Length == 0)
            {
                return RemoveKey();
            }

            if (!CredentialId.IsValid(_settings.CredentialId))
            {
                _settings = _settings.Clone();
                _settings.CredentialId = CredentialId.New();
                _save?.Invoke(_settings.Clone());
            }

            CredentialResult result = _credentials.Write(_settings.CredentialId, cleaned);
            if (result.Status != CredentialStatus.Ok)
            {
                _log?.Warning($"The stream key could not be registered: {result.Status} (system error {result.ErrorCode}).");
                return false;
            }

            _log?.Information("A stream key was registered.");
            SetKeyRegistered(true);
            return true;
        }

        public async Task<StreamingProblem> TestConnectionAsync()
        {
            string url = _options.Value.ServerUrl;
            if (!StreamDestination.IsUsableUrl(url))
            {
                return StreamingProblem.ServerUnreachable;
            }

            Func<string, PublisherProblem> test = _environment.TestConnection;
            try
            {
                StreamingProblem problem = ProblemOf(await Task.Run(() => test(url)).ConfigureAwait(true));
                _log?.Information($"Connection test to {StreamDestination.HostOf(url)}: {problem}.");
                return problem;
            }
            catch (StreamPublisherException exception)
            {
                _log?.Warning($"Connection test to {StreamDestination.HostOf(url)} could not run.", exception);
                return StreamingProblem.EncoderFailed;
            }
        }

        public void Start()
        {
            if (_session != null)
            {
                return;
            }

            StreamingOptions options = _options.Value;
            if (!CredentialId.IsValid(_settings.CredentialId) || _credentials.Read(_settings.CredentialId, out string key).Status != CredentialStatus.Ok)
            {
                _log?.Warning("Streaming cannot start: no stream key is registered.");
                Fail(StreamingProblem.NoStreamKey);
                return;
            }

            if (!StreamDestination.IsUsableUrl(options.ServerUrl))
            {
                _log?.Warning("Streaming cannot start: the server URL cannot be used.");
                Fail(StreamingProblem.ServerUnreachable);
                return;
            }

            // Without the stream bus the stream goes on with video only (streaming detailed design 12).
            RecordingAudio audio = _environment.Audio();
            var format = new StreamFormat(options.ServerUrl, _size.Width, _size.Height, _frameRate, options.Codec, options.VideoBitrate, _audioBitrate, audio);
            IStreamPublisher publisher;
            IStreamFrameReader reader;
            try
            {
                // Opening starts the plugin's threads and does not wait for them, so the main thread can do it.
                publisher = _environment.OpenPublisher(format, key);
            }
            catch (StreamPublisherException exception)
            {
                _log?.Error("Streaming cannot start: the plugin did not open.", exception);
                Fail(StreamingProblem.EncoderFailed);
                return;
            }

            try
            {
                reader = _environment.ReadFrames(format, publisher);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException)
            {
                _log?.Error("Streaming cannot start: the stream video cannot be read.", exception);
                Task.Run(publisher.Close);
                Fail(StreamingProblem.EncoderFailed);
                return;
            }

            long now = _clock.GetTimestamp();
            _session = new Session(format, publisher, reader, now);
            _problem.Set(audio.IsValid ? StreamingProblem.None : StreamingProblem.AudioUnavailable);
            _state.Set(StreamingState.Connecting);
            _log?.Information($"Streaming to {StreamDestination.HostOf(format.Url)}: {format.Codec} {format.Width}x{format.Height} at {format.FrameRate} fps, {format.VideoBitrate / 1000} kbit/s, {(audio.IsValid ? $"audio {format.AudioBitrate / 1000} kbit/s" : "no audio")}.");
        }

        public void Stop()
        {
            if (_session != null && _state.Value != StreamingState.Stopping)
            {
                BeginStop(StreamingProblem.None);
            }
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            _tick?.Dispose();
            _tick = null;
            Session session = _session;
            if (session != null)
            {
                // A stream in progress sends what is queued and disconnects (streaming detailed design 7).
                session.Reader.Release();
                Task closing = session.Closing ?? Task.Run(session.Publisher.Close);
                await closing.ConfigureAwait(false);
                session.Reader.Dispose();
                _session = null;
            }
        }

        public void Dispose()
        {
            _tick?.Dispose();
            _tick = null;

            // Only when shutdown did not run.
            if (_session != null)
            {
                _session.Reader.Release();
                _session.Reader.Dispose();
                if (_session.Closing == null)
                {
                    _session.Publisher.Close();
                }
            }

            _session = null;
        }

        /// <summary>Once per frame, on the main thread.</summary>
        internal void Tick()
        {
            Session session = _session;
            if (session == null)
            {
                return;
            }

            long now = _clock.GetTimestamp();
            if (_state.Value == StreamingState.Stopping)
            {
                Finish(session, now);
                return;
            }

            session.Reader.Tick();
            if (now >= session.NextStatus)
            {
                session.NextStatus = now + Ticks(s_statusInterval);
                Watch(session, now);
            }
        }

        private void Watch(Session session, long now)
        {
            PublisherStatus status = session.Publisher.GetStatus();
            Count(session, status, now);
            (StreamingState state, StreamingProblem problem) = Follow(status.State, status.Problem, session.Format.Audio.IsValid ? StreamingProblem.None : StreamingProblem.AudioUnavailable);
            if (state == StreamingState.Failed)
            {
                _log?.Error($"The stream failed: {problem}.");
                BeginStop(problem);
                return;
            }

            if (state == StreamingState.Live && session.LiveAt == 0)
            {
                session.LiveAt = now;
                _log?.Information($"Streaming is live, {(status.Hardware ? "hardware" : "software")} encoder.");
            }

            if (state != _state.Value)
            {
                _log?.Information($"Streaming is {state} ({problem}).");
            }

            _problem.Set(problem);
            _state.Set(state);
        }

        private void BeginStop(StreamingProblem problem)
        {
            Session session = _session;
            session.Reader.Stop();
            session.Failure = problem;
            session.StopRequested = _clock.GetTimestamp();
            _state.Set(StreamingState.Stopping);
        }

        private void Finish(Session session, long now)
        {
            if (session.Closing == null)
            {
                // The readbacks on their way are the last frames; they arrive within a frame or two.
                if (session.Failure == StreamingProblem.None && session.Reader.Waiting > 0 && _clock.GetElapsed(session.StopRequested, now) < s_readbackWait)
                {
                    return;
                }

                session.Reader.Release();
                session.Closing = Task.Run(session.Publisher.Close);
                return;
            }

            if (!session.Closing.IsCompleted)
            {
                return;
            }

            if (session.Closing.IsFaulted)
            {
                _log?.Error("The stream could not be closed.", session.Closing.Exception?.GetBaseException());
            }

            session.Reader.Dispose();
            _session = null;
            _log?.Information($"Streaming ended: {session.Counted.BytesSent / (1024 * 1024)} MB sent, {session.Counted.FramesSent} frames, {session.Counted.FramesDropped} dropped, {session.Counted.Reconnects} reconnects.");
            if (session.Failure == StreamingProblem.None)
            {
                _problem.Set(StreamingProblem.None);
                _state.Set(StreamingState.Idle);
            }
            else
            {
                Fail(session.Failure);
            }
        }

        private bool RemoveKey()
        {
            CredentialResult result = _credentials.Delete(_settings.CredentialId);
            if (result.Status != CredentialStatus.Ok && result.Status != CredentialStatus.NotFound && result.Status != CredentialStatus.Invalid)
            {
                _log?.Warning($"The stream key could not be removed: {result.Status} (system error {result.ErrorCode}).");
                return false;
            }

            _log?.Information("The stream key was removed.");
            SetKeyRegistered(false);
            return true;
        }

        private void SetKeyRegistered(bool registered)
        {
            _options.Set(StreamDestination.OptionsOf(_settings, registered, _environment.Av1Available));
        }

        private void Fail(StreamingProblem problem)
        {
            _problem.Set(problem);
            _state.Set(StreamingState.Failed);
        }

        /// <summary>Adds what changed since the last look to the metrics.</summary>
        private void Count(Session session, PublisherStatus status, long now)
        {
            PublisherStatus last = session.Counted;
            long skipped = session.Reader.Skipped;
            _dropped?.Add((status.FramesDropped - last.FramesDropped) + (skipped - session.CountedSkipped));
            _reconnects?.Add(status.Reconnects - last.Reconnects);
            long frames = status.FramesSent - last.FramesSent;
            if (frames > 0)
            {
                _encodeTime?.Set((status.EncodeMicroseconds - last.EncodeMicroseconds) / 1000.0 / frames);
            }

            _queue?.Set(status.QueueMs);
            _avOffset?.Set(status.AvOffsetMs);

            int oldest = session.Samples < BitrateSamples ? 0 : session.NextSample;
            long bitsPerSecond = BitsPerSecond(status.BytesSent - session.Bytes[oldest], _clock.GetElapsed(session.Times[oldest], now).TotalSeconds);
            session.Times[session.NextSample] = now;
            session.Bytes[session.NextSample] = status.BytesSent;
            session.NextSample = (session.NextSample + 1) % BitrateSamples;
            session.Samples = Math.Min(session.Samples + 1, BitrateSamples);

            _bitrate?.Set(bitsPerSecond / 1000.0);
            Stats = new StreamingStats(bitsPerSecond, status.QueueMs, status.FramesDropped + skipped, status.Reconnects);
            session.Counted = status;
            session.CountedSkipped = skipped;
        }

        internal static long BitsPerSecond(long bytes, double seconds)
        {
            return seconds > 0 ? (long)(bytes * 8 / seconds) : 0;
        }

        private long Ticks(TimeSpan span)
        {
            return (long)(span.TotalSeconds * _clock.Frequency);
        }

        /// <summary>One stream, from Start until the plugin is closed.</summary>
        private sealed class Session
        {
            public Session(StreamFormat format, IStreamPublisher publisher, IStreamFrameReader reader, long startedAt)
            {
                Format = format;
                Publisher = publisher;
                Reader = reader;
                Times[0] = startedAt;
                NextSample = 1;
                Samples = 1;
            }

            public StreamFormat Format { get; }

            public IStreamPublisher Publisher { get; }

            public IStreamFrameReader Reader { get; }

            public Task Closing { get; set; }

            public StreamingProblem Failure { get; set; }

            public long LiveAt { get; set; }

            public long StopRequested { get; set; }

            public long NextStatus { get; set; }

            public PublisherStatus Counted { get; set; }

            public long CountedSkipped { get; set; }

            public long[] Times { get; } = new long[BitrateSamples];

            public long[] Bytes { get; } = new long[BitrateSamples];

            public int NextSample { get; set; }

            public int Samples { get; set; }
        }
    }
}
