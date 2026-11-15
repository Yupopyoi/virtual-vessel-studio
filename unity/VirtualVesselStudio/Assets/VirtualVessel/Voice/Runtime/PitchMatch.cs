using System;

namespace VirtualVessel.Voice
{
    /// <summary>
    /// Matching the conversion's pitch to the user (Voice Lab detailed design 8.3.4; Voice detailed design 8.4): the
    /// shift that brings the user's median pitch to the voice's. A voice at the wrong pitch sounds like another voice
    /// even when its timbre is right (decision record, 2026-11-08).
    /// </summary>
    public static class PitchMatch
    {
        /// <summary>The range of the shift, as the pitch setting allows (Voice detailed design 6.2).</summary>
        public const float MaxSemitones = 24f;

        /// <summary>
        /// Semitones from the user's median to the voice's, to the nearest whole one; 0 when either is unknown. Pure.
        /// </summary>
        public static float Semitones(float voiceHz, float userHz)
        {
            if (voiceHz <= 0f || userHz <= 0f)
            {
                return 0f;
            }

            return Clamp((float)Math.Round(12.0 * Math.Log(voiceHz / userHz, 2.0)));
        }

        /// <summary>The shift applied: the automatic one when on, plus the user's adjustment, within range. Pure.</summary>
        public static float Applied(bool auto, float automatic, float adjustment)
        {
            return Clamp((auto ? automatic : 0f) + adjustment);
        }

        private static float Clamp(float semitones)
        {
            return Math.Max(-MaxSemitones, Math.Min(MaxSemitones, semitones));
        }
    }

    /// <summary>
    /// Collects the user's voiced pitch, block by block, for its median. It starts giving a value after
    /// <see cref="MinFrames"/> and stops taking frames at <see cref="LockFrames"/>, so singing high later does not move
    /// the shift. State only; the median is a pure computation over what it holds.
    /// </summary>
    public sealed class PitchMeter
    {
        /// <summary>2 s of voiced 10 ms frames before a median is trusted.</summary>
        public const int MinFrames = 200;

        /// <summary>10 s of voiced frames, after which the median is kept.</summary>
        public const int LockFrames = 1000;

        // Real speech stays well inside this; RMVPE's edges are not voices.
        private const float LowestHz = 50f;
        private const float HighestHz = 1100f;

        private readonly float[] _frames = new float[LockFrames];
        private readonly float[] _scratch = new float[LockFrames];
        private int _count;
        private float _previous;

        /// <summary>The user's median pitch in Hz; 0 until <see cref="MinFrames"/> voiced frames are in.</summary>
        public float MedianHz { get; private set; }

        public bool IsLocked => _count >= LockFrames;

        /// <summary>
        /// Takes a block's frames; unvoiced and implausible ones are left out. True when the median moved. The
        /// generator holds the last voiced pitch through unvoiced frames, so a frame exactly equal to the one before
        /// it is a held one: a voiced frame is a weighted mean of RMVPE's bins and does not repeat exactly. Counted,
        /// the held frames pulled the median down to the falling ends of phrases (149 Hz against upstream RMVPE's 200).
        /// </summary>
        public bool Add(ReadOnlySpan<float> frames)
        {
            if (IsLocked)
            {
                return false;
            }

            for (int i = 0; i < frames.Length && _count < LockFrames; i++)
            {
                float hz = frames[i];
                bool held = hz == _previous;
                _previous = hz;
                if (!held && hz >= LowestHz && hz <= HighestHz)
                {
                    _frames[_count++] = hz;
                }
            }

            if (_count < MinFrames)
            {
                return false;
            }

            float median = Median(new ReadOnlySpan<float>(_frames, 0, _count), _scratch);
            bool moved = median != MedianHz;
            MedianHz = median;
            return moved;
        }

        /// <summary>A median measured before, kept as if measured now, so the pitch matches from the first block.</summary>
        public void Restore(float medianHz)
        {
            if (medianHz <= 0f)
            {
                return;
            }

            _count = LockFrames;
            MedianHz = medianHz;
        }

        /// <summary>Forgets what was measured, to measure again.</summary>
        public void Reset()
        {
            _count = 0;
            _previous = 0f;
            MedianHz = 0f;
        }

        /// <summary>
        /// The median of <paramref name="values"/>. Pure but for sorting a copy in <paramref name="scratch"/>, a buffer
        /// the caller reuses so that a block's work makes no garbage.
        /// </summary>
        public static float Median(ReadOnlySpan<float> values, float[] scratch)
        {
            if (values.Length == 0)
            {
                return 0f;
            }

            values.CopyTo(scratch);
            Array.Sort(scratch, 0, values.Length);
            int middle = values.Length / 2;
            return values.Length % 2 == 1 ? scratch[middle] : (scratch[middle - 1] + scratch[middle]) * 0.5f;
        }
    }
}
