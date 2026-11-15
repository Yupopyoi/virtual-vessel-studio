using System;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualVessel.UI.Voice
{
    /// <summary>What to convert in a trial, and with which voice and pitch.</summary>
    public sealed class VoiceTrialRequest
    {
        public VoiceTrialRequest(string voiceFolder, string recording, bool autoPitch, float adjustment, float knownUserHz)
        {
            VoiceFolder = voiceFolder;
            Recording = recording;
            AutoPitch = autoPitch;
            Adjustment = adjustment;
            KnownUserHz = knownUserHz;
        }

        /// <summary>A folder with voice.sentis, voice.spk, and voice.json: a voice just made, or a registered one.</summary>
        public string VoiceFolder { get; }

        /// <summary>The user's recording: WAV, M4A, or MP3.</summary>
        public string Recording { get; }

        public bool AutoPitch { get; }

        /// <summary>Semitones added to the automatic shift, or the whole shift with <see cref="AutoPitch"/> off.</summary>
        public float Adjustment { get; }

        /// <summary>The user's median pitch when already measured; 0 measures it from the recording first.</summary>
        public float KnownUserHz { get; }
    }

    /// <summary>A converted recording, ready to play to the user only.</summary>
    public sealed class VoiceTrialResult
    {
        public VoiceTrialResult(string converted, float userHz, float voiceHz, float semitones)
        {
            Converted = converted;
            UserHz = userHz;
            VoiceHz = voiceHz;
            Semitones = semitones;
        }

        /// <summary>A WAV file of the converted recording.</summary>
        public string Converted { get; }

        /// <summary>The user's median pitch it matched; 0 when unknown.</summary>
        public float UserHz { get; }

        /// <summary>The voice's median pitch; 0 when unknown.</summary>
        public float VoiceHz { get; }

        /// <summary>The shift it converted with.</summary>
        public float Semitones { get; }
    }

    /// <summary>Why a trial could not convert.</summary>
    public sealed class VoiceTrialFailedException : Exception
    {
        public VoiceTrialFailedException(string message, Exception inner = null)
            : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Converts a recording with a voice the way the stream would, a block at a time with the same windows, joins, and
    /// speech anchor, into a file to play to the user only (Voice Lab detailed design 8.3.4; Voice detailed design 8.5).
    /// The microphone, its monitoring, and the voice converting for the stream are untouched.
    /// </summary>
    public interface IVoiceTrial
    {
        /// <summary>
        /// Converts in the background without stalling frames. Cancelling throws OperationCanceledException; a voice
        /// or recording that cannot be used throws <see cref="VoiceTrialFailedException"/>.
        /// </summary>
        Task<VoiceTrialResult> ConvertAsync(VoiceTrialRequest request, IProgress<float> progress, CancellationToken cancellationToken);
    }
}
