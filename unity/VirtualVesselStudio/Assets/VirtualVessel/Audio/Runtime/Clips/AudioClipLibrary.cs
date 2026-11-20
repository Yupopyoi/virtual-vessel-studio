using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.ProjectData.Records;

namespace VirtualVessel.Audio.Clips
{
    /// <summary>audio-profile.json beside a registered file (BGM and SE detailed design 6.1).</summary>
    public sealed class AudioClipProfile
    {
        public const int SchemaVersion = 1;

        public string Name { get; set; }

        public float GainDb { get; set; }

        /// <summary>BGM only; SE keeps it false and does not use it.</summary>
        public bool Loop { get; set; }

        public double DurationSeconds { get; set; }

        public string OriginalName { get; set; }

        /// <summary>ISO 8601 in UTC.</summary>
        public string AddedAtUtc { get; set; }
    }

    /// <summary>
    /// The registered files as Audio/BGM/bgm-&lt;id&gt;/ and Audio/SE/se-&lt;id&gt;/ folders in the Data Root, each with a
    /// copy of the original file and its audio-profile.json.
    /// </summary>
    public sealed class AudioClipLibrary : IAudioClipLibrary
    {
        public const string ProfileFile = "audio-profile.json";
        public const string AudioFileName = "audio";
        public const int MaxNameLength = 60;
        public const int MaxEffectSeconds = 30;
        public const float DefaultGainDb = 0f;

        private const string BgmFolder = "BGM";
        private const string EffectFolder = "SE";
        private const string BgmPrefix = "bgm-";
        private const string EffectPrefix = "se-";

        private static readonly string[] s_extensions = { ".wav", ".mp3" };

        private readonly string _root;
        private readonly ISystemClock _clock;
        private readonly Func<string, AudioFileInfo> _probe;
        private readonly Func<string> _newId;

        /// <param name="audioFolder">The Data Root's Audio folder.</param>
        /// <param name="probe">Reads a file's length; <see cref="AudioFile.Probe"/> outside the tests.</param>
        /// <param name="newId">A new ID's unique part.</param>
        public AudioClipLibrary(string audioFolder, ISystemClock clock, Func<string, AudioFileInfo> probe, Func<string> newId)
        {
            _root = audioFolder ?? throw new ArgumentNullException(nameof(audioFolder));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _probe = probe ?? throw new ArgumentNullException(nameof(probe));
            _newId = newId ?? throw new ArgumentNullException(nameof(newId));
        }

        public event Action<AudioClipKind> Changed;

        /// <summary>Whether a registration plays now, so it is not deleted under the player. Set by the composition.</summary>
        public Func<string, bool> InUse { get; set; } = _ => false;

        public IReadOnlyList<string> FileExtensions => s_extensions;

        public CrudResult<IReadOnlyList<AudioClipRecord>> List(AudioClipKind kind)
        {
            return CrudResult<IReadOnlyList<AudioClipRecord>>.Run(() =>
            {
                var records = new List<AudioClipRecord>();
                string folder = KindFolder(kind);
                if (!Directory.Exists(folder))
                {
                    return records;
                }

                foreach (string clip in Directory.GetDirectories(folder))
                {
                    string id = Path.GetFileName(clip);
                    AudioClipRecord record = KindOf(id) == kind ? Load(id) : null;
                    if (record != null)
                    {
                        records.Add(record);
                    }
                }

                records.Sort((a, b) => a.AddedAt.CompareTo(b.AddedAt));
                return records;
            }, $"Listing {kind}");
        }

        public CrudResult<AudioClipRecord> Get(string id)
        {
            if (KindOf(id) == null)
            {
                return CrudResult<AudioClipRecord>.Fail(CrudError.Invalid, $"'{id}' is not an audio clip ID.");
            }

            CrudResult<AudioClipRecord> read = CrudResult<AudioClipRecord>.Run(() => Load(id), $"Reading audio clip {id}");
            return read.Ok && read.Value == null ? CrudResult<AudioClipRecord>.Fail(CrudError.NotFound, $"There is no audio clip {id}.") : read;
        }

        public Task<AudioClipProblem> CheckAsync(AudioClipKind kind, string sourcePath, CancellationToken cancellationToken)
        {
            return Task.Run(() => Check(kind, sourcePath).Problem, cancellationToken);
        }

        public async Task<CrudResult<AudioClipRecord>> AddAsync(AudioClipKind kind, string sourcePath, string name, CancellationToken cancellationToken)
        {
            CrudResult<AudioClipRecord> added = await Task.Run(() =>
            {
                (AudioClipProblem problem, AudioFileInfo info) = Check(kind, sourcePath);
                if (problem != AudioClipProblem.None)
                {
                    return CrudResult<AudioClipRecord>.Fail(CrudError.Invalid, $"The file cannot be registered as {kind}: {problem}.");
                }

                string clean = CleanName(string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(sourcePath) : name);
                if (clean == null)
                {
                    return CrudResult<AudioClipRecord>.Fail(CrudError.Invalid, "The name is empty or too long.");
                }

                string id = (kind == AudioClipKind.Bgm ? BgmPrefix : EffectPrefix) + _newId();
                AudioClipProfile profile = NewProfile(kind, clean, info, Path.GetFileName(sourcePath), _clock.UtcNow);
                return CrudResult<AudioClipRecord>.Run(() =>
                {
                    Copy(id, sourcePath);
                    Store(id).Save(profile);
                    return Load(id);
                }, $"Adding audio clip {id}");
            }, cancellationToken).ConfigureAwait(false);

            if (added.Ok)
            {
                Changed?.Invoke(kind);
            }

            return added;
        }

        public CrudResult<AudioClipRecord> Update(string id, AudioClipChange change)
        {
            string name = null;
            if (change.Name != null)
            {
                name = CleanName(change.Name);
                if (name == null)
                {
                    return CrudResult<AudioClipRecord>.Fail(CrudError.Invalid, "The name is empty or too long.");
                }
            }

            CrudResult<AudioClipRecord> current = Get(id);
            if (!current.Ok)
            {
                return current;
            }

            CrudResult<AudioClipRecord> updated = CrudResult<AudioClipRecord>.Run(() =>
            {
                // Loaded and saved through one store, so fields this version does not know are kept (CLAUDE.md 25).
                ConfigFileStore<AudioClipProfile> store = Store(id);
                AudioClipProfile profile = store.Load().Value;
                profile.Name = name ?? profile.Name;
                profile.GainDb = change.GainDb.HasValue ? ClampGain(change.GainDb.Value) : profile.GainDb;
                profile.Loop = change.Loop.HasValue && current.Value.Kind == AudioClipKind.Bgm ? change.Loop.Value : profile.Loop;
                store.Save(profile);
                return Load(id);
            }, $"Changing audio clip {id}");

            if (updated.Ok)
            {
                Changed?.Invoke(current.Value.Kind);
            }

            return updated;
        }

        public CrudResult Delete(string id)
        {
            AudioClipKind? kind = KindOf(id);
            if (kind == null)
            {
                return CrudResult.Fail(CrudError.Invalid, $"'{id}' is not an audio clip ID.");
            }

            // A track that plays has its file open on the deck's thread until it has faded out.
            if (InUse(id))
            {
                return CrudResult.Fail(CrudError.Busy, $"Audio clip {id} plays now.");
            }

            CrudResult deleted = CrudResult.Run(() =>
            {
                string folder = FolderOf(id);
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }, $"Deleting audio clip {id}");

            if (deleted.Ok)
            {
                Changed?.Invoke(kind.Value);
            }

            return deleted;
        }

        /// <summary>A registration's kind from its ID, or null for what is not one (no path in it). Pure.</summary>
        public static AudioClipKind? KindOf(string id)
        {
            if (string.IsNullOrEmpty(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.EndsWith(".part", StringComparison.Ordinal))
            {
                return null;
            }

            if (id.StartsWith(BgmPrefix, StringComparison.Ordinal) && id.Length > BgmPrefix.Length)
            {
                return AudioClipKind.Bgm;
            }

            if (id.StartsWith(EffectPrefix, StringComparison.Ordinal) && id.Length > EffectPrefix.Length)
            {
                return AudioClipKind.SoundEffect;
            }

            return null;
        }

        /// <summary>A name trimmed, or null when it is empty or longer than <see cref="MaxNameLength"/>. Pure.</summary>
        public static string CleanName(string name)
        {
            string trimmed = name?.Trim();
            return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxNameLength ? null : trimmed;
        }

        /// <summary>Why a file of this kind, with this extension and length, cannot be registered. Pure.</summary>
        public static AudioClipProblem ProblemOf(AudioClipKind kind, string extension, TimeSpan? duration)
        {
            if (Array.IndexOf(s_extensions, (extension ?? string.Empty).ToLowerInvariant()) < 0)
            {
                return AudioClipProblem.Unsupported;
            }

            if (duration == null)
            {
                return AudioClipProblem.Unreadable;
            }

            return kind == AudioClipKind.SoundEffect && duration.Value.TotalSeconds > MaxEffectSeconds ? AudioClipProblem.TooLong : AudioClipProblem.None;
        }

        /// <summary>A new registration's profile: BGM loops by default, SE has no loop. Pure.</summary>
        public static AudioClipProfile NewProfile(AudioClipKind kind, string name, AudioFileInfo info, string originalName, DateTimeOffset now)
        {
            return new AudioClipProfile
            {
                Name = name,
                GainDb = DefaultGainDb,
                Loop = kind == AudioClipKind.Bgm,
                DurationSeconds = info.Duration.TotalSeconds,
                OriginalName = originalName,
                AddedAtUtc = now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            };
        }

        private (AudioClipProblem Problem, AudioFileInfo Info) Check(AudioClipKind kind, string sourcePath)
        {
            string extension = Path.GetExtension(sourcePath ?? string.Empty);
            if (ProblemOf(kind, extension, TimeSpan.Zero) == AudioClipProblem.Unsupported)
            {
                return (AudioClipProblem.Unsupported, default);
            }

            AudioFileInfo info;
            try
            {
                info = _probe(sourcePath);
            }
            catch (Exception exception) when (exception is InvalidDataException || exception is AudioEngineException || CrudResult.IsStorage(exception))
            {
                return (AudioClipProblem.Unreadable, default);
            }

            return (ProblemOf(kind, extension, info.Duration), info);
        }

        private string KindFolder(AudioClipKind kind)
        {
            return Path.Combine(_root, kind == AudioClipKind.Bgm ? BgmFolder : EffectFolder);
        }

        private string FolderOf(string id)
        {
            return Path.Combine(KindFolder(KindOf(id) ?? AudioClipKind.Bgm), id);
        }

        private void Copy(string id, string sourcePath)
        {
            // Copied into a folder beside the target and then moved, so a half-copied file is never listed.
            string folder = FolderOf(id);
            string staging = folder + ".part";
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }

            Directory.CreateDirectory(staging);
            File.Copy(sourcePath, Path.Combine(staging, AudioFileName + Path.GetExtension(sourcePath).ToLowerInvariant()));
            Directory.Move(staging, folder);
        }

        // The registration, or null when there is none or its files could not be used.
        private AudioClipRecord Load(string id)
        {
            string folder = FolderOf(id);
            if (!File.Exists(Path.Combine(folder, ProfileFile)))
            {
                return null;
            }

            string file = AudioFilePath(folder);
            ConfigLoadResult<AudioClipProfile> loaded = Store(id).Load();
            if (file == null || loaded.Status == ConfigLoadStatus.Recovered)
            {
                return null;
            }

            AudioClipProfile profile = loaded.Value;
            AudioClipKind kind = KindOf(id).Value;
            DateTimeOffset.TryParse(profile.AddedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset added);
            return new AudioClipRecord(
                id,
                kind,
                CleanName(profile.Name) ?? Path.GetFileNameWithoutExtension(profile.OriginalName ?? id),
                ClampGain(profile.GainDb),
                kind == AudioClipKind.Bgm && profile.Loop,
                TimeSpan.FromSeconds(double.IsNaN(profile.DurationSeconds) ? 0 : Math.Max(0, profile.DurationSeconds)),
                profile.OriginalName,
                added,
                file);
        }

        // The copied file: audio and the extension it was registered with.
        private static string AudioFilePath(string folder)
        {
            foreach (string extension in s_extensions)
            {
                string path = Path.Combine(folder, AudioFileName + extension);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        private ConfigFileStore<AudioClipProfile> Store(string id)
        {
            return new ConfigFileStore<AudioClipProfile>(FolderOf(id), ProfileFile, AudioClipProfile.SchemaVersion, () => new AudioClipProfile(), _clock);
        }

        private static float ClampGain(float db)
        {
            return float.IsNaN(db) ? DefaultGainDb : Math.Max(AudioSourceOptions.MinGainDb, Math.Min(AudioSourceOptions.MaxGainDb, db));
        }
    }
}
