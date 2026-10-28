using System;
using System.Collections.Generic;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.Audio
{
    /// <summary>
    /// The project's audio profile (audio detailed design 10): the sources as a list, so BGM, SE, and more
    /// microphones join without changing the format. A source of a role this version does not know is kept.
    /// </summary>
    public sealed class AudioSettings
    {
        public const int SchemaVersion = 1;

        /// <summary>The name when the file is not a project's profile, such as in tests.</summary>
        public const string FileName = "audio-settings.json";

        public const string MicrophoneRole = "microphone";
        public const string GameAudioRole = "gameAudio";
        public const string StreamBus = "stream";

        /// <summary>The game audio starts a little lower than the voice, so talking over a game stays clear.</summary>
        public const float GameAudioGainDb = -6f;

        public List<AudioSourceSettings> Sources { get; set; } = new List<AudioSourceSettings>();

        public static IReadOnlyList<ConfigMigration> Migrations { get; } = Array.Empty<ConfigMigration>();

        public AudioSettings Clone()
        {
            var clone = new AudioSettings { Sources = new List<AudioSourceSettings>() };
            foreach (AudioSourceSettings source in Sources ?? new List<AudioSourceSettings>())
            {
                clone.Sources.Add(source?.Clone());
            }

            return clone;
        }

        /// <summary>
        /// The settings with a microphone and a game audio source, adding those missing: the microphone on
        /// Windows' default input device, the game audio on none, since a capture board must be chosen
        /// (audio detailed design 10). Other sources are kept as they are. Pure.
        /// </summary>
        public static AudioSettings Prepared(AudioSettings settings, IReadOnlyList<AudioDevice> devices, Func<string> newId)
        {
            AudioSettings next = (settings ?? new AudioSettings()).Clone();
            next.Sources.RemoveAll(source => source == null);
            if (next.Sources.Find(source => source.Role == MicrophoneRole) == null)
            {
                string defaultDevice = null;
                foreach (AudioDevice device in devices ?? Array.Empty<AudioDevice>())
                {
                    defaultDevice = device.IsDefault ? device.Id : defaultDevice;
                }

                next.Sources.Insert(0, new AudioSourceSettings { Id = newId(), Role = MicrophoneRole, DeviceId = defaultDevice });
            }

            if (next.Sources.Find(source => source.Role == GameAudioRole) == null)
            {
                next.Sources.Add(new AudioSourceSettings { Id = newId(), Role = GameAudioRole, GainDb = GameAudioGainDb });
            }

            foreach (AudioSourceSettings source in next.Sources)
            {
                source.Id = string.IsNullOrEmpty(source.Id) ? newId() : source.Id;
                source.Sends ??= new Dictionary<string, float>();
                if (!source.Sends.ContainsKey(StreamBus))
                {
                    source.Sends[StreamBus] = 0f;
                }
            }

            return next;
        }

        /// <summary>The role of a saved source, or null for one this version does not know. Pure.</summary>
        public static AudioSourceRole? RoleOf(string role)
        {
            return role switch
            {
                MicrophoneRole => AudioSourceRole.Microphone,
                GameAudioRole => AudioSourceRole.GameAudio,
                _ => null,
            };
        }
    }

    /// <summary>One source in the audio profile.</summary>
    public sealed class AudioSourceSettings
    {
        public string Id { get; set; }

        public string Role { get; set; }

        /// <summary>The endpoint ID; null for none.</summary>
        public string DeviceId { get; set; }

        public float GainDb { get; set; }

        public bool Muted { get; set; }

        /// <summary>Per bus, in dB; a bus not listed gets none. Negative infinity cannot be written to JSON, so -120 or below is none.</summary>
        public Dictionary<string, float> Sends { get; set; } = new Dictionary<string, float>();

        public AudioSourceSettings Clone()
        {
            AudioSourceSettings clone = (AudioSourceSettings)MemberwiseClone();
            clone.Sends = Sends != null ? new Dictionary<string, float>(Sends) : new Dictionary<string, float>();
            return clone;
        }

        /// <summary>What the user sees of this source. Pure.</summary>
        public AudioSourceOptions ToOptions()
        {
            float send = Sends != null && Sends.TryGetValue(AudioSettings.StreamBus, out float db) && db > -120f ? db : float.NegativeInfinity;
            return new AudioSourceOptions(string.IsNullOrEmpty(DeviceId) ? null : DeviceId, ClampGain(GainDb), Muted, send);
        }

        /// <summary>A copy with the user's options; other buses' sends are kept. Pure.</summary>
        public AudioSourceSettings With(AudioSourceOptions options)
        {
            AudioSourceSettings next = Clone();
            next.DeviceId = options.DeviceId;
            next.GainDb = ClampGain(options.GainDb);
            next.Muted = options.Muted;
            next.Sends[AudioSettings.StreamBus] = float.IsNegativeInfinity(options.StreamSendDb) ? -120f : options.StreamSendDb;
            return next;
        }

        private static float ClampGain(float db)
        {
            return float.IsNaN(db) ? 0f : Math.Max(AudioSourceOptions.MinGainDb, Math.Min(AudioSourceOptions.MaxGainDb, db));
        }
    }
}
