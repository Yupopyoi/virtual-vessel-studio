using System;
using System.Collections.Generic;
using System.IO;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.VoiceLab.Registration
{
    /// <summary>
    /// Where registered voices' records are kept (Voice Lab detailed design 6.5, 8.6). The first one keeps
    /// model.json beside each voice's files; SQLite (system design 13.12) will be another, so nothing else reads
    /// the records. The voice files stay in their folders either way (system design 13.13).
    /// </summary>
    public interface IVoiceModelRepository
    {
        /// <summary>The folder of a voice's files.</summary>
        string FolderOf(string voiceId);

        /// <summary>Records a voice whose files are already in <see cref="FolderOf"/>.</summary>
        void Add(VoiceModelRecord record);

        /// <summary>The voice, or null when there is none.</summary>
        VoiceModelRecord Get(string voiceId);

        /// <summary>Every voice that can be read, newest first.</summary>
        IReadOnlyList<VoiceModelRecord> List();

        /// <exception cref="InvalidOperationException">There is no such voice.</exception>
        void Rename(string voiceId, string name);

        /// <summary>Deletes the voice and its files; a voice that is not there is no error.</summary>
        void Delete(string voiceId);
    }

    /// <summary>
    /// Registered voices as VoiceModels/voice-&lt;RunId&gt;/ folders, each with its model.json, kept with
    /// ProjectData's settings files. Other folders in VoiceModels (the prototype's) are not voices of Voice Lab.
    /// </summary>
    public sealed class VoiceModelStore : IVoiceModelRepository
    {
        public const string ModelFile = "model.json";
        public const string IdPrefix = "voice-";

        private readonly string _root;
        private readonly ISystemClock _clock;

        public VoiceModelStore(string voiceModelsFolder, ISystemClock clock)
        {
            _root = voiceModelsFolder ?? throw new ArgumentNullException(nameof(voiceModelsFolder));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public string FolderOf(string voiceId)
        {
            if (!IsVoiceId(voiceId))
            {
                throw new ArgumentException($"'{voiceId}' is not a voice ID.", nameof(voiceId));
            }

            return Path.Combine(_root, voiceId);
        }

        public void Add(VoiceModelRecord record)
        {
            Store(record.Id).Save(record);
        }

        public VoiceModelRecord Get(string voiceId)
        {
            if (!IsVoiceId(voiceId) || !File.Exists(Path.Combine(FolderOf(voiceId), ModelFile)))
            {
                return null;
            }

            ConfigLoadResult<VoiceModelRecord> loaded = Store(voiceId).Load();
            return loaded.Status != ConfigLoadStatus.Recovered && !string.IsNullOrEmpty(loaded.Value.Id) ? loaded.Value : null;
        }

        public IReadOnlyList<VoiceModelRecord> List()
        {
            var voices = new List<VoiceModelRecord>();
            if (!Directory.Exists(_root))
            {
                return voices;
            }

            foreach (string folder in Directory.GetDirectories(_root))
            {
                VoiceModelRecord voice = Get(Path.GetFileName(folder));
                if (voice != null)
                {
                    voices.Add(voice);
                }
            }

            // ISO 8601 in UTC sorts as text.
            voices.Sort((a, b) => string.CompareOrdinal(b.RegisteredAtUtc, a.RegisteredAtUtc));
            return voices;
        }

        public void Rename(string voiceId, string name)
        {
            VoiceModelRecord voice = Get(voiceId) ?? throw new InvalidOperationException($"There is no voice {voiceId}.");
            voice.Name = name;
            Store(voiceId).Save(voice);
        }

        public void Delete(string voiceId)
        {
            if (IsVoiceId(voiceId) && Directory.Exists(FolderOf(voiceId)))
            {
                Directory.Delete(FolderOf(voiceId), true);
            }
        }

        /// <summary>A Voice Lab voice's folder name with no path in it. Pure.</summary>
        public static bool IsVoiceId(string voiceId)
        {
            return !string.IsNullOrEmpty(voiceId) && voiceId.StartsWith(IdPrefix, StringComparison.Ordinal)
                && voiceId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        private ConfigFileStore<VoiceModelRecord> Store(string voiceId)
        {
            return new ConfigFileStore<VoiceModelRecord>(FolderOf(voiceId), ModelFile, VoiceModelRecord.SchemaVersion, () => new VoiceModelRecord(), _clock);
        }
    }
}
