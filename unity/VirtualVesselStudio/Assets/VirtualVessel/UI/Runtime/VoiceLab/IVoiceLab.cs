using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>Whether Voice Lab can make voices on this PC.</summary>
    public enum VoiceLabAvailability
    {
        Ready,

        /// <summary>No training environment yet: activation (Voice Lab detailed design 8.1) has not run.</summary>
        NotSetUp,
    }

    /// <summary>Where building a voice is, in the states the screen shows (system design 13.15, 13.16).</summary>
    public enum VoiceLabBuildState
    {
        None,
        Preparing,
        Preprocessing,
        Training,
        PostProcessing,
        Completed,
        Failed,
        Cancelled,
    }

    /// <summary>The mood a voice is asked for (Voice Lab detailed design 8.3.1): sliders from 0 to 1, and words.</summary>
    public sealed class VoiceLabMood
    {
        public VoiceLabMood(IReadOnlyDictionary<string, float> sliders, string words)
        {
            Sliders = sliders ?? new Dictionary<string, float>();
            Words = words ?? string.Empty;
        }

        public IReadOnlyDictionary<string, float> Sliders { get; }

        public string Words { get; }
    }

    /// <summary>The timbre steps (Voice Lab detailed design 8.3.2), -6 to +6.</summary>
    public readonly struct VoiceLabTimbre
    {
        public VoiceLabTimbre(float pitch, float age, float brightness, float breathiness)
        {
            Pitch = pitch;
            Age = age;
            Brightness = brightness;
            Breathiness = breathiness;
        }

        public float Pitch { get; }

        public float Age { get; }

        public float Brightness { get; }

        public float Breathiness { get; }
    }

    /// <summary>A candidate voice: its sample to play and its pitch to compare.</summary>
    public sealed class VoiceLabCandidate
    {
        public VoiceLabCandidate(string id, string file, int medianHz, int lowHz, int highHz)
        {
            Id = id;
            File = file;
            MedianHz = medianHz;
            LowHz = lowHz;
            HighHz = highHz;
        }

        public string Id { get; }

        /// <summary>For playing only; never shown.</summary>
        public string File { get; }

        public int MedianHz { get; }

        public int LowHz { get; }

        public int HighHz { get; }
    }

    /// <summary>A timbre preview's sample, to play.</summary>
    public sealed class VoiceLabPreview
    {
        public VoiceLabPreview(string file, int medianHz)
        {
            File = file;
            MedianHz = medianHz;
        }

        public string File { get; }

        public int MedianHz { get; }
    }

    /// <summary>A build as the screen follows it.</summary>
    public readonly struct VoiceLabBuildProgress
    {
        public VoiceLabBuildProgress(VoiceLabBuildState state, float fraction)
        {
            State = state;
            Fraction = fraction;
        }

        public VoiceLabBuildState State { get; }

        /// <summary>The whole build, 0 to 1.</summary>
        public float Fraction { get; }
    }

    /// <summary>A finished build.</summary>
    public sealed class VoiceLabBuild
    {
        public VoiceLabBuild(string runId, VoiceLabBuildState state, bool outOfMemory)
        {
            RunId = runId;
            State = state;
            OutOfMemory = outOfMemory;
        }

        public string RunId { get; }

        public VoiceLabBuildState State { get; }

        /// <summary>It failed for GPU memory: the screen suggests closing other applications.</summary>
        public bool OutOfMemory { get; }
    }

    /// <summary>
    /// Voice Lab for its screen (Voice Lab detailed design 5, system design 14.13): designing a voice from
    /// candidates, shaping its timbre, building it, and registering it. Every method may take minutes; none
    /// blocks the main thread.
    /// </summary>
    public interface IVoiceLab
    {
        VoiceLabAvailability Availability { get; }

        /// <summary>
        /// Several candidates in the mood; each call makes new ones. With <paramref name="near"/>, they are made
        /// near that candidate, to look around a voice the user picked (Voice Lab detailed design 8.3.1).
        /// </summary>
        /// <exception cref="VoiceLabFailedException">The voices could not be made.</exception>
        Task<IReadOnlyList<VoiceLabCandidate>> MakeCandidatesAsync(VoiceLabMood mood, int count, VoiceLabCandidate near, CancellationToken cancellationToken);

        /// <exception cref="VoiceLabFailedException">The preview could not be made.</exception>
        Task<VoiceLabPreview> PreviewAsync(VoiceLabCandidate candidate, VoiceLabTimbre timbre, CancellationToken cancellationToken);

        /// <summary>Builds the voice; a failure or cancelling comes back as the build's state.</summary>
        Task<VoiceLabBuild> BuildAsync(VoiceLabCandidate candidate, VoiceLabTimbre timbre, CancellationToken cancellationToken);

        /// <summary>Where the running build is; <see cref="VoiceLabBuildState.None"/> when none runs. Read it as often as needed.</summary>
        VoiceLabBuildProgress BuildProgress { get; }

        /// <summary>Registers a completed build under a name, for voice conversion to use.</summary>
        /// <exception cref="VoiceLabFailedException">It could not be registered.</exception>
        void Register(string runId, string name);
    }

    /// <summary>Voice Lab could not do what was asked; <see cref="System.Exception.Message"/> is for developers.</summary>
    public sealed class VoiceLabFailedException : System.Exception
    {
        public VoiceLabFailedException(string message, System.Exception innerException = null)
            : base(message, innerException)
        {
        }
    }
}
