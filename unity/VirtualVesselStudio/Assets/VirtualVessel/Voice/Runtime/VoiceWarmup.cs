using System;
using System.Collections;
using System.Diagnostics;
using VirtualVessel.Voice.Rvc;

namespace VirtualVessel.Voice
{
    /// <summary>
    /// Gets a chosen voice's stream ready behind the one converting (Voice detailed design 8.2): its layers primed a
    /// few milliseconds each frame, then blocks of silence moved along without waiting, so no frame stalls the voice in
    /// use. Main thread. It owns the stream until <see cref="Take"/>.
    /// </summary>
    public sealed class VoiceWarmup : IDisposable
    {
        /// <summary>Blocks of silence a new stream runs before it converts: its first runs are slow.</summary>
        public const int Blocks = 3;

        // How long a frame may spend preparing the next voice's layers; a few ms keep the frame well inside a block.
        private const double PrimeBudgetMs = 4.0;

        private RvcStream _stream;
        private IEnumerator _priming;
        private int _warmed;
        private float[] _silence;
        private float[] _output;

        public bool IsActive => _stream != null;

        /// <summary>The voice getting ready; null when none is.</summary>
        public string VoiceId { get; private set; }

        public bool IsWarm => _stream != null && _priming == null && _warmed >= Blocks;

        /// <summary>
        /// Whether a warmed voice takes over now: only between blocks, so no block is lost. Pure.
        /// </summary>
        public static bool ShouldSwap(bool hasNext, int warmed, int warmupBlocks, bool currentBusy, bool nextBusy)
        {
            return hasNext && warmed >= warmupBlocks && !currentBusy && !nextBusy;
        }

        /// <summary>Starts getting a stream ready; one that was getting ready is let go.</summary>
        public void Begin(RvcStream stream, string voiceId)
        {
            Dispose();
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            VoiceId = voiceId;
            _priming = stream.Prime();
            if (_silence == null || _silence.Length != stream.BlockSamples)
            {
                _silence = new float[stream.BlockSamples];
                _output = new float[stream.BlockSamples];
            }
        }

        /// <summary>One frame's step.</summary>
        public void Step()
        {
            if (_stream == null)
            {
                return;
            }

            if (_priming != null)
            {
                Stopwatch budget = Stopwatch.StartNew();
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

            if (_stream.IsBusy)
            {
                if (_stream.Advance(_output, false))
                {
                    _warmed++;
                }
            }
            else if (_warmed < Blocks)
            {
                Array.Clear(_silence, 0, _silence.Length);
                _stream.Start(_silence);
            }
        }

        /// <summary>Whether the ready stream can take over from one that is <paramref name="currentBusy"/> or not.</summary>
        public bool CanTakeOver(bool currentBusy)
        {
            return _priming == null && ShouldSwap(_stream != null, _warmed, Blocks, currentBusy, _stream?.IsBusy ?? false);
        }

        /// <summary>Hands the ready stream over; the caller owns it from now on.</summary>
        public RvcStream Take()
        {
            RvcStream stream = _stream;
            _stream = null;
            Reset();
            return stream;
        }

        /// <summary>Lets go of a stream getting ready.</summary>
        public void Dispose()
        {
            _stream?.Dispose();
            _stream = null;
            Reset();
        }

        private void Reset()
        {
            _priming = null;
            _warmed = 0;
            VoiceId = null;
        }
    }
}
