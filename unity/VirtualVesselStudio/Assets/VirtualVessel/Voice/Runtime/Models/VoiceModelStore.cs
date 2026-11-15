using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Voice;

namespace VirtualVessel.Voice.Models
{
    /// <summary>
    /// Where registered voices are kept (Voice detailed design 6.1; system design 9.7). Voice owns them, so the
    /// stream keeps its voices when Voice Lab is broken or gone (system design 13.23); Voice Lab hands a built voice
    /// over through <see cref="Add"/>. The first implementation keeps model.json beside each voice's files; SQLite
    /// (system design 13.12) will be another, so nothing else reads the records. Every operation reports its failure
    /// as a result and leaves handling it to the caller (system design 6.31).
    /// </summary>
    public interface IVoiceModelRepository
    {
        /// <summary>The folder of a voice's files.</summary>
        /// <exception cref="ArgumentException">Not a voice ID: a bug in the caller.</exception>
        string FolderOf(string voiceId);

        /// <summary>
        /// Copies the voice's files in and records it. Invalid when the ID or a runtime file is wrong, StorageFailed
        /// when the files could not be copied. A voice with the same ID is replaced.
        /// </summary>
        CrudResult<VoiceModelRecord> Add(NewVoiceModel voice);

        /// <summary>NotFound when there is no such voice.</summary>
        CrudResult<VoiceModelRecord> Get(string voiceId);

        /// <summary>Every voice that can be read, newest first.</summary>
        CrudResult<IReadOnlyList<VoiceModelRecord>> List();

        CrudResult Rename(string voiceId, string name);

        /// <summary>Keeps a voice's finish (Voice detailed design 8.6). NotFound without the voice.</summary>
        CrudResult SetTone(string voiceId, VoiceTone tone);

        /// <summary>Deletes the voice and its files; a voice that is not there succeeds.</summary>
        CrudResult Delete(string voiceId);
    }

    /// <summary>
    /// Registered voices as VoiceModels/voice-&lt;RunId&gt;/ folders, each with its model.json, kept with
    /// ProjectData's settings files. Other folders in VoiceModels (the prototype's) are not registered voices.
    /// </summary>
    public sealed class VoiceModelStore : IVoiceModelRepository
    {
        public const string ModelFile = "model.json";
        public const string ManifestFile = "voice.json";
        public const string IdPrefix = "voice-";

        /// <summary>What the conversion reads: the generator, the speaker vector, and their shape.</summary>
        public static readonly IReadOnlyList<string> RuntimeFiles = new[] { "voice.sentis", "voice.spk", ManifestFile };

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

        public CrudResult<VoiceModelRecord> Add(NewVoiceModel voice)
        {
            string problem = Problem(voice);
            if (problem != null)
            {
                return CrudResult<VoiceModelRecord>.Fail(CrudError.Invalid, problem);
            }

            foreach (string file in RuntimeFiles)
            {
                if (!File.Exists(Path.Combine(voice.SourceFolder, file)))
                {
                    return CrudResult<VoiceModelRecord>.Fail(CrudError.Invalid, $"Voice {voice.Id} has no {file}.");
                }
            }

            return CrudResult<VoiceModelRecord>.Run(() =>
            {
                Copy(voice);
                int medianPitch = MedianPitch(File.ReadAllText(Path.Combine(FolderOf(voice.Id), ManifestFile)));
                VoiceModelRecord record = Record(voice, medianPitch, _clock.UtcNow);
                // Recorded once the files are in place; until then the voice is not listed.
                Store(voice.Id).Save(record);
                return record;
            }, $"Adding voice {voice.Id}");
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

            return CrudResult.Run(() =>
            {
                // Loaded and saved through one store: it keeps the fields this version does not know only from its
                // own last load, so a fresh store would drop them (CLAUDE.md 25).
                ConfigFileStore<VoiceModelRecord> store = Store(voiceId);
                VoiceModelRecord record = store.Load().Value;
                record.Name = name;
                store.Save(record);
            }, $"Renaming voice {voiceId}");
        }

        public CrudResult SetTone(string voiceId, VoiceTone tone)
        {
            CrudResult<VoiceModelRecord> voice = Get(voiceId);
            if (!voice.Ok)
            {
                return voice.WithoutValue;
            }

            return CrudResult.Run(() =>
            {
                // One store for the load and the save, so fields this version does not know are kept (CLAUDE.md 25).
                ConfigFileStore<VoiceModelRecord> store = Store(voiceId);
                VoiceModelRecord record = store.Load().Value;
                record.Tone = ToneValues.From(tone);
                store.Save(record);
            }, $"Keeping the finish of voice {voiceId}");
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

        /// <summary>A registered voice's folder name with no path in it. Pure.</summary>
        public static bool IsVoiceId(string voiceId)
        {
            return !string.IsNullOrEmpty(voiceId) && voiceId.StartsWith(IdPrefix, StringComparison.Ordinal)
                && voiceId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        /// <summary>What is wrong with a voice to add, apart from its files being there; null when nothing is. Pure.</summary>
        public static string Problem(NewVoiceModel voice)
        {
            if (voice == null || !IsVoiceId(voice.Id))
            {
                return $"'{voice?.Id}' is not a voice ID.";
            }

            if (string.IsNullOrEmpty(voice.SourceFolder))
            {
                return $"Voice {voice.Id} has no folder to copy from.";
            }

            // A file name only: the copy must not reach outside the source or the voice's folder.
            string bad = voice.Files.FirstOrDefault(file => string.IsNullOrEmpty(file) || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || file == "." || file == "..");
            if (bad != null || voice.Files.Contains(ModelFile))
            {
                return $"Voice {voice.Id} lists a file it cannot copy: '{bad ?? ModelFile}'.";
            }

            string missing = RuntimeFiles.FirstOrDefault(file => !voice.Files.Contains(file));
            return missing == null ? null : $"Voice {voice.Id} does not list {missing}.";
        }

        /// <summary>The median pitch export_voice.py wrote in voice.json; 0 when it is not there. Pure.</summary>
        public static int MedianPitch(string voiceJson)
        {
            try
            {
                return ConfigDocument.Parse(voiceJson).Get("medianPitchHz", 0);
            }
            catch (FormatException)
            {
                return 0;
            }
        }

        /// <summary>model.json's contents. Pure.</summary>
        public static VoiceModelRecord Record(NewVoiceModel voice, int medianPitchHz, DateTimeOffset now)
        {
            return new VoiceModelRecord
            {
                Id = voice.Id,
                Name = voice.Name,
                RunId = voice.RunId,
                RegisteredAtUtc = now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                MedianPitchHz = medianPitchHz,
                Versions = new Dictionary<string, string>(voice.Versions.ToDictionary(pair => pair.Key, pair => pair.Value)),
                Tone = voice.Tone.IsFlat ? null : ToneValues.From(voice.Tone),
            };
        }

        private void Copy(NewVoiceModel voice)
        {
            string folder = FolderOf(voice.Id);
            // Copied into a folder beside the target and then moved, so the conversion never sees half a voice.
            string staging = folder + ".part";
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }

            Directory.CreateDirectory(staging);
            foreach (string file in voice.Files)
            {
                File.Copy(Path.Combine(voice.SourceFolder, file), Path.Combine(staging, file));
            }

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            Directory.Move(staging, folder);
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

        private ConfigFileStore<VoiceModelRecord> Store(string voiceId)
        {
            return new ConfigFileStore<VoiceModelRecord>(FolderOf(voiceId), ModelFile, VoiceModelRecord.SchemaVersion, () => new VoiceModelRecord(), _clock);
        }
    }
}
