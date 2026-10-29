using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.UI.Recording;

namespace VirtualVessel.Video.Recording
{
    /// <summary>
    /// Records the stream video to MP4 files (recording detailed design 7): checks the folder and the free
    /// space, opens the encoder on a worker thread, reads frames back while recording, and finishes the
    /// file on a worker thread, so the main thread never waits for the encoder. An Optional application
    /// service after Video: without it there is no recording, and everything else runs.
    /// </summary>
    public sealed class RecordingService : IApplicationService, IRecordingService
    {
        public const string ServiceName = "Recording";

        /// <summary>How long Stop waits for readbacks still on their way before finishing the file.</summary>
        private static readonly TimeSpan s_readbackWait = TimeSpan.FromSeconds(1);

        private static readonly TimeSpan s_statusInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan s_spaceInterval = TimeSpan.FromSeconds(10);

        private readonly ConfigSaveScheduler<RecordingSettings> _saves;
        private readonly VideoSize _size;
        private readonly IFrameLoop _frameLoop;
        private readonly IMonotonicClock _clock;
        private readonly RecordingEnvironment _environment;
        private readonly ILog _log;
        private readonly ObservableValue<RecordingState> _state = new ObservableValue<RecordingState>(RecordingState.Idle);
        private readonly ObservableValue<RecordingProblem> _problem = new ObservableValue<RecordingProblem>(RecordingProblem.None);
        private readonly ObservableValue<RecordingOptions> _options;
        private readonly PerfCounter _written;
        private readonly PerfCounter _dropped;
        private readonly PerfCounter _skipped;
        private readonly PerfGauge _latency;
        private readonly PerfGauge _avOffset;
        private RecordingSettings _settings;
        private Session _session;
        private IDisposable _tick;

        public RecordingService(
            RecordingSettings settings,
            ConfigSaveScheduler<RecordingSettings> saves,
            VideoSize size,
            IFrameLoop frameLoop,
            IMonotonicClock clock,
            RecordingEnvironment environment,
            IPerformanceMetrics metrics,
            ILog log)
        {
            _settings = (settings ?? throw new ArgumentNullException(nameof(settings))).Usable();
            _saves = saves;
            _size = size;
            _frameLoop = frameLoop ?? throw new ArgumentNullException(nameof(frameLoop));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _log = log;
            _options = new ObservableValue<RecordingOptions>(OptionsOf(_settings, size, environment.VideosFolder, environment.Av1Available));
            _written = metrics?.Counter("Recording", "FramesWritten");
            _dropped = metrics?.Counter("Recording", "FramesDropped");
            _skipped = metrics?.Counter("Recording", "FramesSkipped");
            _latency = metrics?.Gauge("Recording", "ReadbackLatency", "frames");
            _avOffset = metrics?.Gauge("Recording", "AvOffset", "ms");
        }

        public string Name => ServiceName;

        public IReadOnlyObservable<RecordingState> State => _state;

        public IReadOnlyObservable<RecordingProblem> Problem => _problem;

        public TimeSpan Elapsed => _state.Value == RecordingState.Recording && _session != null
            ? _clock.GetElapsed(_session.StartedAt, _clock.GetTimestamp())
            : TimeSpan.Zero;

        public string LastFile { get; private set; }

        public IReadOnlyObservable<RecordingOptions> Options => _options;

        public static ConfigFileStore<RecordingSettings> CreateStore(string directory, ISystemClock clock, string fileName = RecordingSettings.FileName)
        {
            return new ConfigFileStore<RecordingSettings>(directory, fileName, RecordingSettings.SchemaVersion, () => new RecordingSettings(), clock, RecordingSettings.Migrations);
        }

        /// <summary>What the user sees of <paramref name="settings"/>. Pure.</summary>
        public static RecordingOptions OptionsOf(RecordingSettings settings, VideoSize size, string videosFolder, bool av1Available)
        {
            return new RecordingOptions(
                RecordingNames.Folder(settings.RecordingFolder, videosFolder),
                RecordingNames.QualityOf(settings.VideoBitrate),
                RecordingNames.CodecOf(settings.VideoCodec, av1Available),
                av1Available,
                size.Width,
                size.Height,
                settings.FrameRate);
        }

        /// <summary>
        /// A copy of <paramref name="settings"/> with the user's folder, quality, and format. The default folder
        /// is kept as null, so it follows the Videos folder; an unchanged quality keeps a bitrate edited in the
        /// file, and an unchanged format keeps the choice unmade, so it follows the PC's GPU. Pure.
        /// </summary>
        public static RecordingSettings With(RecordingSettings settings, RecordingOptions options, string videosFolder, bool av1Available)
        {
            RecordingSettings next = settings.Clone();
            next.RecordingFolder = string.IsNullOrWhiteSpace(options.Folder) || string.Equals(options.Folder, RecordingNames.DefaultFolder(videosFolder), StringComparison.OrdinalIgnoreCase)
                ? null
                : options.Folder;
            if (RecordingNames.QualityOf(settings.VideoBitrate) != options.Quality)
            {
                next.VideoBitrate = RecordingNames.BitrateOf(options.Quality);
            }

            if (RecordingNames.CodecOf(settings.VideoCodec, av1Available) != options.Codec)
            {
                next.VideoCodec = RecordingNames.SettingOf(options.Codec);
            }

            return next;
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            _tick = _frameLoop.Register(Tick);
            _log?.Information($"Recording ready: {_options.Value.Codec} (AV1 encoder {(_environment.Av1Available ? "found" : "not found")}), {_size.Width}x{_size.Height} at {_settings.FrameRate} fps, {_settings.VideoBitrate / 1000} kbit/s, to {_options.Value.Folder}.");
            return Task.CompletedTask;
        }

        public void SetOptions(RecordingOptions options)
        {
            _settings = With(_settings, options, _environment.VideosFolder, _environment.Av1Available);
            _options.Set(OptionsOf(_settings, _size, _environment.VideosFolder, _environment.Av1Available));
            _saves?.Changed(_settings.Clone());
        }

        public long? FreeBytes()
        {
            return _environment.FreeBytes(_options.Value.Folder);
        }

        public void Start()
        {
            if (_session != null)
            {
                return;
            }

            string folder = _options.Value.Folder;
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is NotSupportedException)
            {
                _log?.Warning($"Recording cannot start: the folder {folder} cannot be created.", exception);
                Fail(RecordingProblem.FolderUnavailable);
                return;
            }

            long? free = _environment.FreeBytes(folder);
            if (!RecordingNames.HasRoom(free))
            {
                _log?.Warning($"Recording cannot start: {free / (1024 * 1024)} MB free in {folder}, {RecordingNames.StartReserveBytes / (1024 * 1024)} MB needed.");
                Fail(RecordingProblem.DiskFull);
                return;
            }

            string path = RecordingNames.NewFile(folder, _environment.LocalNow(), File.Exists);
            // The stream bus as it is now; without it the recording goes on with video only (recording detailed design 12).
            RecordingAudio audio = _environment.Audio();
            var format = new EncoderFormat(_size.Width, _size.Height, _settings.FrameRate, _settings.VideoBitrate, _options.Value.Codec).WithAudio(audio, _settings.AudioBitrate);
            Func<string, EncoderFormat, IVideoEncoder> open = _environment.OpenEncoder;

            // Opening starts Media Foundation and the encoder, which can take a moment: not on the main thread.
            _session = new Session(path, format, Task.Run(() => open(path, format)));
            _problem.Set(audio.IsValid ? RecordingProblem.None : RecordingProblem.AudioUnavailable);
            _state.Set(RecordingState.Starting);
        }

        public void Stop()
        {
            if (_session == null)
            {
                return;
            }

            if (_state.Value == RecordingState.Starting)
            {
                _session.StopWhenOpen = true;
            }
            else if (_state.Value == RecordingState.Recording)
            {
                BeginStop(RecordingProblem.None);
            }
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            _tick?.Dispose();
            _tick = null;
            Session session = _session;
            if (session != null)
            {
                // A recording in progress is finished, so the file plays (recording detailed design 7).
                session.Reader?.Release();
                try
                {
                    IVideoEncoder encoder = session.Encoder ?? await session.Opening.ConfigureAwait(false);
                    Task<EncoderStatus> closing = session.Closing ?? Task.Run(() => encoder.Close());
                    EncoderStatus status = await closing.ConfigureAwait(false);
                    LogFinished(session, status, RecordingProblem.None);
                }
                catch (Exception exception)
                {
                    _log?.Error($"The recording {Path.GetFileName(session.Path)} could not be finished at shutdown.", exception);
                }

                _session = null;
            }

            if (_saves != null)
            {
                await _saves.FlushAsync().ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            _tick?.Dispose();
            _tick = null;

            // Only when shutdown did not run: the file is finished here rather than left unplayable.
            if (_session?.Encoder != null && _session.Closing == null)
            {
                _session.Reader?.Release();
                _session.Encoder.Close();
            }

            _session = null;
        }

        /// <summary>Once per frame, on the main thread.</summary>
        internal void Tick()
        {
            _saves?.Tick();
            Session session = _session;
            if (session == null)
            {
                return;
            }

            long now = _clock.GetTimestamp();
            switch (_state.Value)
            {
                case RecordingState.Starting:
                    Opened(session, now);
                    break;
                case RecordingState.Recording:
                    session.Reader.Tick();
                    Watch(session, now);
                    break;
                case RecordingState.Stopping:
                    Finish(session, now);
                    break;
            }
        }

        private void Opened(Session session, long now)
        {
            if (!session.Opening.IsCompleted)
            {
                return;
            }

            if (session.Opening.IsFaulted || session.Opening.IsCanceled)
            {
                _log?.Error($"Recording cannot start: the encoder did not open for {Path.GetFileName(session.Path)}.", session.Opening.Exception?.GetBaseException());
                _session = null;
                Fail(RecordingProblem.EncoderFailed);
                return;
            }

            session.Encoder = session.Opening.Result;
            session.Reader = _environment.ReadFrames(session.Format, session.Encoder);
            session.StartedAt = now;
            session.NextStatus = now + Ticks(s_statusInterval);
            session.NextSpace = now + Ticks(s_spaceInterval);
            _state.Set(RecordingState.Recording);
            _log?.Information($"Recording started: {Path.GetFileName(session.Path)} in {Path.GetDirectoryName(session.Path)}, {session.Format.Codec} {session.Format.Width}x{session.Format.Height} at {session.Format.FrameRate} fps, {session.Format.VideoBitrate / 1000} kbit/s, {(session.Format.Audio.IsValid ? $"audio {session.Format.AudioBitrate / 1000} kbit/s" : "no audio")}.");
            if (session.StopWhenOpen)
            {
                BeginStop(RecordingProblem.None);
            }
        }

        private void Watch(Session session, long now)
        {
            if (now >= session.NextStatus)
            {
                session.NextStatus = now + Ticks(s_statusInterval);
                EncoderStatus status = session.Encoder.GetStatus();
                Count(session, status);
                if (status.Failed)
                {
                    _log?.Error($"The encoder failed while recording {Path.GetFileName(session.Path)} (HRESULT 0x{status.LastError:X8}); the part written is kept.");
                    BeginStop(RecordingProblem.EncoderFailed);
                    return;
                }
            }

            if (now >= session.NextSpace)
            {
                session.NextSpace = now + Ticks(s_spaceInterval);
                long? free = _environment.FreeBytes(Path.GetDirectoryName(session.Path));
                if (RecordingNames.MustStop(free))
                {
                    _log?.Warning($"Recording stopped: {free / (1024 * 1024)} MB left on the disk of {Path.GetFileName(session.Path)}.");
                    BeginStop(RecordingProblem.DiskFull);
                }
            }
        }

        private void BeginStop(RecordingProblem problem)
        {
            Session session = _session;
            session.Reader.Stop();
            session.Failure = problem;
            session.StopRequested = _clock.GetTimestamp();
            _state.Set(RecordingState.Stopping);
        }

        private void Finish(Session session, long now)
        {
            if (session.Closing == null)
            {
                // The readbacks on their way are the last frames; they arrive within a frame or two.
                if (session.Reader.Waiting > 0 && _clock.GetElapsed(session.StopRequested, now) < s_readbackWait)
                {
                    return;
                }

                session.Reader.Release();
                IVideoEncoder encoder = session.Encoder;
                session.Closing = Task.Run(() => encoder.Close());
                return;
            }

            if (!session.Closing.IsCompleted)
            {
                return;
            }

            _session = null;
            RecordingProblem problem = session.Failure;
            if (session.Closing.IsFaulted || session.Closing.IsCanceled)
            {
                _log?.Error($"The recording {Path.GetFileName(session.Path)} could not be finished.", session.Closing.Exception?.GetBaseException());
                problem = problem == RecordingProblem.None ? RecordingProblem.EncoderFailed : problem;
            }
            else
            {
                EncoderStatus status = session.Closing.Result;
                Count(session, status);
                problem = problem == RecordingProblem.None && status.Failed ? RecordingProblem.EncoderFailed : problem;
                LogFinished(session, status, problem);
            }

            if (File.Exists(session.Path))
            {
                LastFile = session.Path;
            }

            if (problem == RecordingProblem.None)
            {
                _state.Set(RecordingState.Idle);
            }
            else
            {
                Fail(problem);
            }
        }

        private void Fail(RecordingProblem problem)
        {
            _problem.Set(problem);
            _state.Set(RecordingState.Failed);
        }

        /// <summary>Adds what changed since the last look to the metrics.</summary>
        private void Count(Session session, EncoderStatus status)
        {
            _written?.Add(status.Written - session.Counted.Written);
            _dropped?.Add(status.Dropped - session.Counted.Dropped);
            long skipped = session.Reader?.Skipped ?? 0;
            _skipped?.Add(skipped - session.CountedSkipped);
            _latency?.Set(session.Reader?.LatencyFrames ?? 0);
            if (session.Format.Audio.IsValid)
            {
                _avOffset?.Set(status.AvOffsetMs);
            }
            session.Counted = status;
            session.CountedSkipped = skipped;
        }

        private void LogFinished(Session session, EncoderStatus status, RecordingProblem problem)
        {
            long bytes = File.Exists(session.Path) ? new FileInfo(session.Path).Length : 0;
            string encoder = status.Hardware ? "hardware" : "software";
            string outcome = problem == RecordingProblem.None ? "finished" : $"ended early ({problem})";
            _log?.Information($"Recording {outcome}: {Path.GetFileName(session.Path)}, {bytes / (1024 * 1024)} MB, {status.Written} frames written, {status.Dropped} dropped, {session.Reader?.Skipped ?? 0} skipped, {encoder} encoder, {status.AudioFrames / 48000.0:0.0} s of audio.");
        }

        private long Ticks(TimeSpan span)
        {
            return (long)(span.TotalSeconds * _clock.Frequency);
        }

        /// <summary>One recording, from Start until its file is finished.</summary>
        private sealed class Session
        {
            public Session(string path, EncoderFormat format, Task<IVideoEncoder> opening)
            {
                Path = path;
                Format = format;
                Opening = opening;
            }

            public string Path { get; }

            public EncoderFormat Format { get; }

            public Task<IVideoEncoder> Opening { get; }

            public IVideoEncoder Encoder { get; set; }

            public IStreamFrameReader Reader { get; set; }

            public Task<EncoderStatus> Closing { get; set; }

            public bool StopWhenOpen { get; set; }

            public RecordingProblem Failure { get; set; }

            public long StartedAt { get; set; }

            public long StopRequested { get; set; }

            public long NextStatus { get; set; }

            public long NextSpace { get; set; }

            public EncoderStatus Counted { get; set; }

            public long CountedSkipped { get; set; }
        }
    }
}
