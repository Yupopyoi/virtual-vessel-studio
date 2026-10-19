using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// The avatars' folders in the Data Root (avatar detailed design 6.1):
    /// <c>Avatars/&lt;AvatarId&gt;/</c> with the copied model, avatar-profile.json, and the thumbnail.
    /// Registration builds a folder under <c>Avatars/.importing/</c> and moves it in only when complete.
    /// </summary>
    /// <remarks>
    /// Every call touches the file system; call it off the main thread where it may take long, as
    /// copying does. Only folders named by an avatar id are ever deleted, so a folder the user put
    /// there is left alone.
    /// </remarks>
    public sealed class AvatarStore
    {
        public const string ImportingFolder = ".importing";
        public const string ModelFileName = "model";
        public const string ThumbnailFileName = "thumbnail";

        private const int CopyBufferBytes = 1 << 20;

        private readonly ISystemClock _clock;

        public AvatarStore(string directory, ISystemClock clock)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("Directory must not be empty.", nameof(directory));
            }

            Directory = directory;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>DataRoot/Avatars.</summary>
        public string Directory { get; }

        /// <summary>A new avatar id: a GUID in the N format, never a name, so ids are unique and valid file names (6.1).</summary>
        public static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }

        /// <summary>Whether <paramref name="name"/> is an avatar id, and so a folder this store owns. Pure.</summary>
        public static bool IsId(string name)
        {
            return name != null && name.Length == 32 && Guid.TryParseExact(name, "N", out _);
        }

        /// <summary>
        /// Whether a format from a profile can be part of a file name: a few lower-case letters and
        /// digits, so a hand-edited profile cannot point the model path elsewhere. Pure.
        /// </summary>
        public static bool IsFormatName(string format)
        {
            if (string.IsNullOrEmpty(format) || format.Length > 8)
            {
                return false;
            }

            foreach (char c in format)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The model's file name for a format, such as model.vrm. Pure.</summary>
        public static string ModelFile(string format)
        {
            return ModelFileName + "." + format;
        }

        public string FolderOf(string avatarId)
        {
            return Path.Combine(Directory, Checked(avatarId));
        }

        public string ImportFolderOf(string avatarId)
        {
            return Path.Combine(Directory, ImportingFolder, Checked(avatarId));
        }

        /// <summary>The registered avatars, by reading every avatar folder; unreadable ones are listed as not usable.</summary>
        public IReadOnlyList<AvatarEntry> List(ICollection<string> warnings)
        {
            var entries = new List<AvatarEntry>();
            if (!System.IO.Directory.Exists(Directory))
            {
                return entries;
            }

            foreach (string folder in System.IO.Directory.GetDirectories(Directory))
            {
                string id = Path.GetFileName(folder);
                if (IsId(id))
                {
                    entries.Add(Read(id, folder, warnings));
                }
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.Profile?.ImportedAt ?? string.Empty, b.Profile?.ImportedAt ?? string.Empty));
            return entries;
        }

        /// <summary>
        /// Removes what an interrupted registration left behind. Everything under .importing belongs to a
        /// registration of this application that never finished.
        /// </summary>
        public void CleanImports()
        {
            string importing = Path.Combine(Directory, ImportingFolder);
            if (System.IO.Directory.Exists(importing))
            {
                System.IO.Directory.Delete(importing, recursive: true);
            }
        }

        /// <summary>
        /// Copies <paramref name="sourcePath"/> into a new import folder as the model and returns the
        /// SHA-256 of what was copied. One pass reads the file for both.
        /// </summary>
        public string CopyIn(string sourcePath, string avatarId, string format, CancellationToken cancellationToken)
        {
            string folder = ImportFolderOf(avatarId);
            System.IO.Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, ModelFile(format));
            using (var hash = SHA256.Create())
            using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes, FileOptions.SequentialScan))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferBytes))
            {
                var buffer = new byte[CopyBufferBytes];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    hash.TransformBlock(buffer, 0, read, null, 0);
                    output.Write(buffer, 0, read);
                }

                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                output.Flush(flushToDisk: true);
                return AvatarFileCheck.ToHex(hash.Hash);
            }
        }

        /// <summary>Writes the profile and the thumbnail into the import folder.</summary>
        public void WriteImport(string avatarId, AvatarProfile profile, AvatarThumbnail thumbnail)
        {
            string folder = ImportFolderOf(avatarId);
            if (thumbnail != null)
            {
                File.WriteAllBytes(Path.Combine(folder, profile.Thumbnail), thumbnail.Data);
            }

            CreateProfileStore(folder).Save(profile);
        }

        /// <summary>Moves a complete import folder in, which registers the avatar.</summary>
        public AvatarEntry Commit(string avatarId)
        {
            string target = FolderOf(avatarId);
            System.IO.Directory.Move(ImportFolderOf(avatarId), target);
            return Read(avatarId, target, null);
        }

        public void DeleteImport(string avatarId)
        {
            DeleteFolder(ImportFolderOf(avatarId));
        }

        public void Delete(string avatarId)
        {
            DeleteFolder(FolderOf(avatarId));
        }

        public string ModelPathOf(AvatarEntry entry)
        {
            return Path.Combine(FolderOf(entry.Id), ModelFile(entry.Profile.Format));
        }

        private static string Checked(string avatarId)
        {
            // Ids come from settings files too; one that is not an id could point outside the folder.
            if (!IsId(avatarId))
            {
                throw new ArgumentException($"'{avatarId}' is not an avatar id.", nameof(avatarId));
            }

            return avatarId;
        }

        private static void DeleteFolder(string folder)
        {
            if (System.IO.Directory.Exists(folder))
            {
                System.IO.Directory.Delete(folder, recursive: true);
            }
        }

        private ConfigFileStore<AvatarProfile> CreateProfileStore(string folder)
        {
            return new ConfigFileStore<AvatarProfile>(folder, AvatarProfile.FileName, AvatarProfile.SchemaVersion, () => new AvatarProfile(), _clock);
        }

        private AvatarEntry Read(string id, string folder, ICollection<string> warnings)
        {
            ConfigLoadResult<AvatarProfile> loaded = CreateProfileStore(folder).Load();
            foreach (string warning in loaded.Warnings)
            {
                warnings?.Add(warning);
            }

            AvatarProfile profile = loaded.Value;
            bool readable = loaded.Status != ConfigLoadStatus.Created && loaded.Status != ConfigLoadStatus.Recovered
                && profile.AvatarId == id
                && IsFormatName(profile.Format)
                && File.Exists(Path.Combine(folder, ModelFile(profile.Format)));
            if (!readable)
            {
                warnings?.Add($"Avatar {id} cannot be loaded: its profile or model is missing or unreadable ({loaded.Status}).");
                return new AvatarEntry(id, null, null);
            }

            string thumbnail = ThumbnailPath(folder, profile.Thumbnail);
            return new AvatarEntry(id, profile, thumbnail != null && File.Exists(thumbnail) ? thumbnail : null);
        }

        /// <summary>The thumbnail's path, or null for a missing or unsafe name from the profile. Pure.</summary>
        internal static string ThumbnailPath(string folder, string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || fileName != Path.GetFileName(fileName) || !fileName.StartsWith(ThumbnailFileName + ".", StringComparison.Ordinal))
            {
                return null;
            }

            return Path.Combine(folder, fileName);
        }
    }
}
