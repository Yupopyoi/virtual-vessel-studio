using System;

namespace VirtualVessel.Avatar
{
    /// <summary>What a model file says about itself, read while loading.</summary>
    public sealed class AvatarModelInfo
    {
        public AvatarModelInfo(string name, string format, string formatVersion, bool converted, AvatarLicense license, AvatarThumbnail thumbnail)
        {
            Name = name ?? string.Empty;
            Format = format ?? throw new ArgumentNullException(nameof(format));
            FormatVersion = formatVersion ?? string.Empty;
            Converted = converted;
            License = license ?? new AvatarLicense();
            Thumbnail = thumbnail;
        }

        /// <summary>The model's name; empty when the file has none.</summary>
        public string Name { get; }

        /// <summary>Such as "vrm".</summary>
        public string Format { get; }

        /// <summary>The file's own version, such as "1.0" or "0.x", before any conversion.</summary>
        public string FormatVersion { get; }

        /// <summary>Whether the model was converted while loading, as VRM 0.x is (avatar detailed design 6.8).</summary>
        public bool Converted { get; }

        public AvatarLicense License { get; }

        /// <summary>The image in the file, or null when it has none.</summary>
        public AvatarThumbnail Thumbnail { get; }
    }

    /// <summary>An image file's bytes as stored in the model, so it is saved without decoding it again.</summary>
    public sealed class AvatarThumbnail
    {
        public AvatarThumbnail(byte[] data, string extension)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Extension = extension ?? throw new ArgumentNullException(nameof(extension));
        }

        public byte[] Data { get; }

        /// <summary>".png" or ".jpg".</summary>
        public string Extension { get; }

        /// <summary>The file extension of an image MIME type, or null for a type that is not shown. Pure.</summary>
        public static string ExtensionOf(string mimeType)
        {
            switch (mimeType?.Trim().ToLowerInvariant())
            {
                case "image/png":
                    return ".png";
                case "image/jpeg":
                case "image/jpg":
                    return ".jpg";
                default:
                    return null;
            }
        }
    }
}
