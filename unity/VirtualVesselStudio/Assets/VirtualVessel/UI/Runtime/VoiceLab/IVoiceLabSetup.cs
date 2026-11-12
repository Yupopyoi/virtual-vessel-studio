using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.ProjectData.Records;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>Where preparing Voice Lab is (Voice Lab detailed design 8.1).</summary>
    public enum VoiceLabSetupState
    {
        /// <summary>Nothing built and no build begun.</summary>
        NotStarted,

        /// <summary>A build runs now.</summary>
        Preparing,

        /// <summary>A build was stopped; starting again continues it.</summary>
        Paused,

        /// <summary>A build failed; starting again continues it.</summary>
        Failed,

        /// <summary>An environment is built and in use.</summary>
        Ready,
    }

    /// <summary>Why a build failed, for the screen to say what to do.</summary>
    public enum VoiceLabSetupFailure
    {
        None,
        Network,
        Corrupt,
        DiskFull,
        Setup,
        Check,
    }

    /// <summary>The build's steps as the screen shows them.</summary>
    public enum VoiceLabSetupStep
    {
        Download,
        Unpack,
        Install,
        Check,
    }

    /// <summary>Where a build is: its step, and bytes downloaded of the total while downloading.</summary>
    public readonly struct VoiceLabSetupProgress
    {
        public VoiceLabSetupProgress(VoiceLabSetupStep step, long done, long total)
        {
            Step = step;
            Done = done;
            Total = total;
        }

        public VoiceLabSetupStep Step { get; }

        public long Done { get; }

        public long Total { get; }

        /// <summary>Of the whole build, 0 to 1: downloading is most of the time, the rest share what is left. Pure.</summary>
        public float Fraction
        {
            get
            {
                float download = Total > 0 && Step == VoiceLabSetupStep.Download ? (float)Done / Total : 1f;
                switch (Step)
                {
                    case VoiceLabSetupStep.Download:
                        return 0.6f * download;
                    case VoiceLabSetupStep.Unpack:
                        return 0.65f;
                    case VoiceLabSetupStep.Install:
                        return 0.75f;
                    default:
                        return 0.9f;
                }
            }
        }
    }

    /// <summary>What an entry of the build's record says happened.</summary>
    public enum VoiceLabSetupEntryKind
    {
        /// <summary>A component began downloading (or was found already downloaded).</summary>
        Download,
        Unpack,

        /// <summary>The runtime the scripts run in is installed.</summary>
        Runtime,

        /// <summary>The packages are installed; the longest part after downloading.</summary>
        Packages,
        Check,
        Finish,
        Done,
        Stopped,
        Failed,
    }

    /// <summary>One line of the build's record, in the app's words (screen spec voice-lab 2.1).</summary>
    public readonly struct VoiceLabSetupEntry
    {
        public VoiceLabSetupEntry(DateTime at, VoiceLabSetupEntryKind kind, string item = null, long size = 0L)
        {
            At = at;
            Kind = kind;
            Item = item;
            Size = size;
        }

        /// <summary>Local time.</summary>
        public DateTime At { get; }

        public VoiceLabSetupEntryKind Kind { get; }

        /// <summary>The component's id for a download; null otherwise.</summary>
        public string Item { get; }

        public long Size { get; }
    }

    /// <summary>What this PC has, against what making voices needs (Voice Lab detailed design 8.1).</summary>
    public sealed class VoiceLabPcCheck
    {
        public VoiceLabPcCheck(string gpu, bool gpuOk, string driver, bool driverOk, string location, long freeBytes, bool spaceOk, bool online)
        {
            Gpu = gpu;
            GpuOk = gpuOk;
            Driver = driver;
            DriverOk = driverOk;
            Location = location;
            FreeBytes = freeBytes;
            SpaceOk = spaceOk;
            Online = online;
        }

        /// <summary>The GPU's name; null when no NVIDIA GPU was found.</summary>
        public string Gpu { get; }

        public bool GpuOk { get; }

        /// <summary>The NVIDIA driver's version; null when unknown.</summary>
        public string Driver { get; }

        public bool DriverOk { get; }

        /// <summary>The folder chosen to build in.</summary>
        public string Location { get; }

        public long FreeBytes { get; }

        public bool SpaceOk { get; }

        public bool Online { get; }

        public bool AllOk => GpuOk && DriverOk && SpaceOk && Online;
    }

    /// <summary>
    /// Preparing Voice Lab on this PC (Voice Lab detailed design 8.1, 8.2): the check of the PC, the terms, the folder to
    /// build in, and the build itself, which continues where it stopped. The screen reads it on the main thread.
    /// </summary>
    public interface IVoiceLabSetup
    {
        VoiceLabSetupState State { get; }

        /// <summary>Why the last build failed; None otherwise, and after a restart.</summary>
        VoiceLabSetupFailure LastFailure { get; }

        /// <summary>
        /// The build was paused because streaming or recording started, or a start waits for them to end (2026-11-13).
        /// Continuing is the user's, once they have ended.
        /// </summary>
        bool PausedForLive { get; }

        /// <summary>Whether this app brings a newer environment than the one in use.</summary>
        bool UpdateAvailable { get; }

        /// <summary>Bytes the build downloads.</summary>
        long DownloadBytes { get; }

        /// <summary>Where the running build is. Read it as often as needed.</summary>
        VoiceLabSetupProgress Progress { get; }

        /// <summary>What the builds since the app started did, oldest first; a copy.</summary>
        IReadOnlyList<VoiceLabSetupEntry> Entries { get; }

        string TermsVersion { get; }

        bool TermsAgreed { get; }

        /// <summary>The terms' full text as Markdown, in a language, else English; null when they cannot be read.</summary>
        string TermsText(string language);

        void AgreeToTerms();

        /// <summary>The folder chosen to build in.</summary>
        string Location { get; }

        /// <summary>A CRUD update (system design 6.31): Invalid for a folder that does not exist, Busy while one is in use.</summary>
        CrudResult SetLocation(string folder);

        /// <summary>Looks at the GPU, its driver, the space where it would be built, and the connection.</summary>
        Task<VoiceLabPcCheck> CheckPcAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Builds the environment, or the update, and switches Voice Lab to it. A failure comes back as
        /// <see cref="State"/> Failed with <see cref="LastFailure"/>; stopping it throws OperationCanceledException.
        /// </summary>
        Task RunAsync(CancellationToken cancellationToken);
    }
}
