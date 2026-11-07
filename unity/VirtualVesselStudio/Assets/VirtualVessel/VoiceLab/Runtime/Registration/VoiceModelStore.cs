using System;
using System.Collections.Generic;
using System.IO;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.ProjectData.Records;

namespace VirtualVessel.VoiceLab.Registration
{
    /// <summary>
    /// Where registered voices' records are kept (Voice Lab detailed design 6.5, 8.6). The first one keeps
    /// model.json beside each voice's files; SQLite (system design 13.12) will be another, so nothing else reads
    /// the records. The voice files stay in their folders either way (system design 13.13). Every operation
    /// reports its failure as a result and leaves handling it to the caller (system design 6.31).
    /// </summary>
    public interface IVoiceModelRepository
    {
        /// <summary>The folder of a voice's files.</summary>
        /// <exception cref="ArgumentException">Not a voice ID: a bug in the caller.</exception>
        string FolderOf(string voiceId);

        /// <summary>Records a voice whose files are already in <see cref="FolderOf"/>.</summary>
        CrudResult Add(VoiceModelRecord record);

        /// <summary>NotFound when there is no such voice.</summary>
        CrudResult<VoiceModelRecord> Get(string voiceId);

        /// <summary>Every voice that can be read, newest first.</summary>
        CrudResult<IReadOnlyList<VoiceModelRecord>> List();

        CrudResult Rename(string voiceId, string name);

        /// <summary>Deletes the voice and its files; a voice that is not there succeeds.</summary>
        CrudResult Delete(string voiceId);
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

        public CrudResult Add(VoiceModelRecord record)
        {
            if (!IsVoiceId(record?.Id))
            {
                return CrudResult.Fail(CrudError.Invalid, $"'{record?.Id}' is not a voice ID.");
            }

            return CrudResult.Run(() => Store(record.Id).Save(record), $"Recording voice {record.Id}");
        }

        public CrudResult<VoiceModelRecord> Get(string voiceId)
        {
            if (!IsVoiceId(voiceId))
            {
                return CrudResult<VoiceModelRecord>.Fail(CrudError.Invalid, $"'{voiceId}' is not a voice ID.");
            }

            CrudResult<VoiceModelRecord> read = CrudResult<VoiceModelRecord>.Run(() => Load(voiceId), $"Reading voice {voiceId}");
            return read.Ok && read.Value == null ? CrudResult<VoiceModelRecord>.Fail(CrudError.NotFound, $"There is no voice {voiceId}.") : read;
        }

        public CrudResult<IReadOnlyList<VoiceModelRecord>> List()
        {
            return CrudResult<IReadOnlyList<VoiceModelRecord>>.Run(() =>
            {
                var voices = new List<VoiceModelRecord>();
                if (!Directory.Exists(_root))
                {
                    return voices;
                }

                foreach (string folder in Directory.GetDirectories(_root))
                {
                    string voiceId = Path.GetFileName(folder);
                    VoiceModelRecord voice = IsVoiceId(voiceId) ? Load(voiceId) : null;
                    if (voice != null)
                    {
                        voices.Add(voice);
                    }
                }

                // ISO 8601 in UTC sorts as text.
                voices.Sort((a, b) => string.CompareOrdinal(b.RegisteredAtUtc, a.RegisteredAtUtc));
                return voices;
            }, "Listing voices");
        }

        public CrudResult Rename(string voiceId, string name)
        {
            CrudResult<VoiceModelRecord> voice = Get(voiceId);
            if (!voice.Ok)
            {
                return voice.WithoutValue;
            }

            voice.Value.Name = name;
            return CrudResult.Run(() => Store(voiceId).Save(voice.Value), $"Renaming voice {voiceId}");
        }

        public CrudResult Delete(string voiceId)
        {
            if (!IsVoiceId(voiceId))
            {
                return CrudResult.Fail(CrudError.Invalid, $"'{voiceId}' is not a voice ID.");
            }

            return CrudResult.Run(() =>
            {
                if (Directory.Exists(FolderOf(voiceId)))
                {
                    Directory.Delete(FolderOf(voiceId), true);
                }
            }, $"Deleting voice {voiceId}");
        }

        // The voice, or null when there is none or its file could not be used.
        private VoiceModelRecord Load(string voiceId)
        {
            if (!File.Exists(Path.Combine(FolderOf(voiceId), ModelFile)))
            {
                return null;
            }

            ConfigLoadResult<VoiceModelRecord> loaded = Store(voiceId).Load();
            return loaded.Status != ConfigLoadStatus.Recovered && !string.IsNullOrEmpty(loaded.Value.Id) ? loaded.Value : null;
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
