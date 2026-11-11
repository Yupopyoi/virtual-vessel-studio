using System;
using System.IO;
using System.Text.RegularExpressions;

namespace VirtualVessel.VoiceLab.Activation
{
    /// <summary>
    /// Voice Lab's terms of use and disclaimer (Voice Lab detailed design 8.2; decision record 2026-11-01): Markdown files
    /// in Japanese and English beside the scripts (terms/voice-lab-terms.&lt;language&gt;.md), the version in their first
    /// line. Agreeing to a version is kept in the activation record; a new version is agreed to again before making voices.
    /// </summary>
    public sealed class VoiceTerms
    {
        public const string Folder = "terms";

        private static readonly Regex s_version = new Regex(@"^<!--\s*version:\s*(?<v>[0-9A-Za-z._-]+)\s*-->");

        private VoiceTerms(string version, string text)
        {
            Version = version;
            Text = text;
        }

        public string Version { get; }

        /// <summary>The whole text, as Markdown.</summary>
        public string Text { get; }

        /// <summary>The terms file of a language beside the scripts. Pure.</summary>
        public static string FileOf(string scripts, string language)
        {
            return Path.Combine(scripts, Folder, $"voice-lab-terms.{language}.md");
        }

        /// <summary>The terms from a file's text; null when the first line names no version. Pure.</summary>
        public static VoiceTerms Parse(string text)
        {
            Match match = s_version.Match(text ?? string.Empty);
            return match.Success ? new VoiceTerms(match.Groups["v"].Value, text) : null;
        }

        /// <summary>The terms in a language, else English; null when neither is readable.</summary>
        public static VoiceTerms Load(string scripts, string language)
        {
            foreach (string candidate in new[] { language, "en" })
            {
                string path = FileOf(scripts, candidate ?? "en");
                if (File.Exists(path))
                {
                    VoiceTerms terms = Parse(File.ReadAllText(path));
                    if (terms != null)
                    {
                        return terms;
                    }
                }
            }

            return null;
        }

        /// <summary>Whether the record shows this version agreed to. Pure.</summary>
        public static bool IsAgreed(VoiceActivationRecord record, string version)
        {
            return record != null && version != null && string.Equals(record.TermsVersion, version, StringComparison.Ordinal);
        }
    }
}
