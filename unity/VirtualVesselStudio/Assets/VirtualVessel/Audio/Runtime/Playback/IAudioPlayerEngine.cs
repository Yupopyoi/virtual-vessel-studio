using System;

namespace VirtualVessel.Audio
{
    /// <summary>Which player a source plays from (VV_AUDIO_PLAYER_*).</summary>
    public enum AudioPlayer
    {
        Bgm = 0,
        SoundEffects = 1,
    }

    /// <summary>The BGM deck's state (VV_AUDIO_BGM_*).</summary>
    public enum BgmEngineState
    {
        Idle = 0,
        Starting = 1,
        Playing = 2,
        Stopping = 3,
        Failed = 4,
    }

    /// <summary>What the BGM deck reports (vv_audio_bgm_status).</summary>
    public readonly struct BgmEngineStatus
    {
        public BgmEngineStatus(BgmEngineState state, int error, long generation, long finishedGeneration, long playedFrames, long lengthFrames, long underruns)
        {
            State = state;
            Error = error;
            Generation = generation;
            FinishedGeneration = finishedGeneration;
            PlayedFrames = playedFrames;
            LengthFrames = lengthFrames;
            Underruns = underruns;
        }

        public BgmEngineState State { get; }

        /// <summary>The HRESULT when it failed.</summary>
        public int Error { get; }

        /// <summary>Of the last play.</summary>
        public long Generation { get; }

        /// <summary>Of the last track that played to its end.</summary>
        public long FinishedGeneration { get; }

        public long PlayedFrames { get; }

        /// <summary>Zero when the file does not say.</summary>
        public long LengthFrames { get; }

        public long Underruns { get; }
    }

    /// <summary>A sound effect handed to the engine: its handle, or why it could not be.</summary>
    public readonly struct AudioEffectLoad
    {
        public AudioEffectLoad(int effect, TimeSpan duration, string problem)
        {
            Effect = effect;
            Duration = duration;
            Problem = problem;
        }

        /// <summary>-1 when it could not be loaded.</summary>
        public int Effect { get; }

        public TimeSpan Duration { get; }

        /// <summary>For developers; null when loaded.</summary>
        public string Problem { get; }
    }

    /// <summary>
    /// An engine with BGM and SE players (BGM and SE detailed design 4, 8): the native deck and voices. Every call is
    /// safe from any thread but the mix's; loading an effect decodes it, so it is called off the main thread.
    /// </summary>
    public interface IAudioPlayerEngine
    {
        int AddPlayerSource(AudioPlayer player);

        /// <summary>Fades out what plays and starts the file; returns the play's generation, 0 when it could not ask.</summary>
        long PlayBgm(string path, bool loop, float gainDb);

        void StopBgm();

        /// <summary>The playing track's own volume, at once and without a click.</summary>
        void SetBgmGain(float gainDb);

        BgmEngineStatus GetBgmStatus();

        AudioEffectLoad LoadEffect(string path);

        void UnloadEffect(int effect);

        bool PlayEffect(int effect, float gainDb);

        void StopEffects();

        long EffectsStolen { get; }
    }
}
