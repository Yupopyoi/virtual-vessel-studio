using System;
using System.Collections.Generic;
using VirtualVessel.Core.State;

namespace VirtualVessel.Audio
{
    /// <summary>What a source is for (audio detailed design 4). Later: music and sound effects.</summary>
    public enum AudioSourceRole
    {
        Microphone,
        GameAudio,
    }

    /// <summary>Where a source is (audio detailed design 5).</summary>
    public enum AudioSourceState
    {
        /// <summary>No device chosen.</summary>
        Off,
        Starting,
        Running,

        /// <summary>The device is missing or unplugged; it opens again when it is back.</summary>
        Unavailable,

        /// <summary>The device cannot be opened; <see cref="AudioProblem"/> says why.</summary>
        Error,
    }

    /// <summary>Why a source is in error, so the user is told what to do (audio detailed design 12).</summary>
    public enum AudioProblem
    {
        None,

        /// <summary>Another application uses the device exclusively.</summary>
        InUse,

        /// <summary>Windows' privacy settings deny microphone access.</summary>
        AccessDenied,

        Failed,
    }

    /// <summary>An audio device as the user sees it.</summary>
    public readonly struct AudioDevice : IEquatable<AudioDevice>
    {
        public AudioDevice(string id, string name, bool isDefault)
        {
            Id = id;
            Name = name;
            IsDefault = isDefault;
        }

        /// <summary>The endpoint ID, stable across restarts; what the profile saves.</summary>
        public string Id { get; }

        public string Name { get; }

        /// <summary>Windows' default input device.</summary>
        public bool IsDefault { get; }

        public bool Equals(AudioDevice other)
        {
            return Id == other.Id && Name == other.Name && IsDefault == other.IsDefault;
        }

        public override bool Equals(object obj)
        {
            return obj is AudioDevice other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Name, IsDefault);
        }
    }

    /// <summary>What the user sets for a source (audio detailed design 5, 10).</summary>
    public readonly struct AudioSourceOptions : IEquatable<AudioSourceOptions>
    {
        public const float MinGainDb = -60f;
        public const float MaxGainDb = 12f;

        public AudioSourceOptions(string deviceId, float gainDb, bool muted, float streamSendDb)
        {
            DeviceId = deviceId;
            GainDb = gainDb;
            Muted = muted;
            StreamSendDb = streamSendDb;
        }

        /// <summary>The endpoint ID; null turns the source off.</summary>
        public string DeviceId { get; }

        public float GainDb { get; }

        public bool Muted { get; }

        /// <summary>How much reaches the stream bus (recording, streaming), in dB; negative infinity for none.</summary>
        public float StreamSendDb { get; }

        public AudioSourceOptions WithDevice(string deviceId)
        {
            return new AudioSourceOptions(string.IsNullOrEmpty(deviceId) ? null : deviceId, GainDb, Muted, StreamSendDb);
        }

        public AudioSourceOptions WithGain(float gainDb)
        {
            return new AudioSourceOptions(DeviceId, Math.Max(MinGainDb, Math.Min(MaxGainDb, gainDb)), Muted, StreamSendDb);
        }

        public AudioSourceOptions WithMuted(bool muted)
        {
            return new AudioSourceOptions(DeviceId, GainDb, muted, StreamSendDb);
        }

        public bool Equals(AudioSourceOptions other)
        {
            return DeviceId == other.DeviceId && GainDb.Equals(other.GainDb) && Muted == other.Muted && StreamSendDb.Equals(other.StreamSendDb);
        }

        public override bool Equals(object obj)
        {
            return obj is AudioSourceOptions other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(DeviceId, GainDb, Muted, StreamSendDb);
        }
    }

    /// <summary>One source of sound: a device, its gain and mute, and its level (audio detailed design 5).</summary>
    public interface IAudioSource
    {
        string Id { get; }

        AudioSourceRole Role { get; }

        IReadOnlyObservable<AudioSourceState> State { get; }

        IReadOnlyObservable<AudioProblem> Problem { get; }

        IReadOnlyObservable<AudioSourceOptions> Options { get; }

        /// <summary>Applied at once and saved in the project's audio profile.</summary>
        void SetOptions(AudioSourceOptions options);

        /// <summary>The highest level of the last tenth of a second, after the gain and before mute, in dBFS.</summary>
        float PeakDb { get; }
    }

    /// <summary>The stream bus for the encoder, as plain values (vv_audio_stream_tap), so Video needs no link to Audio.</summary>
    public readonly struct AudioStreamTap
    {
        public AudioStreamTap(IntPtr context, IntPtr read)
        {
            Context = context;
            Read = read;
        }

        public IntPtr Context { get; }

        /// <summary>A native function pointer: int32_t (*)(void*, float*, int32_t, int64_t, int64_t*).</summary>
        public IntPtr Read { get; }

        public bool IsValid => Context != IntPtr.Zero && Read != IntPtr.Zero;
    }

    /// <summary>The sources, the devices, and the stream bus (audio detailed design 5).</summary>
    public interface IAudioService
    {
        /// <summary>The microphone, then the game audio; more come with BGM and SE.</summary>
        IReadOnlyList<IAudioSource> Sources { get; }

        IReadOnlyObservable<IReadOnlyList<AudioDevice>> InputDevices { get; }

        /// <summary>Lists the input devices again, such as when the user opens the device choice.</summary>
        void RefreshDevices();

        /// <summary>How far the stream bus is behind the moment the sound was heard (audio detailed design 8.4).</summary>
        TimeSpan StreamLatency { get; }

        /// <summary>
        /// How much later recording and streaming get the sound, so the voice meets the avatar's motion,
        /// which tracking delays (audio detailed design 8.5). What the user hears is not delayed.
        /// </summary>
        IReadOnlyObservable<TimeSpan> StreamDelay { get; }

        /// <summary>Kept within 0 to <see cref="AudioSettings.MaxStreamDelayMs"/> ms and saved in the audio profile.</summary>
        void SetStreamDelay(TimeSpan delay);

        AudioStreamTap StreamTap { get; }
    }
}
