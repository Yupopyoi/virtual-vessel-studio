using System;

namespace VirtualVessel.Voice.Rvc
{
    /// <summary>
    /// The second of the user's own speech that goes before each HuBERT window. HuBERT misreads a vowel when its
    /// whole input is that vowel held: once a held "i" filled the window, it came out nearer "u". With a second
    /// of something else before the window the vowel stayed right, and of what was tried (noise, another
    /// speaker), the user's own speech kept both held notes and speech closest to HuBERT with the whole
    /// recording as context (decision record, 2026-11-03).
    /// <para>
    /// The anchor is the latest unbroken second of speech that has left the window: taken from inside the
    /// window, the same audio twice made speech worse, and pieces joined across breaks did too. Fixed noise
    /// stands in until there is such a second. It is kept only in memory.
    /// </para>
    /// </summary>
    public sealed class SpeechAnchor
    {
        public const int Samples = RvcDsp.ModelRate;

        private const float NoiseLevel = 0.01f;

        // From RvcDsp.MelChange on the user's recordings: held notes 0.22 to 0.29, speech 0.35 to 0.67
        // (10th to 90th percentile of 100 ms blocks).
        private const float SpeechChange = 0.32f;
        private const float SpeechRms = 0.01f; // About -40 dBFS

        private readonly float[] _anchor = new float[Samples];
        private readonly float[] _run = new float[Samples];
        private readonly bool[] _inWindow;
        private int _oldest;
        private int _runSamples;

        /// <param name="windowBlocks">Blocks in the window, so a block's verdict is used when it leaves.</param>
        public SpeechAnchor(int windowBlocks)
        {
            _inWindow = new bool[windowBlocks];
            var random = new Random(0);
            for (int i = 0; i < Samples; i++)
            {
                double radius = Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble()));
                _anchor[i] = (float)(radius * Math.Cos(2.0 * Math.PI * random.NextDouble())) * NoiseLevel;
            }
        }

        public ReadOnlySpan<float> Anchor => _anchor;

        /// <summary>Whether a block is speech: its sound changes, unlike a held note, and it is not quiet. Pure.</summary>
        public static bool IsSpeech(float melChange, float rms)
        {
            return melChange > SpeechChange && rms > SpeechRms;
        }

        /// <summary>
        /// The window moved on a block: <paramref name="leaving"/> is the 16 kHz audio of the block that left
        /// it, and <paramref name="newestIsSpeech"/> the verdict on the block that came in.
        /// </summary>
        public void Advance(ReadOnlySpan<float> leaving, bool newestIsSpeech)
        {
            bool leftWasSpeech = _inWindow[_oldest];
            _inWindow[_oldest] = newestIsSpeech;
            _oldest = (_oldest + 1) % _inWindow.Length;
            if (!leftWasSpeech)
            {
                _runSamples = 0;
                return;
            }

            Array.Copy(_run, leaving.Length, _run, 0, Samples - leaving.Length);
            leaving.CopyTo(new Span<float>(_run, Samples - leaving.Length, leaving.Length));
            _runSamples = Math.Min(Samples, _runSamples + leaving.Length);
            if (_runSamples == Samples)
            {
                _run.CopyTo(_anchor, 0);
            }
        }
    }
}
