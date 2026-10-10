using System.Text;
using System.Text.RegularExpressions;

namespace VirtualVessel.Diagnostics.Logging.Pipeline
{
    /// <summary>
    /// Replaces values that look like secrets with a fixed mask.
    /// </summary>
    /// <remarks>
    /// This is a safety net only. Modules remain responsible for never logging secrets
    /// (system design 5.23, CLAUDE.md 20); adding patterns here is not a reason to relax that rule.
    /// </remarks>
    internal static class SecretMasker
    {
        public const string Mask = "********";

        private const string SensitiveKeyPattern =
            @"stream[\s_-]?key|password|passwd|pwd|(?:access[\s_-]?|refresh[\s_-]?|auth[\s_-]?)?token|api[\s_-]?key|client[\s_-]?secret|secret|credential";

        private static readonly Regex s_bearer = new Regex(
            @"(authorization\s*[:=]\s*bearer\s+)[^\s""',;]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // No leading word boundary, so prefixed keys such as "userPassword=" or "my_token=" are also
        // masked. Over-masking an unrelated value is acceptable for a safety net.
        private static readonly Regex s_keyValue = new Regex(
            @"(" + SensitiveKeyPattern + @")(""?\s*[:=]\s*)(""?)([^\s""',;&]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // rtmp(s)://host/app/<stream key>: the last path segment is the stream key.
        private static readonly Regex s_rtmpUrl = new Regex(
            @"(rtmps?://[^\s""']+/)([^/\s""']+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex s_sensitiveKeyName = new Regex(
            "^(?:" + SensitiveKeyPattern + ")$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Returns <paramref name="text"/> with secret-looking values masked. Returns the same instance
        /// when nothing matched.
        /// </summary>
        public static string MaskText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            string result = s_bearer.Replace(text, "$1" + Mask);
            result = s_keyValue.Replace(result, "$1$2$3" + Mask);
            result = s_rtmpUrl.Replace(result, "$1" + Mask);
            return result;
        }

        /// <summary>
        /// Returns whether a property key names a secret, ignoring separators such as "_" and "-".
        /// </summary>
        public static bool IsSensitiveKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            return s_sensitiveKeyName.IsMatch(key) || s_sensitiveKeyName.IsMatch(RemoveSeparators(key));
        }

        private static string RemoveSeparators(string key)
        {
            var builder = new StringBuilder(key.Length);
            foreach (char c in key)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }
    }
}
