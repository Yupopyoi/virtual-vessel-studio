using System;

namespace VirtualVessel.Audio
{
    /// <summary>
    /// How BGM is mixed (BGM and SE detailed design 5, 8.4, 8.5): lowered while the user talks (ducking), and how long
    /// a switch to another track takes (the crossfade). Values are kept in range.
    /// </summary>
    public readonly struct BgmMixOptions : IEquatable<BgmMixOptions>
    {
        public const float MinDuckDepthDb = -24f;
        public const float MaxDuckDepthDb = -3f;
        public const float DefaultDuckDepthDb = -12f;
        public const float MinDuckThresholdDb = -60f;
        public const float MaxDuckThresholdDb = -20f;
        public const float DefaultDuckThresholdDb = -40f;
        public const float MaxCrossfadeSeconds = 5f;
        public const float DefaultCrossfadeSeconds = 1f;

        /// <summary>Lowered by more than this, BGM counts as ducked for the screens.</summary>
        public const float DuckingShownBelowDb = -1f;

        public BgmMixOptions(bool ducking, float duckDepthDb, float duckThresholdDb, float crossfadeSeconds)
        {
            Ducking = ducking;
            DuckDepthDb = Clamp(duckDepthDb, MinDuckDepthDb, MaxDuckDepthDb, DefaultDuckDepthDb);
            DuckThresholdDb = Clamp(duckThresholdDb, MinDuckThresholdDb, MaxDuckThresholdDb, DefaultDuckThresholdDb);
            CrossfadeSeconds = Clamp(crossfadeSeconds, 0f, MaxCrossfadeSeconds, DefaultCrossfadeSeconds);
        }

        /// <summary>Ducking off, lowered 12 dB, and a 1-second crossfade (the user's decisions, 2026-11-23).</summary>
        public static BgmMixOptions Default => new BgmMixOptions(false, DefaultDuckDepthDb, DefaultDuckThresholdDb, DefaultCrossfadeSeconds);

        /// <summary>Whether BGM is lowered while the user talks.</summary>
        public bool Ducking { get; }

        /// <summary>How far BGM is lowered, in dB (negative).</summary>
        public float DuckDepthDb { get; }

        /// <summary>How loud the voice must be to lower it, in dBFS.</summary>
        public float DuckThresholdDb { get; }

        /// <summary>How long a switch to another track takes; 0 switches as before.</summary>
        public float CrossfadeSeconds { get; }

        public BgmMixOptions WithDucking(bool ducking) => new BgmMixOptions(ducking, DuckDepthDb, DuckThresholdDb, CrossfadeSeconds);

        public BgmMixOptions WithDuckDepth(float db) => new BgmMixOptions(Ducking, db, DuckThresholdDb, CrossfadeSeconds);

        public BgmMixOptions WithDuckThreshold(float db) => new BgmMixOptions(Ducking, DuckDepthDb, db, CrossfadeSeconds);

        public BgmMixOptions WithCrossfade(float seconds) => new BgmMixOptions(Ducking, DuckDepthDb, DuckThresholdDb, seconds);

        /// <summary>A value in range; NaN is the default. Pure.</summary>
        public static float Clamp(float value, float min, float max, float fallback)
        {
            return float.IsNaN(value) ? fallback : Math.Max(min, Math.Min(max, value));
        }

        public bool Equals(BgmMixOptions other)
        {
            return Ducking == other.Ducking && DuckDepthDb.Equals(other.DuckDepthDb) && DuckThresholdDb.Equals(other.DuckThresholdDb) && CrossfadeSeconds.Equals(other.CrossfadeSeconds);
        }

        public override bool Equals(object obj)
        {
            return obj is BgmMixOptions other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Ducking, DuckDepthDb, DuckThresholdDb, CrossfadeSeconds);
        }

        public override string ToString()
        {
            return $"ducking {(Ducking ? "on" : "off")} ({DuckDepthDb:0.#} dB from {DuckThresholdDb:0.#} dBFS), crossfade {CrossfadeSeconds:0.#} s";
        }
    }

    /// <summary>
    /// An engine that mixes BGM (BGM and SE detailed design 8.4, 8.5): ducking and the crossfade. Calls are safe from
    /// the main thread.
    /// </summary>
    public interface IAudioMixEngine
    {
        void SetDucking(int keySource, int duckedSource, bool enabled, float depthDb, float thresholdDb);

        /// <summary>How far the ducked source is lowered now, in dB; 0 when it is not.</summary>
        float GetDuckingDb();

        void SetBgmCrossfade(TimeSpan length);
    }
}
