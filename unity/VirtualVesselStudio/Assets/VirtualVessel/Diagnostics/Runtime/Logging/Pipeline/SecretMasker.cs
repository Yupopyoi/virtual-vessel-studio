using System;
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

        // Every pattern below needs one of these words to match. Checking for them first keeps
        // ordinary messages off the regexes, which cost tens of microseconds per call on Unity's
        // runtime and run on the caller's thread. Lower-case ASCII only; keep in sync with the patterns.
        private static readonly string[] s_triggerWords =
        {
            "key", "pass", "pwd", "token", "secret", "credential", "bearer", "rtmp",
        };

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
            if (string.IsNullOrEmpty(text) || !ContainsTriggerWord(text))
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
            if (string.IsNullOrEmpty(key) || !ContainsTriggerWord(key))
            {
                return false;
            }

            return s_sensitiveKeyName.IsMatch(key) || s_sensitiveKeyName.IsMatch(RemoveSeparators(key));
        }

        /// <summary>
        /// Returns whether <paramref name="text"/> contains a trigger word, ignoring ASCII case.
        /// </summary>
        /// <remarks>
        /// Hand-written because <c>IndexOf(..., OrdinalIgnoreCase)</c> takes over a microsecond per
        /// call on Unity's runtime. Only ASCII letters are folded; the regexes would match non-ASCII
        /// case variants (such as the Kelvin sign), which is not a realistic way to write a key name.
        /// </remarks>
        internal static bool ContainsTriggerWord(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                // Setting bit 5 lower-cases ASCII letters and never turns a non-letter into a letter.
                char c = (char)(text[i] | 0x20);
                foreach (string word in s_triggerWords)
                {
                    if (word[0] == c && MatchesAt(text, i, word))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool MatchesAt(string text, int start, string word)
        {
            if (start + word.Length > text.Length)
            {
                return false;
            }

            for (int j = 1; j < word.Length; j++)
            {
                if ((char)(text[start + j] | 0x20) != word[j])
                {
                    return false;
                }
            }

            return true;
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
