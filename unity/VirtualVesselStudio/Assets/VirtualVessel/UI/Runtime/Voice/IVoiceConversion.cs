using VirtualVessel.Core.State;

namespace VirtualVessel.UI.Voice
{
    public enum VoiceConversionState
    {
        Off,
        Loading,
        Running,

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

        VoiceConversionStats Stats { get; }

        void SetEnabled(bool enabled);
    }
}
