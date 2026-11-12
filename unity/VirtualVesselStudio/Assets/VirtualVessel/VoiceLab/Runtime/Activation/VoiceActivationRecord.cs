using System;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.VoiceLab.Activation
{
    /// <summary>Where activation is (Voice Lab detailed design 6.1).</summary>
    public enum VoiceActivationState
    {
        NotStarted,
        Installing,
        Ready,
        Failed,
    }

    /// <summary>
    /// Config/voice-activation.json (Voice Lab detailed design 6.1): the terms agreed to, and the environment built. Read
    /// and written with ProjectData's settings files (ConfigFileStore), which keep fields a newer version added.
    /// </summary>
    public sealed class VoiceActivationRecord
    {
        public const int SchemaVersion = 1;
        public const string FileName = "voice-activation.json";

        /// <summary>The store of this record in a config folder; one place, so its name and schema cannot drift apart.</summary>
        public static ConfigFileStore<VoiceActivationRecord> StoreIn(string configDirectory, ISystemClock clock)
        {
            return new ConfigFileStore<VoiceActivationRecord>(configDirectory, FileName, SchemaVersion, () => new VoiceActivationRecord(), clock);
        }

        /// <summary>The terms' version agreed to; null before agreeing.</summary>
        public string TermsVersion { get; set; }

        public DateTimeOffset? AgreedAtUtc { get; set; }

        /// <summary>The environment's version that is built and in use; null before the first one.</summary>
        public string ComponentsVersion { get; set; }

        /// <summary>The version being built, so a stopped build resumes into the same folder; null when none is.</summary>
        public string InstallingVersion { get; set; }

        public VoiceActivationState State { get; set; } = VoiceActivationState.NotStarted;

        /// <summary>
        /// The folder the user chose to build in (the app adds its own folders inside it); null for the default, the
        /// user's local application data.
        /// </summary>
        public string Location { get; set; }

        public VoiceActivationRecord Clone()
        {
            return (VoiceActivationRecord)MemberwiseClone();
        }
    }
}
