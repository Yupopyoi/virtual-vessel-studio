using System.Threading.Tasks;
using VirtualVessel.Core.State;

namespace VirtualVessel.UI.Voice
{
    public enum VoiceConversionState
    {
        Off,
        Loading,
        Running,

        /// <summary>Turned on, but the microphone gives nothing to convert, such as with no device chosen.</summary>
        NoMicrophone,

        /// <summary>A WAV file takes the microphone's place, to judge a voice without speaking.</summary>
        PlayingFile,

        /// <summary>No models in the folder, or no microphone.</summary>
        Unavailable,
        Failed,
    }

    /// <summary>The numbers of the running conversion, as plain values for the developers' Card.</summary>
    public readonly struct VoiceConversionStats
    {
        public VoiceConversionStats(double blockMs, double workMs, double issueMs, long blocks, long dropped)
        {
            BlockMs = blockMs;
            WorkMs = workMs;
            IssueMs = issueMs;
            Blocks = blocks;
            Dropped = dropped;
        }

        public double BlockMs { get; }

        /// <summary>From a block going in to its audio coming out, the last block.</summary>
        public double WorkMs { get; }

        /// <summary>The part of it on the main thread.</summary>
        public double IssueMs { get; }

        public long Blocks { get; }

        /// <summary>Blocks of the microphone left out because the conversion fell behind.</summary>
        public long Dropped { get; }
    }

    /// <summary>Real-time voice conversion of the microphone. A prototype, shown only to developers.</summary>
    public interface IVoiceConversion
    {
        IReadOnlyObservable<VoiceConversionState> State { get; }

        float Semitones { get; set; }

        /// <summary>
        /// How much the models see, in 10 ms frames, one of <see cref="Windows"/>. Longer steadies held notes
        /// and costs more GPU time. A change reloads the models.
        /// </summary>
        int WindowFrames { get; set; }

        System.Collections.Generic.IReadOnlyList<int> Windows { get; }

        /// <summary>Frames decoded past the end and dropped, 3 or 1. Fewer waits less and may dip at joins.</summary>
        int TailGuardFrames { get; set; }

        /// <summary>Removes noise such as key presses before the voice is converted.</summary>
        bool Denoise { get; set; }

        /// <summary>How far the models have loaded, from 0 to 1.</summary>
        float LoadProgress { get; }

        /// <summary>The WAV file playing, without its folder; null when none is.</summary>
        string FileName { get; }

        /// <summary>The WAV file's place and length in seconds; both 0 when none is playing.</summary>
        (float Position, float Length) FileTime { get; }

        VoiceConversionStats Stats { get; }

        void SetEnabled(bool enabled);

        /// <summary>Turns on and converts this WAV file in the microphone's place, once, at real time. False when it cannot be read.</summary>
        Task<bool> PlayFileAsync(string path);

        /// <summary>Stops a WAV file; the microphone comes back.</summary>
        void StopFile();
    }
}
