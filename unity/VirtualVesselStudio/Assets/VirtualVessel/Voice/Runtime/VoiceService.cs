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

        private const int ChannelCount = 2;
        private const int WarmupBlocks = 3;

        // The stage's output comes in bursts, so the first one is put back by this much silence, which
        // every later burst inherits, to let a late burst arrive before the mix runs dry.
        private const int MarginFrames = 1440;

        // More waiting than this and the oldest is left out: being late would last for the whole stream.
        private const int MaxWaitingBlocks = 2;

        private static readonly RvcStreamShape s_shape = new RvcStreamShape(100, 10, 2, 1, 2, 3, 42, 32);

        private readonly IAudioStage _stage;
        private readonly IFrameLoop _frameLoop;
        private readonly string _folder;
        private readonly ILog _log;
        private readonly ObservableValue<VoiceConversionState> _state = new ObservableValue<VoiceConversionState>(VoiceConversionState.Off);

        private IDisposable _tick;
        private Task<Model[]> _loading;
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

        private void BeginLoading()
        {
            if (!_stage.IsAvailable || string.IsNullOrEmpty(_folder) || !Array.TrueForAll(ModelPaths(_folder), File.Exists))
            {
                _log?.Warning($"Voice conversion is unavailable: stage {_stage.IsAvailable}, model folder '{_folder}'.");
                _state.Set(VoiceConversionState.Unavailable);
                return;
            }

            _state.Set(VoiceConversionState.Loading);
            string[] paths = ModelPaths(_folder);
            _loading = Task.Run(() => Array.ConvertAll(paths, ModelLoader.Load));
        }

        private void Tick()
        {
            if (_loading != null && _loading.IsCompleted)
            {
                FinishLoading();
            }

            if (_state.Value != VoiceConversionState.Running)
            {
                return;
            }

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
                _stream = new RvcStream(models[0], models[1], models[2], s_shape, BackendType.GPUCompute, _semitones);
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
            _state.Set(VoiceConversionState.Running);
        }

        private void Stop()
        {
            if (_state.Value == VoiceConversionState.Running)
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

                _startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                _stream.Start(new ReadOnlySpan<float>(_waiting, 0, block));
                Consume(block);
            }
        }

        private void Collect()
        {
            int room = _waiting.Length - _waitingSamples;
            int read = _stage.Read(_stereo, Math.Min(room, _stereo.Length / ChannelCount));
            ToMono(new ReadOnlySpan<float>(_stereo, 0, read * ChannelCount), new Span<float>(_waiting, _waitingSamples, read));
            _waitingSamples += read;
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
