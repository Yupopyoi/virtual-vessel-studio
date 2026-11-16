using VirtualVessel.Core.State;

namespace VirtualVessel.Audio.Playback
{
    /// <summary>What the BGM player does (BGM and SE detailed design 7).</summary>
    public enum BgmPlayState
    {
        Idle,

        /// <summary>Opening the file and reading ahead.</summary>
        Starting,
        Playing,

        /// <summary>Fading out.</summary>
        Stopping,

        /// <summary>The track could not be played; other audio goes on.</summary>
        Failed,
    }

    /// <summary>The player's state and the track it is on.</summary>
    public readonly struct BgmStatus : System.IEquatable<BgmStatus>
    {
        public BgmStatus(BgmPlayState state, string trackId)
        {
            State = state;
            TrackId = trackId;
        }

        public BgmPlayState State { get; }

        /// <summary>The track playing, starting, stopping, or that failed; null when idle.</summary>
        public string TrackId { get; }

        /// <summary>Whether the track's file is in use, so it is not deleted.</summary>
        public bool HoldsTrack => TrackId != null && (State == BgmPlayState.Starting || State == BgmPlayState.Playing || State == BgmPlayState.Stopping);

        public bool Equals(BgmStatus other)
        {
            return State == other.State && TrackId == other.TrackId;
        }

        public override bool Equals(object obj)
        {
            return obj is BgmStatus other && Equals(other);
        }

        public override int GetHashCode()
        {
            return System.HashCode.Combine(State, TrackId);
        }
    }

    /// <summary>A track to play, and whether it loops.</summary>
    public readonly struct BgmCue
    {
        public BgmCue(string trackId, bool loop)
        {
            TrackId = trackId;
            Loop = loop;
        }

        public string TrackId { get; }

        public bool Loop { get; }
    }

    /// <summary>
    /// What plays next (BGM and SE detailed design 5). The first version has <see cref="SingleTrack"/>; playlists and
    /// shuffle are other implementations, and the player does not change.
    /// </summary>
    public interface IBgmSequence
    {
        /// <summary>The first track when <paramref name="finished"/> is null, else the one after it; null to end.</summary>
        BgmCue? Next(BgmCue? finished);
    }

    /// <summary>One track, looped or not: the first version's only sequence.</summary>
    public sealed class SingleTrack : IBgmSequence
    {
        private readonly BgmCue _cue;

        public SingleTrack(string trackId, bool loop)
        {
            _cue = new BgmCue(trackId, loop);
        }

        public BgmCue? Next(BgmCue? finished)
        {
            return finished == null ? _cue : (BgmCue?)null;
        }
    }

    /// <summary>Plays BGM on the stream (BGM and SE detailed design 5, 8.1).</summary>
    public interface IBgmPlayer
    {
        IReadOnlyObservable<BgmStatus> Status { get; }

        /// <summary>Fades out what plays and starts the sequence's first track.</summary>
        void Play(IBgmSequence sequence);

        /// <summary>Fades out.</summary>
        void Stop();

        /// <summary>0 to 1 through the track; null when nothing plays or its length is unknown.</summary>
        float? Progress { get; }
    }

    /// <summary>Plays sound effects on the stream (BGM and SE detailed design 5, 8.2).</summary>
    public interface ISoundEffectPlayer
    {
        /// <summary>Plays it from its start, over whatever plays. One not loaded yet is not played.</summary>
        void Play(string id);

        void StopAll();

        /// <summary>Whether it is loaded and can play now.</summary>
        bool IsReady(string id);
    }
}
