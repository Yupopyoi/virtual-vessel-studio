using System;
using System.Threading.Tasks;
using VirtualVessel.Core.Lifecycle;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.Voice
{
    /// <summary>
    /// The project's voice settings (Voice detailed design 6.2): the voice used for the stream and its pitch. A voice
    /// that is gone stays named here, not dropped (CLAUDE.md 25).
    /// </summary>
    public sealed class VoiceProfile
    {
        public const int SchemaVersion = 1;

        /// <summary>The chosen voice; null when none is chosen, and then the first usable one runs.</summary>
        public string VoiceModelId { get; set; }

        /// <summary>The user's adjustment in semitones, -24 to 24.</summary>
        public float Semitones { get; set; }

        public bool AutoPitch { get; set; } = true;

        /// <summary>Whether the conversion was on, to turn it on again at start. Off by default.</summary>
        public bool Enabled { get; set; }

        public VoiceProfile Clone()
        {
            return (VoiceProfile)MemberwiseClone();
        }
    }

    /// <summary>
    /// This PC's voice settings (Voice detailed design 6.3), in Config/voice-settings.json: the user's own pitch as
    /// measured, so the pitch matches from the start next time (the user's request, 2026-11-08).
    /// </summary>
    public sealed class VoiceSettings
    {
        public const int SchemaVersion = 1;
        public const string FileName = "voice-settings.json";

        /// <summary>The user's median pitch in Hz once measured; 0 until then.</summary>
        public float UserPitchHz { get; set; }

        public VoiceSettings Clone()
        {
            return (VoiceSettings)MemberwiseClone();
        }
    }

    /// <summary>
    /// Where the voice settings are read from and saved to. Saving waits a moment so a slider makes one write, and the
    /// frame loop runs it only while a save waits, so the frames pay nothing otherwise.
    /// </summary>
    public sealed class VoiceStorage
    {
        private readonly ConfigSaveScheduler<VoiceProfile> _profileSaves;
        private readonly ConfigSaveScheduler<VoiceSettings> _settingsSaves;
        private readonly IFrameLoop _frameLoop;
        private IDisposable _tick;

        public VoiceStorage(VoiceProfile profile, VoiceSettings settings, ConfigSaveScheduler<VoiceProfile> profileSaves, ConfigSaveScheduler<VoiceSettings> settingsSaves, IFrameLoop frameLoop)
        {
            Profile = profile ?? new VoiceProfile();
            Settings = settings ?? new VoiceSettings();
            _profileSaves = profileSaves;
            _settingsSaves = settingsSaves;
            _frameLoop = frameLoop;
        }

        /// <summary>Nothing saved: for tests and for running without a project.</summary>
        public static VoiceStorage InMemory()
        {
            return new VoiceStorage(new VoiceProfile(), new VoiceSettings(), null, null, null);
        }

        /// <summary>The project's profile file and this PC's settings file, read now.</summary>
        public static VoiceStorage Open(string profileDirectory, string profileFile, string configDirectory, ISystemClock clock, IMonotonicClock monotonic, IFrameLoop frameLoop, ILog log)
        {
            var profiles = new ConfigFileStore<VoiceProfile>(profileDirectory, profileFile, VoiceProfile.SchemaVersion, () => new VoiceProfile(), clock);
            var settings = new ConfigFileStore<VoiceSettings>(configDirectory, VoiceSettings.FileName, VoiceSettings.SchemaVersion, () => new VoiceSettings(), clock);
            VoiceProfile profile = profiles.Load().Value;
            VoiceSettings local = settings.Load().Value;
            TimeSpan wait = TimeSpan.FromSeconds(0.5);
            return new VoiceStorage(
                profile,
                local,
                new ConfigSaveScheduler<VoiceProfile>(profiles, monotonic, wait, failure => log?.Warning($"Failed to save the voice profile to {profiles.FilePath}.", failure)),
                new ConfigSaveScheduler<VoiceSettings>(settings, monotonic, wait, failure => log?.Warning($"Failed to save the voice settings to {settings.FilePath}.", failure)),
                frameLoop);
        }

        public VoiceProfile Profile { get; private set; }

        public VoiceSettings Settings { get; private set; }

        public void SaveProfile(VoiceProfile profile)
        {
            Profile = profile.Clone();
            _profileSaves?.Changed(Profile.Clone());
            Watch();
        }

        public void SaveSettings(VoiceSettings settings)
        {
            Settings = settings.Clone();
            _settingsSaves?.Changed(Settings.Clone());
            Watch();
        }

        /// <summary>Writes what waits and waits for it, such as at shutdown.</summary>
        public async Task FlushAsync()
        {
            _tick?.Dispose();
            _tick = null;
            if (_profileSaves != null)
            {
                await _profileSaves.FlushAsync().ConfigureAwait(false);
            }

            if (_settingsSaves != null)
            {
                await _settingsSaves.FlushAsync().ConfigureAwait(false);
            }
        }

        private void Watch()
        {
            if (_frameLoop != null && _tick == null && (_profileSaves != null || _settingsSaves != null))
            {
                _tick = _frameLoop.Register(Tick);
            }
        }

        private void Tick()
        {
            _profileSaves?.Tick();
            _settingsSaves?.Tick();
            if (!(_profileSaves?.Pending ?? false) && !(_settingsSaves?.Pending ?? false))
            {
                _tick?.Dispose();
                _tick = null;
            }
        }
    }
}
