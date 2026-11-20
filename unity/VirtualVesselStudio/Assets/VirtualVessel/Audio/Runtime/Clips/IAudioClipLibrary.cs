using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.ProjectData.Records;

namespace VirtualVessel.Audio.Clips
{
    /// <summary>What a registered audio file is for (BGM and SE detailed design 4).</summary>
    public enum AudioClipKind
    {
        Bgm,
        SoundEffect,
    }

    /// <summary>Why a file cannot be registered, so the user is told what to do (BGM and SE detailed design 12).</summary>
    public enum AudioClipProblem
    {
        None,

        /// <summary>Not a format this version takes (WAV, MP3).</summary>
        Unsupported,

        /// <summary>Missing, or Windows cannot decode it.</summary>
        Unreadable,

        /// <summary>A sound effect longer than <see cref="AudioClipLibrary.MaxEffectSeconds"/>.</summary>
        TooLong,
    }

    /// <summary>One registered BGM or SE (BGM and SE detailed design 6.1).</summary>
    public sealed class AudioClipRecord
    {
        public AudioClipRecord(string id, AudioClipKind kind, string name, float gainDb, bool loop, TimeSpan duration, string originalName, DateTimeOffset addedAt, string filePath)
        {
            Id = id;
            Kind = kind;
            Name = name;
            GainDb = gainDb;
            Loop = loop;
            Duration = duration;
            OriginalName = originalName;
            AddedAt = addedAt;
            FilePath = filePath;
        }

        public string Id { get; }

        public AudioClipKind Kind { get; }

        public string Name { get; }

        /// <summary>The file's own volume, apart from the BGM or SE source's.</summary>
        public float GainDb { get; }

        /// <summary>BGM only: plays again from its start at its end.</summary>
        public bool Loop { get; }

        /// <summary>Zero when the file does not say.</summary>
        public TimeSpan Duration { get; }

        public string OriginalName { get; }

        public DateTimeOffset AddedAt { get; }

        /// <summary>The copy in the library, which playback reads.</summary>
        public string FilePath { get; }
    }

    /// <summary>A change to a registration: the values to set, the others left as they are.</summary>
    public readonly struct AudioClipChange
    {
        public AudioClipChange(string name = null, float? gainDb = null, bool? loop = null)
        {
            Name = name;
            GainDb = gainDb;
            Loop = loop;
        }

        public string Name { get; }

        public float? GainDb { get; }

        public bool? Loop { get; }
    }

    /// <summary>
    /// The registered BGM and SE files (BGM and SE detailed design 5, 6.1). Every operation reports its failure as a
    /// result and leaves handling it to the caller (system design 6.31).
    /// </summary>
    public interface IAudioClipLibrary
    {
        /// <summary>The extensions offered in a file dialog, with their dots.</summary>
        IReadOnlyList<string> FileExtensions { get; }

        /// <summary>Raised after a registration of the kind was added, changed, or deleted; on any thread.</summary>
        event Action<AudioClipKind> Changed;

        /// <summary>
        /// Every registration of the kind that can be read, in the user's order: those they placed first, then the
        /// others in the order they were added.
        /// </summary>
        CrudResult<IReadOnlyList<AudioClipRecord>> List(AudioClipKind kind);

        /// <summary>NotFound when there is no such registration.</summary>
        CrudResult<AudioClipRecord> Get(string id);

        /// <summary>Whether a file can be registered, and if not, why; reads the file, so call it off the main thread.</summary>
        Task<AudioClipProblem> CheckAsync(AudioClipKind kind, string sourcePath, CancellationToken cancellationToken);

        /// <summary>
        /// Copies the file in and records it. Invalid when it cannot be registered (<see cref="CheckAsync"/> says why),
        /// StorageFailed when it could not be copied. A null or empty name takes the file's.
        /// </summary>
        Task<CrudResult<AudioClipRecord>> AddAsync(AudioClipKind kind, string sourcePath, string name, CancellationToken cancellationToken);

        /// <summary>Invalid for an empty or too long name; NotFound without the registration.</summary>
        CrudResult<AudioClipRecord> Update(string id, AudioClipChange change);

        /// <summary>Busy while it plays; a registration that is not there succeeds.</summary>
        CrudResult Delete(string id);

        /// <summary>
        /// Puts a registration at a position in its kind's list (0 is the first), the others keeping their order.
        /// A position past the end puts it last. NotFound without the registration.
        /// </summary>
        CrudResult Move(string id, int position);
    }
}
