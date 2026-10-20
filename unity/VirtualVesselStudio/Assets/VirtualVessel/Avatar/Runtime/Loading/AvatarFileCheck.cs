using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// Checks done on a picked file before anything parses it (avatar detailed design 6.2, 13): model
    /// files are untrusted input. Pure.
    /// </summary>
    public static class AvatarFileCheck
    {
        /// <summary>Default size limit: 1 GB, far above real avatars (tens of MB).</summary>
        public const long MaxBytes = 1L << 30;

        /// <summary>Bytes needed by <see cref="Check"/>.</summary>
        public const int HeaderLength = 4;

        // A glTF binary (.glb, and so .vrm) starts with the ASCII magic "glTF" (glTF 2.0 spec, 4.4.2).
        private static readonly byte[] s_glbMagic = { 0x67, 0x6C, 0x54, 0x46 };

        /// <summary>
        /// The problem with a file of <paramref name="extension"/> and <paramref name="length"/> bytes that
        /// starts with <paramref name="header"/>, or <see cref="AvatarProblem.None"/>.
        /// </summary>
        public static AvatarProblem Check(string extension, long length, byte[] header, IReadOnlyList<string> extensions, long maxBytes = MaxBytes)
        {
            if (extensions == null)
            {
                throw new ArgumentNullException(nameof(extensions));
            }

            string normalized = extension?.ToLowerInvariant();
            bool known = false;
            foreach (string candidate in extensions)
            {
                known |= candidate == normalized;
            }

            if (!known)
            {
                return AvatarProblem.NotVrm;
            }

            if (length > maxBytes)
            {
                return AvatarProblem.TooLarge;
            }

            if (header == null || header.Length < HeaderLength)
            {
                return AvatarProblem.NotVrm;
            }

            for (int i = 0; i < s_glbMagic.Length; i++)
            {
                if (header[i] != s_glbMagic[i])
                {
                    return AvatarProblem.NotVrm;
                }
            }

            return AvatarProblem.None;
        }

        /// <summary>Lower-case hexadecimal of a hash. Pure.</summary>
        public static string ToHex(byte[] hash)
        {
            var text = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
            {
                text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return text.ToString();
        }
    }
}
