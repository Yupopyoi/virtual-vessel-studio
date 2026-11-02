using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Unity.InferenceEngine;
using VirtualVessel.Audio;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.State;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.UI.Voice;
using VirtualVessel.Voice.Rvc;

namespace VirtualVessel.Voice
{
    /// <summary>
    /// Runs <see cref="RvcStream"/> as a stage on the microphone. Inference Engine must be driven from the
    /// main thread, so each frame moves blocks along and the GPU works between frames; the mix never waits
    /// for it (audio detailed design 8.3). A prototype: developers turn it on, and it is not saved.
    /// </summary>
    public sealed class VoiceService : IApplicationService, IVoiceConversion
    {
        public const string ServiceName = "Voice";

        /// <summary>The prototype's folder under the Data Root's VoiceModels.</summary>
        public const string PrototypeFolder = "Prototype";

        private const int ChannelCount = 2;
        private const int WarmupBlocks = 3;
        private const int ModelCount = 3;

        // The stage's output comes in bursts, so the first one is put back by this much silence, which
        // every later burst inherits, to let a late burst arrive before the mix runs dry.
        private const int MarginFrames = 1440;

        // More waiting than this and the oldest is left out: being late would last for the whole stream.
        private const int MaxWaitingBlocks = 2;

        // A window of 1 s. A 5.4 s window steadied held notes in a benchmark, but with the stream video,
        // tracking, and the avatar sharing the GPU it fell behind and the voice broke up (decision record).
        private static readonly RvcStreamShape s_shape = new RvcStreamShape(100, 10, 2, 1, 2, 3, 42, 32);

        private readonly IAudioStage _stage;
        private readonly IFrameLoop _frameLoop;
        private readonly string _folder;
        private readonly ILog _log;
        private readonly ObservableValue<VoiceConversionState> _state = new ObservableValue<VoiceConversionState>(VoiceConversionState.Off);

        private IDisposable _tick;
        private Task<Model[]> _loading;
        private float[] _speaker;
        private RvcStream _stream;
        private float[] _stereo;
        private float[] _waiting;
        private float[] _block;
        private float[] _output;
        private int _waitingSamples;
        private bool _primed;
        private bool _wanted;
        private float _semitones;
        private long _startedAt;
        private long _blocks;
        private long _dropped;
        private VoiceConversionStats _stats;
        private float[] _file;
        private string _fileName;
        private int _loadedModels;
        private bool _denoise;
        private int _fileRead;
        private readonly System.Diagnostics.Stopwatch _fileClock = new System.Diagnostics.Stopwatch();

        /// <param name="folder">Holds the .sentis models; null or missing leaves the conversion unavailable.</param>
        public VoiceService(IAudioStage stage, IFrameLoop frameLoop, string folder, ILog log)
        {
            _stage = stage ?? throw new ArgumentNullException(nameof(stage));
            _frameLoop = frameLoop ?? throw new ArgumentNullException(nameof(frameLoop));
            _folder = folder;
            _log = log;
        }

        public string Name => ServiceName;

        public IReadOnlyObservable<VoiceConversionState> State => _state;

        public VoiceConversionStats Stats => _stats;

        private bool IsAttached => _state.Value == VoiceConversionState.Running || _state.Value == VoiceConversionState.NoMicrophone || _state.Value == VoiceConversionState.PlayingFile;

        public bool Denoise
        {
            get => _denoise;
            set => _denoise = value;
        }

        // The last step, warming up, is counted as much as a model: it takes as long.
        public float LoadProgress => _loadedModels / (ModelCount + 1f);

        public string FileName => _file == null ? null : _fileName;

        public (float Position, float Length) FileTime => _file == null ? (0f, 0f) : (_fileRead / (float)RvcStream.DeviceRate, (_file.Length - RvcStream.DeviceRate) / (float)RvcStream.DeviceRate);

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
                _state.Set(VoiceConversionState.Off);
                ReleaseTick();
                return;
            }

            // Only while it is wanted: turned off, the conversion costs the frames nothing.
            _tick ??= _frameLoop.Register(Tick);
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
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                _log?.Warning($"This WAV file cannot be read: {exception.Message}");
                return false;
            }

            _file = samples;
            _fileName = Path.GetFileName(path);
            _fileRead = 0;
            _fileClock.Reset();
            SetEnabled(true);
            return true;
        }

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

        private static string SpeakerPath(string folder)
        {
            return Path.Combine(folder, "gen_b10.spk");
        }

        private static string[] ModelPaths(string folder)
        {
            return new[]
            {
                Path.Combine(folder, "hubert_16000.sentis"),
                Path.Combine(folder, "rmvpe_32.sentis"),
                Path.Combine(folder, "gen_b10.sentis"),
            };
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

        // A second of silence at the end carries the last block through, whatever its length.
        private static float[] ReadFile(string path)
        {
            float[] samples = WavFile.ToRate(WavFile.Read(File.ReadAllBytes(path), out int rate), rate, RvcStream.DeviceRate);
            Array.Resize(ref samples, samples.Length + RvcStream.DeviceRate);
            return samples;
        }

        private void BeginLoading()
        {
            if (!_stage.IsAvailable || string.IsNullOrEmpty(_folder) || !Array.TrueForAll(ModelPaths(_folder), File.Exists) || !File.Exists(SpeakerPath(_folder)))
            {
                _log?.Warning($"Voice conversion is unavailable: stage {_stage.IsAvailable}, model folder '{_folder}'.");
                _state.Set(VoiceConversionState.Unavailable);
                return;
            }

            _loadedModels = 0;
            _state.Set(VoiceConversionState.Loading);
            string[] paths = ModelPaths(_folder);
            string speaker = SpeakerPath(_folder);
            _loading = Task.Run(() =>
            {
                _speaker = ReadSpeaker(speaker);
                return Array.ConvertAll(paths, LoadModel);
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
            Task<Model[]> loading = _loading;
            _loading = null;
            try
            {
                Model[] models = loading.GetAwaiter().GetResult();
                _stream = new RvcStream(models[0], models[1], models[2], _speaker, s_shape, BackendType.GPUCompute, _semitones);
                _stereo = new float[RvcStream.DeviceRate * ChannelCount];
                _waiting = new float[RvcStream.DeviceRate];
                _block = new float[_stream.BlockSamples];
                _output = new float[_stream.BlockSamples * ChannelCount];

                // The first runs compile the shaders and take far longer than a block, so they happen
                // here, where nobody is waiting for the audio yet.
                var silence = new float[_stream.BlockSamples];
                for (int i = 0; i < WarmupBlocks; i++)
                {
                    _stream.Process(silence, silence);
                    Array.Clear(silence, 0, silence.Length);
                }

                _log?.Information($"Voice conversion is ready: block {_stream.BlockSamples / 48} ms, algorithm delay {_stream.AlgorithmDelaySamples / 48} ms.");
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
                _log?.Error("The voice conversion models could not start.", exception);
                _stream?.Dispose();
                _stream = null;
                _state.Set(VoiceConversionState.Failed);
            }
        }

        private void Attach()
        {
            _waitingSamples = 0;
            _primed = false;
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

        private void Pump()
        {
            Collect();
            if (_stream.IsBusy && _stream.Advance(_block, false))
            {
                Deliver();
            }

            int block = _stream.BlockSamples;
            if (!_stream.IsBusy && _waitingSamples >= block)
            {
                int drop = SamplesToDrop(_waitingSamples, block, MaxWaitingBlocks);
                if (drop > 0)
                {
                    Consume(drop);
                    _dropped += drop / block;
                }

                // The block is a whole number of the denoiser's 10 ms frames, so nothing waits for it.
                if (_denoise)
                {
                    _stage.Denoise(_waiting, block);
                }

                _startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
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
            double work = (System.Diagnostics.Stopwatch.GetTimestamp() - _startedAt) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _stats = new VoiceConversionStats(_stream.BlockSamples / 48.0, work, _stream.Timings.Issue, _blocks, _dropped);
        }
    }
}
