using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.InferenceEngine;

namespace VirtualVessel.Voice.Rvc
{
    /// <summary>The window and block a set of exported models was made for; they fix the models' tensor sizes.</summary>
    public readonly struct RvcStreamShape
    {
        public RvcStreamShape(int windowFrames, int blockFrames, int crossfadeFrames, int searchFrames, int headGuardFrames, int tailGuardFrames, int noiseFrames, int pitchFrames)
        {
            WindowFrames = windowFrames;
            BlockFrames = blockFrames;
            CrossfadeFrames = crossfadeFrames;
            SearchFrames = searchFrames;
            HeadGuardFrames = headGuardFrames;
            TailGuardFrames = tailGuardFrames;
            NoiseFrames = noiseFrames;
            PitchFrames = pitchFrames;
        }

        /// <summary>10 ms frames the generator's encoder sees.</summary>
        public int WindowFrames { get; }

        public int BlockFrames { get; }

        public int CrossfadeFrames { get; }

        public int SearchFrames { get; }

        /// <summary>
        /// Decoded, then thrown away, at the start and end: the vocoder's convolutions lack context at
        /// its edges and their audio is quieter, which at a join is heard as a dip every block.
        /// </summary>
        public int HeadGuardFrames { get; }

        /// <summary>As <see cref="HeadGuardFrames"/>; these add to the delay.</summary>
        public int TailGuardFrames { get; }

        /// <summary>The tail of the window the decoder renders.</summary>
        public int DecodedFrames => BlockFrames + CrossfadeFrames + SearchFrames + HeadGuardFrames + TailGuardFrames;

        /// <summary>The tail of the window the flow runs on.</summary>
        public int NoiseFrames { get; }

        /// <summary>Frames of RMVPE's fixed window.</summary>
        public int PitchFrames { get; }
    }

    /// <summary>Where one block's time went, in milliseconds (system design 9.10).</summary>
    public struct RvcTimings
    {
        public double Prepare;
        public double Features;
        public double FeaturesWait;
        public double Pitch;
        public double PitchWait;
        public double Generator;
        public double GeneratorWait;
        public double Finish;

        /// <summary>The time on the calling thread if the waits were spent elsewhere.</summary>
        public double Issue => Prepare + Features + Pitch + Generator + Finish;

        public double Total => Issue + FeaturesWait + PitchWait + GeneratorWait;
    }

    /// <summary>
    /// Real-time RVC v2: one 48 kHz mono block in, one out, on Unity Inference Engine. It runs the way
    /// upstream's realtime path does (window for the features, a short RMVPE window with cached pitch,
    /// only the window's tail through the flow and decoder, then an in-phase crossfade).
    /// <para>
    /// A block makes one trip to the GPU: the generator takes HuBERT's and RMVPE's outputs on the GPU and
    /// turns them into its inputs itself, and only its audio and the pitch it used come back. Each trip
    /// waits for a frame, and two trips made a block take longer than a block under load.
    /// </para>
    /// </summary>
    public sealed class RvcStream : IDisposable
    {
        public const int DeviceRate = 48000;

        private const int GeneratorRate = 40000;
        private const int SamplesPerFrame16k = RvcDsp.HopSamples;
        private const int SamplesPerFrame48k = DeviceRate / 100;
        private const int LatentChannels = 192;
        private const int SpeakerChannels = 256;
        private const int NoiseLoopFrames = 1024;

        // 512 samples of reflected padding reach back this many hops from the window's end.
        private const int ReflectedFrames = 4;

        // The pitch frames the generator decodes from RMVPE each block; upstream drops RMVPE's first three and
        // its last, which lean on padded audio.
        private const int FreshPitchFrames = 28;

        private readonly RvcStreamShape _shape;
        private readonly Worker _features;
        private readonly Worker _pitch;
        private readonly Worker _generator;

        private readonly Tensor<float> _audioTensor;
        private readonly Tensor<float> _melTensor;
        private readonly Tensor<float> _historyTensor;
        private readonly Tensor<float> _lastTensor;
        private readonly Tensor<float> _factorTensor;
        private readonly Tensor<float> _noiseTensor;
        private readonly Tensor<float> _speakerTensor;
        private NativeArray<float> _audioData;
        private NativeArray<float> _melData;
        private NativeArray<float> _historyData;
        private NativeArray<float> _scalarData;
        private NativeArray<float> _noiseData;

        private readonly float[] _noiseLoop;
        private readonly float[] _history48k;
        private readonly float[] _window16k;
        private readonly float[] _leaving16k;
        private readonly SpeechAnchor _anchor;
        private readonly float[] _hann = RvcDsp.HannWindow(RvcDsp.FftSize);
        private readonly float[] _melBasis = RvcDsp.MelBasis();
        private readonly float[] _down = RvcDsp.ResampleKernels(3, 1);
        private readonly float[] _up = RvcDsp.ResampleKernels(5, 6);
        private readonly float[] _real = new float[RvcDsp.FftSize];
        private readonly float[] _imaginary = new float[RvcDsp.FftSize];
        private readonly float[] _cachedF0;
        private readonly float[] _decoded48k;
        private readonly float[] _previousTail;
        private readonly int _pitchSamples;
        private readonly int _reach48k;
        private readonly int _reach40k;

        private bool _busy;
        private bool _audioRead;
        private bool _pitchRead;
        private long _waitStart;
        private RvcTimings _timings;
        private RvcTimings _blockTimings;
        private Tensor<float> _audioOutput;
        private Tensor<float> _f0Output;
        private int _noiseFrame;
        private float _semitones;
        private float _lastF0;
        private bool _hasTail;
        private bool _disposed;

        public RvcStream(Model features, Model pitch, Model generator, float[] speaker, RvcStreamShape shape, BackendType backend, float semitones)
        {
            _shape = shape;
            _semitones = semitones;
            _features = new Worker(features, backend);
            _pitch = new Worker(pitch, backend);
            _generator = new Worker(generator, backend);

            int windowSamples = shape.WindowFrames * SamplesPerFrame16k;
            int historyFrames = shape.WindowFrames - FreshPitchFrames;
            _pitchSamples = (shape.PitchFrames - 1) * SamplesPerFrame16k;
            _reach48k = RvcDsp.ResampleReach(3, 1);
            _reach40k = RvcDsp.ResampleReach(5, 6);

            _audioData = new NativeArray<float>(SpeechAnchor.Samples + windowSamples, Allocator.Persistent);
            _melData = new NativeArray<float>(RvcDsp.MelBins * shape.PitchFrames, Allocator.Persistent);
            _historyData = new NativeArray<float>(historyFrames, Allocator.Persistent);
            _scalarData = new NativeArray<float>(1, Allocator.Persistent);
            _audioTensor = new Tensor<float>(new TensorShape(1, SpeechAnchor.Samples + windowSamples));
            _melTensor = new Tensor<float>(new TensorShape(1, RvcDsp.MelBins, shape.PitchFrames));
            _historyTensor = new Tensor<float>(new TensorShape(1, historyFrames));
            _lastTensor = new Tensor<float>(new TensorShape(1));
            _factorTensor = new Tensor<float>(new TensorShape(1));

            // Noise tied to time, sliding with the window, so the two windows meeting at a crossfade
            // give the same moment the same noise. A loop is enough: nothing can hear it repeat.
            _noiseLoop = new float[LatentChannels * NoiseLoopFrames];
            FillGaussian(_noiseLoop, new System.Random(1234));
            _noiseData = new NativeArray<float>(LatentChannels * shape.NoiseFrames, Allocator.Persistent);
            _speakerTensor = new Tensor<float>(new TensorShape(1, SpeakerChannels, 1), speaker);
            _noiseTensor = new Tensor<float>(new TensorShape(1, LatentChannels, shape.NoiseFrames));

            _history48k = new float[2 * _reach48k + shape.BlockFrames * SamplesPerFrame48k];
            _window16k = new float[windowSamples];
            _leaving16k = new float[shape.BlockFrames * SamplesPerFrame16k];
            _anchor = new SpeechAnchor(shape.WindowFrames / shape.BlockFrames);
            _cachedF0 = new float[shape.WindowFrames];
            _decoded48k = new float[shape.DecodedFrames * SamplesPerFrame48k];
            _previousTail = new float[shape.CrossfadeFrames * SamplesPerFrame48k];
        }

        public int BlockSamples => _shape.BlockFrames * SamplesPerFrame48k;

        /// <summary>True while a block is on its way.</summary>
        public bool IsBusy => _busy;

        /// <summary>True when the next block can start.</summary>
        public bool CanStart => !_busy;

        /// <summary>The last finished block's times.</summary>
        public RvcTimings Timings => _blockTimings;

        /// <summary>Semitones applied to the voice's pitch, from the next block.</summary>
        public float Semitones
        {
            get => _semitones;
            set => _semitones = value;
        }

        /// <summary>How much later a sample comes out than it went in, apart from the block itself and the work.</summary>
        public int AlgorithmDelaySamples => (_shape.CrossfadeFrames + _shape.SearchFrames + _shape.TailGuardFrames) * SamplesPerFrame48k + _reach48k;

        /// <summary>Converts one block and waits for it. For benchmarks and warming up; it stalls the thread.</summary>
        public RvcTimings Process(ReadOnlySpan<float> input, Span<float> output)
        {
            Start(input);
            while (!Advance(output, true))
            {
            }

            return Timings;
        }

        /// <summary>Takes a block and sends all of its work to the GPU; then call <see cref="Advance"/> each frame.</summary>
        public void Start(ReadOnlySpan<float> input)
        {
            if (input.Length != BlockSamples)
            {
                throw new ArgumentException($"A block is {BlockSamples} samples.");
            }

            if (_busy)
            {
                throw new InvalidOperationException("The last block is not finished.");
            }

            _timings = default;
            long start = Stopwatch.GetTimestamp();
            PushAudio(input);

            // The reflected padding touches the frames that reach past the end, so those are redone.
            RvcDsp.ShiftMel(_melData.AsSpan(), _shape.PitchFrames, _shape.BlockFrames);
            RvcDsp.LogMel(
                new ReadOnlySpan<float>(_window16k, _window16k.Length - _pitchSamples, _pitchSamples),
                _shape.PitchFrames,
                Math.Max(0, _shape.PitchFrames - _shape.BlockFrames - ReflectedFrames),
                _hann,
                _melBasis,
                _melData.AsSpan(),
                _real,
                _imaginary);

            // HuBERT gets the anchor, then the window (SpeechAnchor). The verdict on the new block uses the
            // last frames not leaning on reflected padding, 40 ms behind the block's end.
            int fresh = _shape.BlockFrames * SamplesPerFrame16k;
            float change = RvcDsp.MelChange(_melData.AsSpan(), _shape.PitchFrames, _shape.PitchFrames - 1 - _shape.BlockFrames - ReflectedFrames, _shape.BlockFrames);
            float rms = RvcDsp.Rms(new ReadOnlySpan<float>(_window16k, _window16k.Length - fresh, fresh));
            _anchor.Advance(_leaving16k, SpeechAnchor.IsSpeech(change, rms));
            Span<float> audio = _audioData.AsSpan();
            _anchor.Anchor.CopyTo(audio);
            _window16k.AsSpan().CopyTo(audio.Slice(SpeechAnchor.Samples));

            // The window moved on a block; the generator decodes the newest frames' pitch itself.
            int shift = _shape.BlockFrames;
            Array.Copy(_cachedF0, shift, _cachedF0, 0, _cachedF0.Length - shift);
            NativeArray<float>.Copy(_cachedF0, 0, _historyData, 0, _historyData.Length);
            _timings.Prepare = Since(ref start);

            _audioTensor.dataOnBackend.Upload(_audioData, _audioData.Length);
            _features.Schedule(_audioTensor);
            _timings.Features = Since(ref start);

            _melTensor.dataOnBackend.Upload(_melData, _melData.Length);
            _pitch.Schedule(_melTensor);
            _timings.Pitch = Since(ref start);

            _historyTensor.dataOnBackend.Upload(_historyData, _historyData.Length);
            Upload(_lastTensor, _lastF0);
            Upload(_factorTensor, MathF.Pow(2f, _semitones / 12f));
            CopyNoise();
            _noiseTensor.dataOnBackend.Upload(_noiseData, _noiseData.Length);
            _generator.SetInput("feats", _features.PeekOutput());
            _generator.SetInput("salience", _pitch.PeekOutput());
            _generator.SetInput("history", _historyTensor);
            _generator.SetInput("last", _lastTensor);
            _generator.SetInput("factor", _factorTensor);
            _generator.SetInput("rnd", _noiseTensor);
            _generator.SetInput("spk", _speakerTensor);
            _generator.Schedule();
            _audioOutput = _generator.PeekOutput("audio") as Tensor<float>;
            _f0Output = _generator.PeekOutput("f0new") as Tensor<float>;
            _audioOutput.ReadbackRequest();
            _f0Output.ReadbackRequest();
            _timings.Generator = Since(ref start);

            _waitStart = start;
            _audioRead = false;
            _pitchRead = false;
            _busy = true;
        }

        /// <summary>
        /// Takes back what the GPU has finished. True once <paramref name="output"/> holds the block.
        /// With <paramref name="wait"/> it stalls for the GPU; without it nothing here waits.
        /// </summary>
        public bool Advance(Span<float> output, bool wait)
        {
            if (!_busy)
            {
                return false;
            }

            // Each is read as soon as it is done: a finished request's data lasts only that frame.
            if (!_pitchRead && (wait || _f0Output.IsReadbackRequestDone()))
            {
                ReadPitch();
            }

            if (!_audioRead && (wait || _audioOutput.IsReadbackRequestDone()))
            {
                ReadAudio(output);
            }

            if (!_audioRead || !_pitchRead)
            {
                return false;
            }

            _blockTimings = _timings;
            _busy = false;
            return true;
        }

        /// <summary>Forgets a block on its way, such as when the conversion turns off.</summary>
        public void Abandon()
        {
            _busy = false;
            _hasTail = false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _features.Dispose();
            _pitch.Dispose();
            _generator.Dispose();
            _audioTensor.Dispose();
            _melTensor.Dispose();
            _historyTensor.Dispose();
            _lastTensor.Dispose();
            _factorTensor.Dispose();
            _noiseTensor.Dispose();
            _speakerTensor.Dispose();
            _audioData.Dispose();
            _melData.Dispose();
            _historyData.Dispose();
            _scalarData.Dispose();
            _noiseData.Dispose();
        }

        private static double Since(ref long start)
        {
            long now = Stopwatch.GetTimestamp();
            double milliseconds = (now - start) * 1000.0 / Stopwatch.Frequency;
            start = now;
            return milliseconds;
        }

        private static void FillGaussian(float[] values, System.Random random)
        {
            for (int i = 0; i < values.Length; i++)
            {
                double radius = Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble()));
                values[i] = (float)(radius * Math.Cos(2.0 * Math.PI * random.NextDouble()));
            }
        }

        private void Upload(Tensor<float> tensor, float value)
        {
            _scalarData[0] = value;
            tensor.dataOnBackend.Upload(_scalarData, 1);
        }

        private void PushAudio(ReadOnlySpan<float> input)
        {
            int block = input.Length;
            int kept = _history48k.Length - block;
            Array.Copy(_history48k, block, _history48k, 0, kept);
            input.CopyTo(new Span<float>(_history48k, kept, block));

            int fresh = _shape.BlockFrames * SamplesPerFrame16k;
            Array.Copy(_window16k, 0, _leaving16k, 0, fresh);
            Array.Copy(_window16k, fresh, _window16k, 0, _window16k.Length - fresh);

            // An integer ratio, so each block starts on the same phase of the 16 kHz grid.
            RvcDsp.Resample(_history48k, _reach48k, 3, 1, _down, new Span<float>(_window16k, _window16k.Length - fresh, fresh));
        }

        // The pitch the generator used for the newest frames becomes the history of the next block.
        private void ReadPitch()
        {
            long start = Stopwatch.GetTimestamp();
            _timings.PitchWait = (start - _waitStart) * 1000.0 / Stopwatch.Frequency;
            using (Tensor<float> f0 = _f0Output.ReadbackAndClone())
            {
                ReadOnlySpan<float> fresh = f0.AsReadOnlySpan();
                fresh.CopyTo(new Span<float>(_cachedF0, _cachedF0.Length - FreshPitchFrames, FreshPitchFrames));
                if (fresh[FreshPitchFrames - 1] > 0f)
                {
                    _lastF0 = fresh[FreshPitchFrames - 1];
                }
            }

            _timings.Pitch += Since(ref start);
            _pitchRead = true;
        }

        private void ReadAudio(Span<float> output)
        {
            long start = Stopwatch.GetTimestamp();
            _timings.GeneratorWait = (start - _waitStart) * 1000.0 / Stopwatch.Frequency;
            using (Tensor<float> audio = _audioOutput.ReadbackAndClone())
            {
                Finish(audio.AsReadOnlySpan(), output);
            }

            _timings.Finish = Since(ref start);
            _audioRead = true;
        }

        private void CopyNoise()
        {
            _noiseFrame = (_noiseFrame + _shape.BlockFrames) % NoiseLoopFrames;
            Span<float> data = _noiseData.AsSpan();
            for (int channel = 0; channel < LatentChannels; channel++)
            {
                int row = channel * NoiseLoopFrames;
                for (int t = 0; t < _shape.NoiseFrames; t++)
                {
                    data[channel * _shape.NoiseFrames + t] = _noiseLoop[row + (_noiseFrame + t) % NoiseLoopFrames];
                }
            }
        }

        private void Finish(ReadOnlySpan<float> audio40k, Span<float> output)
        {
            // The window ends now; the upsampler's context past its end comes from the tail guard.
            RvcDsp.Resample(audio40k, _reach40k, 5, 6, _up, _decoded48k.AsSpan(0, _decoded48k.Length - 3 * _reach40k));

            int crossfade = _previousTail.Length;
            int search = _shape.SearchFrames * SamplesPerFrame48k;
            int block = output.Length;
            ReadOnlySpan<float> decoded = new ReadOnlySpan<float>(_decoded48k, _shape.HeadGuardFrames * SamplesPerFrame48k, block + crossfade + search);
            int offset = _hasTail ? RvcDsp.BestOffset(_previousTail, decoded, search) : 0;
            ReadOnlySpan<float> aligned = decoded.Slice(offset);
            if (_hasTail)
            {
                float correlation = RvcDsp.Correlation(_previousTail, aligned.Slice(0, crossfade));
                RvcDsp.Crossfade(_previousTail, aligned.Slice(0, crossfade), correlation, output.Slice(0, crossfade));
                aligned.Slice(crossfade, block - crossfade).CopyTo(output.Slice(crossfade));
            }
            else
            {
                aligned.Slice(0, block).CopyTo(output);
            }

            aligned.Slice(block, crossfade).CopyTo(_previousTail);
            _hasTail = true;
        }
    }
}
