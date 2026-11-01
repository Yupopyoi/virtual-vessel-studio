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
    /// only the window's tail through the flow and decoder, then an in-phase crossfade). The prototype
    /// runs each model to completion on the calling thread.
    /// </summary>
    public sealed class RvcStream : IDisposable
    {
        public const int DeviceRate = 48000;

        private const int GeneratorRate = 40000;
        private const int SamplesPerFrame16k = RvcDsp.HopSamples;
        private const int SamplesPerFrame48k = DeviceRate / 100;
        private const int SamplesPerFrame40k = GeneratorRate / 100;
        private const int PhoneChannels = 768;
        private const int LatentChannels = 192;
        private const int SpeakerChannels = 256;
        private const float VoicedThreshold = 0.03f;
        private const int NoiseLoopFrames = 1024;

        // 512 samples of reflected padding reach back this many hops from the window's end.
        private const int ReflectedFrames = 4;

        // Upstream drops RMVPE's first frames and its last one, which lean on padded audio.
        private const int PitchHeadSkipped = 3;
        private const int PitchTailSkipped = 1;

        private enum Step
        {
            Idle,
            Models,
            Generator,
        }

        private readonly RvcStreamShape _shape;
        private readonly Worker _features;
        private readonly Worker _pitch;
        private readonly Worker _generator;

        private readonly Tensor<float> _audioTensor;
        private readonly Tensor<float> _melTensor;
        private readonly Tensor<float> _phoneTensor;
        private readonly Tensor<int> _coarseTensor;
        private readonly Tensor<float> _pitchfTensor;
        private readonly Tensor<float> _noiseTensor;
        private readonly Tensor<float> _speakerTensor;
        private NativeArray<float> _audioData;
        private NativeArray<float> _melData;
        private NativeArray<float> _phoneData;
        private NativeArray<int> _coarseData;
        private NativeArray<float> _pitchfData;
        private NativeArray<float> _noiseData;

        private readonly float[] _noiseLoop;
        private readonly float[] _history48k;
        private readonly float[] _window16k;
        private readonly float[] _hann = RvcDsp.HannWindow(RvcDsp.FftSize);
        private readonly float[] _melBasis = RvcDsp.MelBasis();
        private readonly float[] _down = RvcDsp.ResampleKernels(3, 1);
        private readonly float[] _up = RvcDsp.ResampleKernels(5, 6);
        private readonly float[] _real = new float[RvcDsp.FftSize];
        private readonly float[] _imaginary = new float[RvcDsp.FftSize];
        private readonly float[] _frameF0;
        private readonly float[] _cachedF0;
        private readonly float[] _decoded48k;
        private readonly float[] _previousTail;
        private readonly int _pitchSamples;
        private readonly int _reach48k;
        private readonly int _reach40k;

        private Step _step;
        private long _waitStart;
        private bool _featuresRead;
        private bool _pitchRead;
        private RvcTimings _timings;
        private Tensor<float> _featuresOutput;
        private Tensor<float> _pitchOutput;
        private Tensor<float> _generatorOutput;
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
            int phoneValues = shape.WindowFrames * PhoneChannels;
            _pitchSamples = (shape.PitchFrames - 1) * SamplesPerFrame16k;
            _reach48k = RvcDsp.ResampleReach(3, 1);
            _reach40k = RvcDsp.ResampleReach(5, 6);

            _audioData = new NativeArray<float>(windowSamples, Allocator.Persistent);
            _melData = new NativeArray<float>(RvcDsp.MelBins * shape.PitchFrames, Allocator.Persistent);
            _phoneData = new NativeArray<float>(phoneValues, Allocator.Persistent);
            _coarseData = new NativeArray<int>(shape.WindowFrames, Allocator.Persistent);
            _pitchfData = new NativeArray<float>(shape.WindowFrames, Allocator.Persistent);
            _audioTensor = new Tensor<float>(new TensorShape(1, windowSamples));
            _melTensor = new Tensor<float>(new TensorShape(1, RvcDsp.MelBins, shape.PitchFrames));
            _phoneTensor = new Tensor<float>(new TensorShape(1, shape.WindowFrames, PhoneChannels));
            _coarseTensor = new Tensor<int>(new TensorShape(1, shape.WindowFrames));
            _pitchfTensor = new Tensor<float>(new TensorShape(1, shape.WindowFrames));

            // Noise tied to time, sliding with the window, so the two windows meeting at a crossfade
            // give the same moment the same noise. A loop is enough: nothing can hear it repeat.
            _noiseLoop = new float[LatentChannels * NoiseLoopFrames];
            FillGaussian(_noiseLoop, new System.Random(1234));
            _noiseData = new NativeArray<float>(LatentChannels * shape.NoiseFrames, Allocator.Persistent);
            _speakerTensor = new Tensor<float>(new TensorShape(1, SpeakerChannels, 1), speaker);
            _noiseTensor = new Tensor<float>(new TensorShape(1, LatentChannels, shape.NoiseFrames));

            _history48k = new float[2 * _reach48k + shape.BlockFrames * SamplesPerFrame48k];
            _window16k = new float[windowSamples];
            _frameF0 = new float[shape.PitchFrames];
            _cachedF0 = new float[shape.WindowFrames];
            _decoded48k = new float[shape.DecodedFrames * SamplesPerFrame48k];
            _previousTail = new float[shape.CrossfadeFrames * SamplesPerFrame48k];
        }

        public int BlockSamples => _shape.BlockFrames * SamplesPerFrame48k;

        /// <summary>True from <see cref="Start"/> until <see cref="Advance"/> gives the block back.</summary>
        public bool IsBusy => _step != Step.Idle;

        /// <summary>The last block's times.</summary>
        public RvcTimings Timings
        {
            get => _timings;
            private set => _timings = value;
        }

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

        /// <summary>Takes a block and sends it to the GPU; then call <see cref="Advance"/> each frame.</summary>
        public void Start(ReadOnlySpan<float> input)
        {
            if (input.Length != BlockSamples)
            {
                throw new ArgumentException($"A block is {BlockSamples} samples.");
            }

            if (IsBusy)
            {
                throw new InvalidOperationException("The last block is not finished.");
            }

            Timings = default;
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
            _audioData.CopyFrom(_window16k);
            _timings.Prepare = Since(ref start);

            _audioTensor.dataOnBackend.Upload(_audioData, _audioData.Length);
            _features.Schedule(_audioTensor);
            _featuresOutput = _features.PeekOutput() as Tensor<float>;
            _featuresOutput.ReadbackRequest();
            _timings.Features = Since(ref start);

            _melTensor.dataOnBackend.Upload(_melData, _melData.Length);
            _pitch.Schedule(_melTensor);
            _pitchOutput = _pitch.PeekOutput() as Tensor<float>;
            _pitchOutput.ReadbackRequest();
            _timings.Pitch = Since(ref start);

            _waitStart = start;
            _featuresRead = false;
            _pitchRead = false;
            _step = Step.Models;
        }

        /// <summary>
        /// Moves the block along as far as the GPU has got. True once <paramref name="output"/> holds it.
        /// With <paramref name="wait"/> it stalls for the GPU; without it nothing here waits.
        /// </summary>
        public bool Advance(Span<float> output, bool wait)
        {
            if (_step == Step.Models)
            {
                // Each is read as soon as it is done: a finished request's data lasts only that frame,
                // so waiting for the slower model loses the faster one's.
                if (!_featuresRead && (wait || _featuresOutput.IsReadbackRequestDone()))
                {
                    ReadFeatures();
                }

                if (!_pitchRead && (wait || _pitchOutput.IsReadbackRequestDone()))
                {
                    ReadPitch();
                }

                if (_featuresRead && _pitchRead)
                {
                    RunGenerator();
                }

                return false;
            }

            if (_step == Step.Generator)
            {
                if (!wait && !_generatorOutput.IsReadbackRequestDone())
                {
                    return false;
                }

                FinishGenerator(output);
                return true;
            }

            throw new InvalidOperationException("No block is on its way.");
        }

        /// <summary>Forgets a block on its way, such as when the conversion turns off.</summary>
        public void Abandon()
        {
            _step = Step.Idle;
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
            _phoneTensor.Dispose();
            _coarseTensor.Dispose();
            _pitchfTensor.Dispose();
            _noiseTensor.Dispose();
            _speakerTensor.Dispose();
            _audioData.Dispose();
            _melData.Dispose();
            _phoneData.Dispose();
            _coarseData.Dispose();
            _pitchfData.Dispose();
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

        private void PushAudio(ReadOnlySpan<float> input)
        {
            int block = input.Length;
            int kept = _history48k.Length - block;
            Array.Copy(_history48k, block, _history48k, 0, kept);
            input.CopyTo(new Span<float>(_history48k, kept, block));

            int fresh = _shape.BlockFrames * SamplesPerFrame16k;
            Array.Copy(_window16k, fresh, _window16k, 0, _window16k.Length - fresh);

            // An integer ratio, so each block starts on the same phase of the 16 kHz grid.
            RvcDsp.Resample(_history48k, _reach48k, 3, 1, _down, new Span<float>(_window16k, _window16k.Length - fresh, fresh));
        }

        private void ReadFeatures()
        {
            long start = Stopwatch.GetTimestamp();
            _timings.FeaturesWait = (start - _waitStart) * 1000.0 / Stopwatch.Frequency;
            using (Tensor<float> features = _featuresOutput.ReadbackAndClone())
            {
                RvcDsp.DoubleFrames(features.AsReadOnlySpan(), features.shape[1], PhoneChannels, _phoneData.AsSpan(), _shape.WindowFrames);
            }

            _timings.Features += Since(ref start);
            _featuresRead = true;
        }

        private void ReadPitch()
        {
            long start = Stopwatch.GetTimestamp();
            _timings.PitchWait = (start - _waitStart) * 1000.0 / Stopwatch.Frequency;
            using (Tensor<float> salience = _pitchOutput.ReadbackAndClone())
            {
                ApplyPitch(salience.AsReadOnlySpan());
            }

            _timings.Pitch += Since(ref start);
            _pitchRead = true;
        }

        private void RunGenerator()
        {
            long start = Stopwatch.GetTimestamp();
            _phoneTensor.dataOnBackend.Upload(_phoneData, _phoneData.Length);
            _coarseTensor.dataOnBackend.Upload(_coarseData, _coarseData.Length);
            _pitchfTensor.dataOnBackend.Upload(_pitchfData, _pitchfData.Length);
            CopyNoise();
            _noiseTensor.dataOnBackend.Upload(_noiseData, _noiseData.Length);
            _generator.SetInput("phone", _phoneTensor);
            _generator.SetInput("pitch", _coarseTensor);
            _generator.SetInput("pitchf", _pitchfTensor);
            _generator.SetInput("rnd", _noiseTensor);
            _generator.SetInput("spk", _speakerTensor);
            _generator.Schedule();
            _generatorOutput = _generator.PeekOutput() as Tensor<float>;
            _generatorOutput.ReadbackRequest();
            _timings.Generator = Since(ref start);
            _waitStart = start;
            _step = Step.Generator;
        }

        private void FinishGenerator(Span<float> output)
        {
            long start = Stopwatch.GetTimestamp();
            _timings.GeneratorWait = (start - _waitStart) * 1000.0 / Stopwatch.Frequency;
            using (Tensor<float> audio = _generatorOutput.ReadbackAndClone())
            {
                Finish(audio.AsReadOnlySpan(), output);
            }

            _timings.Finish = Since(ref start);
            _step = Step.Idle;
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

        private void ApplyPitch(ReadOnlySpan<float> salience)
        {
            RvcDsp.DecodeF0(salience, _shape.PitchFrames, VoicedThreshold, _frameF0);
            RvcDsp.FillUnvoiced(_frameF0, _lastF0);

            int shift = _shape.BlockFrames;
            Array.Copy(_cachedF0, shift, _cachedF0, 0, _cachedF0.Length - shift);
            int fresh = _shape.PitchFrames - PitchHeadSkipped - PitchTailSkipped;
            Array.Copy(_frameF0, PitchHeadSkipped, _cachedF0, _cachedF0.Length - fresh, fresh);
            if (_frameF0[_shape.PitchFrames - 1] > 0f)
            {
                _lastF0 = _frameF0[_shape.PitchFrames - 1];
            }

            for (int i = 0; i < _cachedF0.Length; i++)
            {
                float shifted = RvcDsp.Transpose(_cachedF0[i], _semitones);
                _pitchfData[i] = shifted;
                _coarseData[i] = RvcDsp.CoarsePitch(shifted);
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
