using System.Collections.Generic;
using VirtualVessel.ProjectData.Records;
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

    /// <summary>The timbre steps (Voice Lab detailed design 8.3.2), -6 to +6; softness 0 to +6.</summary>
    public readonly struct VoiceLabTimbre
    {
        public VoiceLabTimbre(float pitch, float age, float brightness, float breathiness, float softness = 0f)
        {
            Pitch = pitch;
            Age = age;
            Brightness = brightness;
            Breathiness = breathiness;
            Softness = softness;
        }

        public float Pitch { get; }

        public float Age { get; }

        public float Brightness { get; }

        public float Breathiness { get; }

        public float Softness { get; }
    }

    /// <summary>A candidate voice: its sample to play and its pitch to compare.</summary>
    public sealed class VoiceLabCandidate
    {
        public VoiceLabCandidate(string id, string file, int medianHz, int lowHz, int highHz, string instruction = null)
        {
            Id = id;
            File = file;
            MedianHz = medianHz;
            LowHz = lowHz;
            HighHz = highHz;
            Instruction = instruction ?? string.Empty;
        }

        /// <summary>The instruction VoxCPM2 was given, in English, for the user who wants to read it.</summary>
        public string Instruction { get; }

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

    /// <summary>How far making candidates is: <see cref="Done"/> of <see cref="Total"/>; a total of 0 while the voice model loads.</summary>
    public readonly struct VoiceLabMaking
    {
        public VoiceLabMaking(int done, int total)
        {
            Done = done;
            Total = total;
        }

        public int Done { get; }

        public int Total { get; }

        public bool Loading => Total <= 0;
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

        /// <summary>STEP 1's words and examples; null when Voice Lab is not set up.</summary>
        VoiceLabShelf Shelf { get; }

        /// <summary>
        /// A sample's file (<see cref="VoiceLabShelf.WordSampleName"/>, <see cref="VoiceLabShelf.ExampleSampleName"/>)
        /// in a language, or null when it has not been made.
        /// </summary>
        string SampleFile(string language, string name);

        /// <summary>
        /// Makes STEP 1's samples on this PC; files already made are kept. Activation will run it; until then
        /// the screen offers it (Voice Lab detailed design 8.3.1).
        /// </summary>
        /// <exception cref="VoiceLabFailedException">They could not be made.</exception>
        Task MakeSamplesAsync(CancellationToken cancellationToken);

        /// <summary>Where the running <see cref="MakeSamplesAsync"/> is. Read it as often as needed.</summary>
        VoiceLabMaking SamplesProgress { get; }

        /// <summary>
        /// Several candidates for the request; each call makes new ones. With <paramref name="near"/>, they are made
        /// near that candidate, to look around a voice the user picked (Voice Lab detailed design 8.3.1).
        /// </summary>
        /// <exception cref="VoiceLabFailedException">The voices could not be made.</exception>
        Task<IReadOnlyList<VoiceLabCandidate>> MakeCandidatesAsync(VoiceLabRequest request, int count, VoiceLabCandidate near, CancellationToken cancellationToken);

        /// <summary>Where the running <see cref="MakeCandidatesAsync"/> is. Read it as often as needed.</summary>
        VoiceLabMaking CandidatesProgress { get; }

        /// <exception cref="VoiceLabFailedException">The preview could not be made.</exception>
        Task<VoiceLabPreview> PreviewAsync(VoiceLabCandidate candidate, VoiceLabTimbre timbre, CancellationToken cancellationToken);

        /// <summary>Builds the voice; a failure or cancelling comes back as the build's state.</summary>
        Task<VoiceLabBuild> BuildAsync(VoiceLabCandidate candidate, VoiceLabTimbre timbre, CancellationToken cancellationToken);

        /// <summary>Where the running build is; <see cref="VoiceLabBuildState.None"/> when none runs. Read it as often as needed.</summary>
        VoiceLabBuildProgress BuildProgress { get; }

        /// <summary>
        /// A run's words, base, and timbre, to make it again (Voice Lab detailed design 8.6). The base becomes a
        /// candidate of this session, so it can be previewed and built.
        /// </summary>
        /// <remarks>A CRUD read (system design 6.31): NotFound when there is no such run, Invalid when Voice Lab is not set up.</remarks>
        CrudResult<VoiceLabRemake> Remake(string runId);

        /// <summary>
        /// Registers a completed build under a name, for voice conversion to use. A CRUD create (system design
        /// 6.31): NotFound without the run, Invalid for a run that did not complete, StorageFailed when copying failed.
        /// </summary>
        CrudResult Register(string runId, string name);
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
