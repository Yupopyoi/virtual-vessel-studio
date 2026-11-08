using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Unity.InferenceEngine;
using VirtualVessel.Audio;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.State;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Voice;
using VirtualVessel.Voice.Models;
using VirtualVessel.Voice.Rvc;

namespace VirtualVessel.Voice
{
    /// <summary>
    /// Runs <see cref="RvcStream"/> as a stage on the microphone. Inference Engine must be driven from the
    /// main thread, so each frame moves blocks along and the GPU works between frames; the mix never waits
    /// for it (audio detailed design 8.3). A prototype: developers turn it on, and it is not saved.
    /// A chosen voice loads behind the one converting and takes over at a block's end, so the audio never
    /// stops for a switch (system design 9.8, Voice detailed design 8.2).
    /// </summary>
    public sealed class VoiceService : IApplicationService, IVoiceConversion
    {
        public const string ServiceName = "Voice";

        private const int ChannelCount = 2;
        private const int WarmupBlocks = 3;

        // The stage's output comes in bursts, so the first one is put back by this much silence, which
        // every later burst inherits, to let a late burst arrive before the mix runs dry.
        private const int MarginFrames = 1440;

        // How long a frame may spend preparing the next voice's layers; a few ms keep the frame well inside a block.
        private const double PrimeBudgetMs = 4.0;

        // More waiting than this and the oldest is left out: being late would last for the whole stream.
        private const int MaxWaitingBlocks = 2;

        /// <summary>
        /// The window the models see, in 10 ms frames: 1 s only. With the user's speech before it, the user heard
        /// no gain from 2.5 s or 5.4 s, and 1 s is the lightest (decision record, 2026-11-03).
        /// </summary>
        public const int WindowFrames = VoiceModelLibrary.WindowFrames;

        private readonly IAudioStage _stage;
        private readonly IFrameLoop _frameLoop;
        private readonly VoiceModelLibrary _library;
        private readonly ILog _log;
        private readonly ObservableValue<VoiceConversionState> _state = new ObservableValue<VoiceConversionState>(VoiceConversionState.Off);

        private IDisposable _tick;
        private Task<LoadedVoice> _loading;
        private RvcStream _stream;
        private string _streamVoice;
        private string _wantedVoice;

        // HuBERT and RMVPE are the same for every voice, so a switch loads only the generator and speaker.
        private Model _features;
        private Model _pitch;
        private string _featuresPath;
        private string _pitchPath;

        // A switch's stream warming up behind the one converting, a block of silence at a time.
        private RvcStream _next;
        private string _nextVoice;
        private int _nextWarmed;
        private float[] _warmSilence;
        private float[] _warmOutput;
        private System.Collections.IEnumerator _priming;
        private int _modelsToLoad;
        private float[] _stereo;
        private float[] _waiting;
        private float[] _block;
        private float[] _output;
        private int _waitingSamples;
        private bool _primed;
        private bool _wanted;
        private readonly PerfTimer _workTimer;
        private readonly PerfTimer _issueTimer;
        private readonly PerfCounter _droppedCounter;
        private readonly PerfTimer[] _stageTimers;
        private readonly PerfTimer[] _delayTimers;
        private readonly double[] _delayParts = new double[DelayParts.Length];
        private readonly IPerformanceMetrics _metrics;
        private double _lastWorkMs;
        private float _semitones;
        // When each block on its way started; two can be on their way at once.
        private readonly Queue<long> _startedAt = new Queue<long>(4);
        private long _blocks;
        private long _dropped;
        private VoiceConversionStats _stats;
        private float[] _file;
        private string _fileName;
        private int _loadedModels;
        private bool _denoise;
        private int _fileRead;
        private readonly System.Diagnostics.Stopwatch _fileClock = new System.Diagnostics.Stopwatch();

        /// <param name="library">The voices; null leaves the conversion unavailable.</param>
        /// <param name="metrics">Optional; each block's work and the blocks left out, for the totals at exit.</param>
        public VoiceService(IAudioStage stage, IFrameLoop frameLoop, VoiceModelLibrary library, ILog log, IPerformanceMetrics metrics = null)
        {
            _workTimer = metrics?.Timer("Voice", "BlockWork");
            _issueTimer = metrics?.Timer("Voice", "BlockIssue");
            _droppedCounter = metrics?.Counter("Voice", "DroppedBlocks");
            _stageTimers = new[] { "Prepare", "Features", "Pitch", "Generator", "Finish" }.Select(name => metrics?.Timer("Voice", "Issue." + name)).ToArray();
            _metrics = metrics;
            // Timers, not gauges: they are durations, and the totals at exit list timers.
            _delayTimers = DelayParts.Select(name => metrics?.Timer("Voice", "Delay." + name)).ToArray();
            _stage = stage ?? throw new ArgumentNullException(nameof(stage));
            _frameLoop = frameLoop ?? throw new ArgumentNullException(nameof(frameLoop));
            _library = library;
            _log = log;
        }

        public string Name => ServiceName;

        public IReadOnlyObservable<VoiceConversionState> State => _state;

        public VoiceConversionStats Stats => _stats;

        private bool IsAttached => _state.Value == VoiceConversionState.Running || _state.Value == VoiceConversionState.NoMicrophone || _state.Value == VoiceConversionState.PlayingFile;

        public string VoiceId => _wantedVoice;

        public bool IsSwitching => _next != null || (_loading != null && _stream != null);

        public CrudResult<IReadOnlyList<VoiceConversionVoice>> ListVoices()
        {
            return _library?.List() ?? CrudResult<IReadOnlyList<VoiceConversionVoice>>.Success(Array.Empty<VoiceConversionVoice>());
        }

        public void SelectVoice(string voiceId)
        {
            _wantedVoice = voiceId;
            if (_tick == null && _stream != null && _streamVoice != voiceId)
            {
                // Off, nothing is heard, so the old voice is simply let go; turning on loads the new one.
                _stream.Dispose();
                _stream = null;
            }
        }

        public bool Denoise
        {
            get => _denoise;
            set => _denoise = value;
        }

        // The last step, warming up, is counted as much as a model: it takes as long.
        public float LoadProgress => _modelsToLoad == 0 ? 0f : _loadedModels / (_modelsToLoad + 1f);

        public string FileName => _file == null ? null : _fileName;

        public (float Position, float Length) FileTime => _file == null ? (0f, 0f) : (_fileRead / (float)RvcStream.DeviceRate, (_file.Length - RvcStream.DeviceRate) / (float)RvcStream.DeviceRate);

        /// <summary>The last block's times on the main thread, for diagnostics.</summary>
        public RvcTimings LastTimings { get; private set; }

        /// <summary>What stopped the conversion, for tests and diagnostics.</summary>
        public Exception Failure { get; private set; }

        public float Semitones
        {
            get => _semitones;
            set
            {
                _semitones = value;
                if (_stream != null)
                {
                    _stream.Semitones = value;
                }
            }
        }

        /// <summary>What to show while on: running only when there is a voice to convert. Pure.</summary>
        public static VoiceConversionState AttachedState(bool capturing)
        {
            return capturing ? VoiceConversionState.Running : VoiceConversionState.NoMicrophone;
        }

        /// <summary>Samples to leave out so that no more than <paramref name="maxBlocks"/> wait. Pure.</summary>
        public static int SamplesToDrop(int waiting, int block, int maxBlocks)
        {
            int excess = waiting - maxBlocks * block;
            return excess <= 0 ? 0 : (excess + block - 1) / block * block;
        }

        /// <summary>Mixes interleaved stereo into mono, for a voice that a device may give on one channel. Pure apart from the target.</summary>
        public static void ToMono(ReadOnlySpan<float> stereo, Span<float> mono)
        {
            for (int i = 0; i < mono.Length; i++)
            {
                mono[i] = (stereo[i * ChannelCount] + stereo[i * ChannelCount + 1]) * 0.5f;
            }
        }

        /// <summary>Copies mono to both channels. Pure apart from the target.</summary>
        public static void ToStereo(ReadOnlySpan<float> mono, Span<float> stereo)
        {
            for (int i = 0; i < mono.Length; i++)
            {
                stereo[i * ChannelCount] = mono[i];
                stereo[i * ChannelCount + 1] = mono[i];
            }
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void SetEnabled(bool enabled)
        {
            _wanted = enabled;
            if (!enabled)
            {
                _file = null;
                Stop();
                DropNext();
                _state.Set(VoiceConversionState.Off);
                ReleaseTick();
                return;
            }

            // Only while it is wanted: turned off, the conversion costs the frames nothing.
            _tick ??= _frameLoop.Register(Tick);
            if (_stream != null && _streamVoice != _wantedVoice && !IsAttached)
            {
                _stream.Dispose();
                _stream = null;
            }

            if (_stream != null)
            {
                Attach();
            }
            else if (_loading == null)
            {
                BeginLoading();
            }
        }

        public async Task<bool> PlayFileAsync(string path)
        {
            float[] samples;
            try
            {
                samples = await Task.Run(() => ReadFile(path));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException || exception is AudioEngineException)
            {
                _log?.Warning($"This audio file cannot be read: {exception.Message}");
                return false;
            }

            _file = samples;
            _fileName = Path.GetFileName(path);
            _fileRead = 0;
            _fileClock.Reset();
            SetEnabled(true);
            return true;
        }

        public IReadOnlyList<string> FileExtensions => AudioFile.Extensions;

        public void StopFile()
        {
            _file = null;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            _wanted = false;
            Stop();
            _tick?.Dispose();
            _tick = null;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _tick?.Dispose();
            _tick = null;
            DropNext();
            _stream?.Dispose();
            _stream = null;
        }

        /// <summary>The voice's speaker vector: 256 little-endian floats, beside its generator.</summary>
        public static float[] ReadSpeaker(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            var speaker = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, speaker, 0, speaker.Length * sizeof(float));
            return speaker;
        }

        /// <summary>The crossfade at each join, in 10 ms frames; the generators are exported for it.</summary>
        public const int Crossfade = 5;

        private static RvcStreamShape Shape(int windowFrames)
        {
            // A 50 ms crossfade: with 20 ms the joins changed the sound every block, heard on held notes as a
            // wobble in pitch (decision record, 2026-11-03).
            return new RvcStreamShape(windowFrames, 10, Crossfade, 1, 2, 3, 10 + Crossfade + 1 + 2 + 3 + 24, 32);
        }

        // A load in progress still needs the frames to finish on the main thread.
        private void ReleaseTick()
        {
            if (_loading == null)
            {
                _tick?.Dispose();
                _tick = null;
            }
        }

        // A second of silence at the end carries the last block through, whatever its length. WAV is read here,
        // without the audio plugin; other formats, such as M4A from a phone, go through Windows' decoders.
        private static float[] ReadFile(string path)
        {
            float[] decoded = string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase)
                ? WavFile.Read(File.ReadAllBytes(path), out int rate)
                : AudioFile.ReadMono(path, out rate);
            float[] samples = WavFile.ToRate(decoded, rate, RvcStream.DeviceRate);
            Array.Resize(ref samples, samples.Length + RvcStream.DeviceRate);
            return samples;
        }

        // What a load gives back: the shared models, which may be the ones already loaded, and the voice's own.
        private sealed class LoadedVoice
        {
            public string VoiceId;
            public string FeaturesPath;
            public string PitchPath;
            public Model Features;
            public Model Pitch;
            public Model Generator;
            public float[] Speaker;
        }

        private void BeginLoading()
        {
            if (_wantedVoice == null && _library != null)
            {
                CrudResult<IReadOnlyList<VoiceConversionVoice>> voices = _library.List();
                _wantedVoice = voices.Ok ? VoiceModelLibrary.FirstUsable(voices.Value) : null;
            }

            VoiceModelFiles files = _library == null || _wantedVoice == null ? VoiceModelFiles.Unusable("There is no voice.") : _library.Locate(_wantedVoice);
            bool switching = _stream != null;
            if (!_stage.IsAvailable || files.Problem != null)
            {
                _log?.Warning($"Voice conversion cannot load voice '{_wantedVoice}': stage {_stage.IsAvailable}; {files.Problem}");
                if (switching)
                {
                    // The voice in use keeps converting.
                    _wantedVoice = _streamVoice;
                }
                else
                {
                    _state.Set(VoiceConversionState.Unavailable);
                }

                return;
            }

            // Read here, on the main thread, so the loading thread touches no field it does not own.
            Model features = files.Features == _featuresPath ? _features : null;
            Model pitch = files.Pitch == _pitchPath ? _pitch : null;
            _loadedModels = 0;
            _modelsToLoad = 1 + (features == null ? 1 : 0) + (pitch == null ? 1 : 0);
            if (!switching)
            {
                _state.Set(VoiceConversionState.Loading);
            }

            string voiceId = _wantedVoice;
            _loading = Task.Run(() => new LoadedVoice
            {
                VoiceId = voiceId,
                FeaturesPath = files.Features,
                PitchPath = files.Pitch,
                Features = features ?? LoadModel(files.Features),
                Pitch = pitch ?? LoadModel(files.Pitch),
                Generator = LoadModel(files.Generator),
                Speaker = ReadSpeaker(files.Speaker),
            });
        }

        private Model LoadModel(string path)
        {
            Model model = ModelLoader.Load(path);
            Interlocked.Increment(ref _loadedModels);
            return model;
        }

        private void Tick()
        {
            if (_loading != null && _loading.IsCompleted)
            {
                FinishLoading();
            }

            if (!IsAttached)
            {
                return;
            }

            if (_next != null)
            {
                WarmNext();
            }
            else if (_loading == null && _wantedVoice != _streamVoice)
            {
                BeginLoading();
            }

            _state.Set(_file != null ? VoiceConversionState.PlayingFile : AttachedState(_stage.IsCapturing));
            try
            {
                Pump();
            }
            catch (Exception exception)
            {
                _log?.Error("Voice conversion failed. The microphone goes back to the mix directly.", exception);
                Failure = exception;
                Stop();
                _state.Set(VoiceConversionState.Failed);
            }
        }

        private void FinishLoading()
        {
            Task<LoadedVoice> loading = _loading;
            _loading = null;
            bool switching = _stream != null && IsAttached;
            RvcStream stream = null;
            try
            {
                LoadedVoice voice = loading.GetAwaiter().GetResult();
                _features = voice.Features;
                _pitch = voice.Pitch;
                _featuresPath = voice.FeaturesPath;
                _pitchPath = voice.PitchPath;
                stream = new RvcStream(voice.Features, voice.Pitch, voice.Generator, voice.Speaker, Shape(WindowFrames), BackendType.GPUCompute, _semitones);
                if (switching)
                {
                    // Warmed a block at a time over the next frames, while the voice in use keeps converting.
                    _next = stream;
                    _nextVoice = voice.VoiceId;
                    _nextWarmed = 0;
                    _priming = stream.Prime();
                    _warmSilence ??= new float[stream.BlockSamples];
                    _warmOutput ??= new float[stream.BlockSamples];
                    _log?.Information($"Voice {voice.VoiceId} loaded; warming up behind voice {_streamVoice}.");
                    return;
                }

                _stereo ??= new float[RvcStream.DeviceRate * ChannelCount];
                _waiting ??= new float[RvcStream.DeviceRate];
                _block ??= new float[stream.BlockSamples];
                _output ??= new float[stream.BlockSamples * ChannelCount];

                // The first runs compile the shaders and take far longer than a block, so they happen
                // here, where nobody is waiting for the audio yet.
                var silence = new float[stream.BlockSamples];
                for (int i = 0; i < WarmupBlocks; i++)
                {
                    stream.Process(silence, silence);
                    Array.Clear(silence, 0, silence.Length);
                }

                _stream?.Dispose();
                _stream = stream;
                _streamVoice = voice.VoiceId;
                _log?.Information($"Voice conversion is ready with voice {voice.VoiceId}: block {_stream.BlockSamples / 48} ms, algorithm delay {_stream.AlgorithmDelaySamples / 48} ms.");
                if (_wanted)
                {
                    Attach();
                }
                else
                {
                    _state.Set(VoiceConversionState.Off);
                    ReleaseTick();
                }
            }
            catch (Exception exception)
            {
                stream?.Dispose();
                if (switching)
                {
                    _log?.Error($"The chosen voice could not load; voice {_streamVoice} keeps converting.", exception);
                    _wantedVoice = _streamVoice;
                    return;
                }

                _log?.Error("The voice conversion models could not start.", exception);
                _stream?.Dispose();
                _stream = null;
                _state.Set(VoiceConversionState.Failed);
            }
        }

        // One step of the next voice's warm-up each frame: its first runs compile shaders, which must not stall the
        // frame that keeps the voice in use converting.
        private void WarmNext()
        {
            if (_priming != null)
            {
                var budget = System.Diagnostics.Stopwatch.StartNew();
                while (budget.Elapsed.TotalMilliseconds < PrimeBudgetMs)
                {
                    if (!_priming.MoveNext())
                    {
                        _priming = null;
                        break;
                    }
                }

                return;
            }

            if (_next.IsBusy)
            {
                if (_next.Advance(_warmOutput, false))
                {
                    _nextWarmed++;
                }
            }
            else if (_nextWarmed < WarmupBlocks)
            {
                Array.Clear(_warmSilence, 0, _warmSilence.Length);
                _next.Start(_warmSilence);
            }
        }

        /// <summary>
        /// Whether a warmed voice takes over now: only between blocks, so no block is lost, and only if it is still
        /// the chosen one. Pure.
        /// </summary>
        public static bool ShouldSwap(bool hasNext, int warmed, int warmupBlocks, bool currentBusy, bool nextBusy)
        {
            return hasNext && warmed >= warmupBlocks && !currentBusy && !nextBusy;
        }

        private void SwapIfReady()
        {
            if (_next == null || _nextWarmed < WarmupBlocks)
            {
                return;
            }

            if (_nextVoice != _wantedVoice)
            {
                // Chosen and then changed again before it was ready.
                DropNext();
                return;
            }

            if (!ShouldSwap(true, _nextWarmed, WarmupBlocks, _stream.IsBusy, _next.IsBusy))
            {
                return;
            }

            _log?.Information($"Voice {_nextVoice} takes over from voice {_streamVoice}.");
            _stream.Dispose();
            _stream = _next;
            _stream.Semitones = _semitones;
            _streamVoice = _nextVoice;
            _next = null;
            _nextVoice = null;
        }

        private void DropNext()
        {
            _priming = null;
            _next?.Dispose();
            _next = null;
            _nextVoice = null;
            _nextWarmed = 0;
        }

        private void Attach()
        {
            _waitingSamples = 0;
            _primed = false;
            _startedAt.Clear();
            _stage.SetEnabled(true);
            _state.Set(AttachedState(_stage.IsCapturing));
        }

        private void Stop()
        {
            if (IsAttached)
            {
                _stage.SetEnabled(false);
            }

            _stream?.Abandon();
        }

        /// <summary>The parts of the delay from the microphone to the monitor, in the order of DelayMs.</summary>
        public static readonly string[] DelayParts = { "CaptureDevice", "StageInput", "Waiting", "Work", "Algorithm", "StageOutput", "Monitor", "OutputBuffer", "OutputDevice", "Total" };

        /// <summary>
        /// The delay's parts in milliseconds into <paramref name="parts"/>, in the order of DelayParts, ending
        /// with their sum. Each queue's part is its average fill, which by Little's law is the average wait in it.
        /// Pure apart from writing the caller's buffer, which is reused every frame to avoid garbage.
        /// </summary>
        public static void DelayMs(AudioStageDelay outside, int waitingSamples, double workMs, int algorithmSamples, Span<double> parts)
        {
            const double MsPerSample = 1000.0 / RvcStream.DeviceRate;
            parts[0] = outside.CaptureDeviceMs;
            parts[1] = outside.StageInputMs;
            parts[2] = waitingSamples * MsPerSample;
            parts[3] = workMs;
            parts[4] = algorithmSamples * MsPerSample;
            parts[5] = outside.StageOutputMs;
            parts[6] = outside.MonitorMs;
            parts[7] = outside.OutputBufferMs;
            parts[8] = outside.OutputDeviceMs;
            double total = 0.0;
            for (int i = 0; i < 9; i++)
            {
                total += parts[i];
            }

            parts[9] = total;
        }

        private void Pump()
        {
            Collect();
            if (_stream.Advance(_block, false))
            {
                Deliver();
            }

            SwapIfReady();
            StartBlock();
            RecordDelay();
        }

        // What waits here stays until the next frame, so one sample a frame is its average. Only while the
        // totals record timers, so the call to the engine costs nothing otherwise.
        private void RecordDelay()
        {
            if (_metrics == null || !_metrics.IsDetailedEnabled || _file != null)
            {
                return;
            }

            DelayMs(_stage.Delay, _waitingSamples, _lastWorkMs, _stream.AlgorithmDelaySamples, _delayParts);
            for (int i = 0; i < _delayParts.Length; i++)
            {
                _delayTimers[i].Record(TimeSpan.FromMilliseconds(_delayParts[i]));
            }
        }

        private void StartBlock()
        {
            int block = _stream.BlockSamples;
            if (_stream.CanStart && _waitingSamples >= block)
            {
                int drop = SamplesToDrop(_waitingSamples, block, MaxWaitingBlocks);
                if (drop > 0)
                {
                    Consume(drop);
                    _dropped += drop / block;
                    _droppedCounter?.Add(drop / block);
                }

                // The block is a whole number of the denoiser's 10 ms frames, so nothing waits for it.
                if (_denoise)
                {
                    _stage.Denoise(_waiting, block);
                }

                _startedAt.Enqueue(System.Diagnostics.Stopwatch.GetTimestamp());
                _stream.Start(new ReadOnlySpan<float>(_waiting, 0, block));
                Consume(block);
            }
        }

        private void Collect()
        {
            int room = _waiting.Length - _waitingSamples;
            int read = _stage.Read(_stereo, Math.Min(room, _stereo.Length / ChannelCount));
            if (_file != null)
            {
                // What the microphone captured is dropped, so it does not arrive late after the file.
                CollectFile(room);
                return;
            }

            ToMono(new ReadOnlySpan<float>(_stereo, 0, read * ChannelCount), new Span<float>(_waiting, _waitingSamples, read));
            _waitingSamples += read;
        }

        // At real time, like a microphone, so that it can be heard as it will sound on a stream.
        private void CollectFile(int room)
        {
            _fileClock.Start();
            long due = _fileClock.ElapsedMilliseconds * RvcStream.DeviceRate / 1000;
            int take = (int)Math.Min(room, Math.Min(due - _fileRead, _file.Length - _fileRead));
            Array.Copy(_file, _fileRead, _waiting, _waitingSamples, take);
            _fileRead += take;
            _waitingSamples += take;
            if (_fileRead == _file.Length)
            {
                _file = null;
            }
        }

        private void Consume(int samples)
        {
            _waitingSamples -= samples;
            Array.Copy(_waiting, samples, _waiting, 0, _waitingSamples);
        }

        private void Deliver()
        {
            ToStereo(_block, _output);
            if (!_primed)
            {
                _primed = true;
                var margin = new float[MarginFrames * ChannelCount];
                _stage.Write(margin, MarginFrames);
            }

            _stage.Write(_output, _block.Length);
            _blocks++;
            long started = _startedAt.Count > 0 ? _startedAt.Dequeue() : System.Diagnostics.Stopwatch.GetTimestamp();
            double work = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _workTimer?.Record(TimeSpan.FromMilliseconds(work));
            _lastWorkMs = work;
            RvcTimings timings = _stream.Timings;
            _issueTimer?.Record(TimeSpan.FromMilliseconds(timings.Issue));
            double[] stages = { timings.Prepare, timings.Features, timings.Pitch, timings.Generator, timings.Finish };
            for (int i = 0; i < stages.Length; i++)
            {
                _stageTimers[i]?.Record(TimeSpan.FromMilliseconds(stages[i]));
            }

            LastTimings = timings;
            _stats = new VoiceConversionStats(_stream.BlockSamples / 48.0, work, _stream.Timings.Issue, _blocks, _dropped);
        }
    }
}
