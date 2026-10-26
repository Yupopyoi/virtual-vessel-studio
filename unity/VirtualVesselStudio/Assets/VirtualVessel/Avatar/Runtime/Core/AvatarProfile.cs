using System.Collections.Generic;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// One registered avatar's avatar-profile.json (avatar detailed design 6.3). Property names become
    /// the camelCase JSON field names. Fields this version does not know, such as later settings, are
    /// kept by the store.
    /// </summary>
    public sealed class AvatarProfile
    {
        public const int SchemaVersion = 1;
        public const string FileName = "avatar-profile.json";

        public string AvatarId { get; set; }

        /// <summary>The model's name from the file at registration; the user may change it.</summary>
        public string DisplayName { get; set; }

        public string Format { get; set; }

        public string FormatVersion { get; set; }

        public bool Converted { get; set; }

        /// <summary>The picked file's name, for reference only; never used to load (system design 7.11).</summary>
        public string SourceFileName { get; set; }

        public string Sha256 { get; set; }

        /// <summary>UTC, ISO 8601.</summary>
        public string ImportedAt { get; set; }

        /// <summary>The thumbnail's file name in the avatar's folder, or null when there is none.</summary>
        public string Thumbnail { get; set; }

        public AvatarLicense License { get; set; } = new AvatarLicense();

        public AvatarProfile Clone()
        {
            var copy = (AvatarProfile)MemberwiseClone();
            copy.License = License?.Clone() ?? new AvatarLicense();
            return copy;
        }
    }

    /// <summary>The avatar to use, as written in avatar-settings.json (avatar detailed design 10).</summary>
    public sealed class AvatarSettings
    {
        public const int SchemaVersion = 1;
        public const string FileName = "avatar-settings.json";

        /// <summary>No avatar is shown when null.</summary>
        public string CurrentAvatarId { get; set; }

        /// <summary>
        /// How strongly the user's face moves the avatar (avatar detailed design 7.4). The user's, not an
        /// avatar's, so it stays when the avatar changes. Files written before it existed read the defaults.
        /// </summary>
        public FaceTuning Face { get; set; } = new FaceTuning();

        public AvatarSettings Clone()
        {
            var copy = (AvatarSettings)MemberwiseClone();
            copy.Face = Face?.Clone() ?? new FaceTuning();
            return copy;
        }
    }

    /// <summary>A registered avatar as listed for the user.</summary>
    public sealed class AvatarEntry
    {
        public AvatarEntry(string id, AvatarProfile profile, string thumbnailPath)
        {
            Id = id;
            Profile = profile;
            ThumbnailPath = thumbnailPath;
        }

        public string Id { get; }

        /// <summary>Null when avatar-profile.json could not be read; the avatar cannot be loaded then (6.1).</summary>
        public AvatarProfile Profile { get; }

        public bool IsUsable => Profile != null;

        /// <summary>The thumbnail's full path, or null when there is none.</summary>
        public string ThumbnailPath { get; }

        public string DisplayName => Profile?.DisplayName ?? string.Empty;
    }

    internal static class AvatarEntries
    {
        /// <summary>The registered avatar with <paramref name="sha256"/>, or null. Pure.</summary>
        public static AvatarEntry FindBySha256(IReadOnlyList<AvatarEntry> entries, string sha256)
        {
            foreach (AvatarEntry entry in entries)
            {
                if (entry.Profile?.Sha256 != null && string.Equals(entry.Profile.Sha256, sha256, System.StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>The avatar with <paramref name="id"/>, or null. Pure.</summary>
        public static AvatarEntry Find(IReadOnlyList<AvatarEntry> entries, string id)
        {
            if (id == null)
            {
                return null;
            }

            foreach (AvatarEntry entry in entries)
            {
                if (entry.Id == id)
                {
                    return entry;
                }
            }

            return null;
        }
    }
}
