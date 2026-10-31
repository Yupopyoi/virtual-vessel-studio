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
        public const string MonitorBus = "monitor";

        /// <summary>A send at or below this is none: JSON cannot write negative infinity.</summary>
        public const float NoSendDb = -120f;

        /// <summary>The game audio starts a little lower than the voice, so talking over a game stays clear.</summary>
        public const float GameAudioGainDb = -6f;

        /// <summary>The user's choice of a default (2026-10-29): the avatar's motion was measured at least 0.2 s behind the voice.</summary>
        public const int DefaultStreamDelayMs = 300;

        public const int MaxStreamDelayMs = 1000;

        public List<AudioSourceSettings> Sources { get; set; } = new List<AudioSourceSettings>();

        /// <summary>
        /// How much later recording and streaming get the sound (audio detailed design 8.5). Added in version 1
        /// without a step: a file without it reads as the default.
        /// </summary>
        public int StreamDelayMs { get; set; } = DefaultStreamDelayMs;

        /// <summary>
        /// What the user hears (audio detailed design 10). Added in version 1 without a step: a file without it
        /// starts on Windows' default output.
        /// </summary>
        public AudioMonitorSettings Monitor { get; set; }

        public static IReadOnlyList<ConfigMigration> Migrations { get; } = Array.Empty<ConfigMigration>();

        public AudioSettings Clone()
        {
            var clone = new AudioSettings { Sources = new List<AudioSourceSettings>(), StreamDelayMs = StreamDelayMs, Monitor = Monitor?.Clone() };
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
        public static AudioSettings Prepared(AudioSettings settings, IReadOnlyList<AudioDevice> devices, Func<string> newId, IReadOnlyList<AudioDevice> outputs = null)
        {
            AudioSettings next = (settings ?? new AudioSettings()).Clone();
            next.Sources.RemoveAll(source => source == null);
            if (next.Sources.Find(source => source.Role == MicrophoneRole) == null)
            {
                next.Sources.Insert(0, new AudioSourceSettings { Id = newId(), Role = MicrophoneRole, DeviceId = DefaultOf(devices) });
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

                // The user hears the game; their own voice, echoed back or into a microphone from speakers, not.
                if (!source.Sends.ContainsKey(MonitorBus))
                {
                    source.Sends[MonitorBus] = source.Role == GameAudioRole ? 0f : NoSendDb;
                }
            }

            next.Monitor ??= new AudioMonitorSettings { DeviceId = DefaultOf(outputs) };
            return next;
        }

        /// <summary>Windows' default device of <paramref name="devices"/>, or null. Pure.</summary>
        private static string DefaultOf(IReadOnlyList<AudioDevice> devices)
        {
            string found = null;
            foreach (AudioDevice device in devices ?? Array.Empty<AudioDevice>())
            {
                found = device.IsDefault ? device.Id : found;
            }

            return found;
        }

        /// <summary>A delay in range, in whole milliseconds. Pure.</summary>
        public static int ClampDelayMs(double milliseconds)
        {
            return double.IsNaN(milliseconds) ? DefaultStreamDelayMs : (int)Math.Round(Math.Max(0, Math.Min(MaxStreamDelayMs, milliseconds)));
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

    /// <summary>The monitor in the audio profile.</summary>
    public sealed class AudioMonitorSettings
    {
        /// <summary>The output's endpoint ID; null for none.</summary>
        public string DeviceId { get; set; }

        public float GainDb { get; set; }

        public bool Muted { get; set; }

        public AudioMonitorSettings Clone()
        {
            return (AudioMonitorSettings)MemberwiseClone();
        }

        /// <summary>What the user sees. Pure.</summary>
        public AudioMonitorOptions ToOptions()
        {
            return new AudioMonitorOptions(string.IsNullOrEmpty(DeviceId) ? null : DeviceId, 0f, Muted).WithGain(float.IsNaN(GainDb) ? 0f : GainDb);
        }

        /// <summary>A copy with the user's options. Pure.</summary>
        public static AudioMonitorSettings Of(AudioMonitorOptions options)
        {
            return new AudioMonitorSettings { DeviceId = options.DeviceId, GainDb = options.GainDb, Muted = options.Muted };
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
            return new AudioSourceOptions(string.IsNullOrEmpty(DeviceId) ? null : DeviceId, ClampGain(GainDb), Muted, SendOf(AudioSettings.StreamBus), SendOf(AudioSettings.MonitorBus));
        }

        /// <summary>A copy with the user's options; other buses' sends are kept. Pure.</summary>
        public AudioSourceSettings With(AudioSourceOptions options)
        {
            AudioSourceSettings next = Clone();
            next.DeviceId = options.DeviceId;
            next.GainDb = ClampGain(options.GainDb);
            next.Muted = options.Muted;
            next.Sends[AudioSettings.StreamBus] = Saved(options.StreamSendDb);
            next.Sends[AudioSettings.MonitorBus] = Saved(options.MonitorSendDb);
            return next;
        }

        private static float Saved(float sendDb)
        {
            return float.IsNegativeInfinity(sendDb) ? AudioSettings.NoSendDb : sendDb;
        }

        private float SendOf(string bus)
        {
            return Sends != null && Sends.TryGetValue(bus, out float db) && db > AudioSettings.NoSendDb ? db : float.NegativeInfinity;
        }

        private static float ClampGain(float db)
        {
            return float.IsNaN(db) ? 0f : Math.Max(AudioSourceOptions.MinGainDb, Math.Min(AudioSourceOptions.MaxGainDb, db));
        }
    }
}
