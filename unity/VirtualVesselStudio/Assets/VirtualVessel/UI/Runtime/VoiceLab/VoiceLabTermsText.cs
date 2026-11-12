using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>A part of the terms' Markdown as the screen shows it.</summary>
    public enum VoiceLabTermsBlockKind
    {
        Title,
        Heading,
        Paragraph,

        /// <summary>A numbered item; nested ones are indented.</summary>
        Item,

        /// <summary>A row of a table, its cells joined.</summary>
        Row,
    }

    /// <summary>
    /// The terms' Markdown as plain blocks for the screen (screen spec voice-lab 2.1): the files the app ships are shown
    /// as headings, paragraphs, numbered items, and table rows, without a Markdown library.
    /// </summary>
    public static class VoiceLabTermsText
    {
        private static readonly Regex s_escape = new Regex(@"\\([\\`*_{}\[\]()#+\-.!|])", RegexOptions.CultureInvariant);

        /// <summary>
        /// The blocks: HTML comments and table rules left out, numbered items with their numbers, escapes and bold marks
        /// removed, and the lines of a paragraph joined. Pure.
        /// </summary>
        public static List<(VoiceLabTermsBlockKind Kind, int Indent, string Text)> Blocks(string markdown)
        {
            var blocks = new List<(VoiceLabTermsBlockKind, int, string)>();
            var paragraph = new StringBuilder();

            void EndParagraph()
            {
                if (paragraph.Length > 0)
                {
                    blocks.Add((VoiceLabTermsBlockKind.Paragraph, 0, paragraph.ToString()));
                    paragraph.Clear();
                }
            }

            foreach (string raw in (markdown ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = raw.Trim();
                int indent = (raw.Length - raw.TrimStart(' ').Length) / 3;
                if (trimmed.Length == 0 || trimmed.StartsWith("<!--", StringComparison.Ordinal))
                {
                    EndParagraph();
                    continue;
                }

                if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                {
                    EndParagraph();
                    blocks.Add((VoiceLabTermsBlockKind.Title, 0, Plain(trimmed.Substring(2))));
                }
                else if (trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    EndParagraph();
                    blocks.Add((VoiceLabTermsBlockKind.Heading, 0, Plain(trimmed.TrimStart('#').Trim())));
                }
                else if (trimmed.StartsWith("|", StringComparison.Ordinal))
                {
                    EndParagraph();
                    string[] cells = trimmed.Trim('|').Split('|');
                    if (Array.TrueForAll(cells, c => c.Trim().Trim('-', ':').Length == 0))
                    {
                        continue;
                    }

                    blocks.Add((VoiceLabTermsBlockKind.Row, 0, Plain(string.Join(" ／ ", Array.ConvertAll(cells, c => c.Trim())))));
                }
                else if (IsNumberedItem(trimmed) || trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    EndParagraph();
                    string text = trimmed.StartsWith("- ", StringComparison.Ordinal) ? "・" + trimmed.Substring(2) : trimmed;
                    blocks.Add((VoiceLabTermsBlockKind.Item, indent, Plain(text)));
                }
                else
                {
                    paragraph.Append(Plain(trimmed));
                }
            }

            EndParagraph();
            return blocks;
        }

        // "1. text", not the escaped "1\." that starts a paragraph.
        private static bool IsNumberedItem(string line)
        {
            int digits = 0;
            while (digits < line.Length && char.IsDigit(line[digits]))
            {
                digits++;
            }

            return digits > 0 && digits + 1 < line.Length && line[digits] == '.' && line[digits + 1] == ' ';
        }

        private static string Plain(string text)
        {
            // Markdown's escapes, such as "1\." for a number that is not a list; other backslashes, as in a path, stay.
            return s_escape.Replace(text.Replace("**", string.Empty), "$1");
        }
    }
}
