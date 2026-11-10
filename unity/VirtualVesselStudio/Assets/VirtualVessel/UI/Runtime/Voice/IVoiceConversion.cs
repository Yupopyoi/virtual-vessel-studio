using System.Collections.Generic;
using System.Threading.Tasks;
using VirtualVessel.Core.State;
using VirtualVessel.ProjectData.Records;

namespace VirtualVessel.UI.Voice
{
    public enum VoiceConversionState
    {
        Off,
        Loading,
        Running,

        /// <summary>Turned on, but the microphone gives nothing to convert, such as with no device chosen.</summary>
        NoMicrophone,

        /// <summary>An audio file takes the microphone's place, to judge a voice without speaking.</summary>
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

    /// <summary>A voice the conversion can be given, as a list shows it.</summary>
    public readonly struct VoiceConversionVoice
    {
        public VoiceConversionVoice(string id, string name, int medianPitchHz, bool usable)
        {
            Id = id;
            Name = name;
            MedianPitchHz = medianPitchHz;
            Usable = usable;
        }

        public string Id { get; }

        /// <summary>The user's name for it; null for the prototype's voice, which has none.</summary>
        public string Name { get; }

        /// <summary>For matching the user's pitch (Voice Lab detailed design 8.3.4); 0 when unknown.</summary>
        public int MedianPitchHz { get; }

        /// <summary>False for a voice this version cannot run; it is listed and kept, not deleted (Voice detailed design 6.1).</summary>
        public bool Usable { get; }
    }

    /// <summary>Real-time voice conversion of the microphone. A prototype, shown only to developers.</summary>
    public interface IVoiceConversion
    {
        IReadOnlyObservable<VoiceConversionState> State { get; }

        /// <summary>The voices to choose from; reads the disk, so not every frame (system design 6.31).</summary>
        CrudResult<IReadOnlyList<VoiceConversionVoice>> ListVoices();

        /// <summary>The voice converting or loading, a trial's included; null when none is chosen or there is none. Choosing saves it in the project.</summary>
        string VoiceId { get; }

        /// <summary>
        /// Chooses the voice. While converting, the new one loads behind the one in use, which keeps converting
        /// until the new one is ready (system design 9.8). If it cannot load, the one in use stays.
        /// </summary>
        void SelectVoice(string voiceId);

        /// <summary>
        /// Tries a voice by its folder, such as one just made and not yet registered, on the microphone: the conversion
        /// turns on with it. Nothing is saved, and pitch settings changed meanwhile are the trial's own.
        /// </summary>
        void BeginTrial(string folder);

        /// <summary>Ends a trial: the saved voice and pitch come back, and the conversion is off again if it was.</summary>
        void EndTrial();

        /// <summary>The voice chosen for the stream as saved, which a trial does not change.</summary>
        string InUseVoiceId { get; }

        /// <summary>True while a chosen voice loads behind the one converting.</summary>
        bool IsSwitching { get; }

        /// <summary>
        /// The user's adjustment in semitones, added to the automatic shift while <see cref="AutoPitch"/> is on, or the
        /// whole shift while it is off.
        /// </summary>
        float Semitones { get; set; }

        /// <summary>Matches the voice's pitch to the user's own (Voice Lab detailed design 8.3.4). On by default.</summary>
        bool AutoPitch { get; set; }

        /// <summary>The automatic shift; 0 until the user's pitch is measured or when the voice's is unknown.</summary>
        float AutoSemitones { get; }

        /// <summary>The user's median pitch in Hz, measured while converting; 0 until about 2 s of voice.</summary>
        float UserPitchHz { get; }

        /// <summary>The voice's median pitch in Hz; 0 when unknown, as for the prototype's voice.</summary>
        float VoicePitchHz { get; }

        /// <summary>Forgets the user's measured pitch and measures it again from the next voice.</summary>
        void RemeasurePitch();

        /// <summary>Whether this PC has a GPU that can convert; without one it cannot be turned on (setup screen 13.3).</summary>
        bool GpuAvailable { get; }

        /// <summary>How far the models have loaded, from 0 to 1.</summary>
        float LoadProgress { get; }

        /// <summary>The audio file playing, without its folder; null when none is.</summary>
        string FileName { get; }

        /// <summary>The audio file's place and length in seconds; both 0 when none is playing.</summary>
        (float Position, float Length) FileTime { get; }

        VoiceConversionStats Stats { get; }

        void SetEnabled(bool enabled);

        /// <summary>Turns on and converts this audio file in the microphone's place, once, at real time. False when it cannot be read.</summary>
        Task<bool> PlayFileAsync(string path);

        /// <summary>The audio files PlayFileAsync reads, by extension.</summary>
        IReadOnlyList<string> FileExtensions { get; }

        /// <summary>Stops an audio file; the microphone comes back.</summary>
        void StopFile();
    }
}
