using System;
using System.Collections.Generic;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// What Voice Lab has made, for the Made voices tab (Voice Lab detailed design 8.6): runs and registered
    /// voices to list, play, name, note, and delete. It works without a training environment, so what was made
    /// can always be seen. Making a run again is <see cref="IVoiceLab.Remake"/>, which needs the environment.
    /// </summary>
    public interface IVoiceLabLibrary
    {
        /// <summary>Newest first.</summary>
        IReadOnlyList<VoiceLabRunRecord> ListRuns();

        /// <summary>Newest first.</summary>
        IReadOnlyList<VoiceLabVoiceRecord> ListVoices();

        /// <exception cref="VoiceLabFailedException">It could not be saved.</exception>
        void SetComment(string runId, string comment);

        /// <exception cref="VoiceLabFailedException">The name is empty or too long, or it could not be saved.</exception>
        void RenameVoice(string voiceId, string name);

        /// <exception cref="VoiceLabFailedException">The run is being made, or it could not be deleted.</exception>
        void DeleteRun(string runId);

        /// <exception cref="VoiceLabFailedException">It could not be deleted.</exception>
        void DeleteVoice(string voiceId);
    }

    /// <summary>A Training Run as the Made voices tab shows it.</summary>
    public sealed class VoiceLabRunRecord
    {
        public VoiceLabRunRecord(string runId, DateTimeOffset createdAt, VoiceLabBuildState state, IReadOnlyList<string> words, string freeWords, VoiceLabTimbre timbre, string comment, string baseFile, IReadOnlyList<string> voiceNames)
        {
            RunId = runId;
            CreatedAt = createdAt;
            State = state;
            Words = words ?? Array.Empty<string>();
            FreeWords = freeWords ?? string.Empty;
            Timbre = timbre;
            Comment = comment ?? string.Empty;
            BaseFile = baseFile;
            VoiceNames = voiceNames ?? Array.Empty<string>();
        }

        public string RunId { get; }

        public DateTimeOffset CreatedAt { get; }

        public VoiceLabBuildState State { get; }

        /// <summary>The shelf's word ids it was asked with; none for a run from before they were kept.</summary>
        public IReadOnlyList<string> Words { get; }

        public string FreeWords { get; }

        public VoiceLabTimbre Timbre { get; }

        public string Comment { get; }

        /// <summary>The base's audio, to play; null when it is gone.</summary>
        public string BaseFile { get; }

        /// <summary>The names of the voices registered from it.</summary>
        public IReadOnlyList<string> VoiceNames { get; }
    }

    /// <summary>A registered voice as the Made voices tab shows it.</summary>
    public sealed class VoiceLabVoiceRecord
    {
        public VoiceLabVoiceRecord(string id, string name, string runId, DateTimeOffset registeredAt, int medianHz, string baseFile)
        {
            Id = id;
            Name = name ?? string.Empty;
            RunId = runId;
            RegisteredAt = registeredAt;
            MedianHz = medianHz;
            BaseFile = baseFile;
        }

        public string Id { get; }

        public string Name { get; }

        public string RunId { get; }

        public DateTimeOffset RegisteredAt { get; }

        /// <summary>0 when unknown.</summary>
        public int MedianHz { get; }

        /// <summary>The base it was made from, to play; null when it is gone.</summary>
        public string BaseFile { get; }
    }

    /// <summary>A run's choices put back to make it again: the words, and the base and timbre when the base is still there.</summary>
    public sealed class VoiceLabRemake
    {
        public VoiceLabRemake(IReadOnlyList<string> words, string freeWords, VoiceLabCandidate candidate, VoiceLabTimbre timbre)
        {
            Words = words ?? Array.Empty<string>();
            FreeWords = freeWords ?? string.Empty;
            Candidate = candidate;
            Timbre = timbre;
        }

        public IReadOnlyList<string> Words { get; }

        public string FreeWords { get; }

        /// <summary>Null when the base's audio is gone; then only the words come back.</summary>
        public VoiceLabCandidate Candidate { get; }

        public VoiceLabTimbre Timbre { get; }
    }
}
