using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine.UIElements;

namespace VirtualVessel.UI.Controls
{
    /// <summary>A part of a Markdown file as a screen shows it.</summary>
    public enum MarkdownBlockKind
    {
        Title,
        Heading,
        Paragraph,

        /// <summary>A numbered or bulleted item; nested ones are indented.</summary>
        Item,

        /// <summary>A row of a table, its cells joined.</summary>
        Row,
    }

    /// <summary>
    /// The Markdown files the app ships (Voice Lab's terms, license files) as plain blocks for the screen: headings,
    /// paragraphs, items, and table rows, without a Markdown library and without its marks (the user's review,
    /// 2026-11-15: the credits showed the files' Markdown as written).
    /// </summary>
    public static class MarkdownText
    {
        private static readonly Regex s_escape = new Regex(@"\\([\\`*_{}\[\]()#+\-.!|])", RegexOptions.CultureInvariant);
        private static readonly Regex s_link = new Regex(@"\[([^\]]+)\]\(([^)\s]+)\)", RegexOptions.CultureInvariant);
        private static readonly Regex s_autoLink = new Regex(@"<((?:https?|mailto):[^>\s]+)>", RegexOptions.CultureInvariant);

        /// <summary>Whether a file is shown as Markdown, by its extension. Pure.</summary>
        public static bool IsMarkdown(string path)
        {
            return path != null && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The blocks: HTML comments and table rules left out, items with their numbers or a bullet, escapes, bold and
        /// code marks removed, links as their text and address, and the lines of a paragraph joined. Pure.
        /// </summary>
        public static List<(MarkdownBlockKind Kind, int Indent, string Text)> Blocks(string markdown)
        {
            var blocks = new List<(MarkdownBlockKind, int, string)>();
            var paragraph = new StringBuilder();
            bool inComment = false;

            void EndParagraph()
            {
                if (paragraph.Length > 0)
                {
                    blocks.Add((MarkdownBlockKind.Paragraph, 0, paragraph.ToString()));
                    paragraph.Clear();
                }
            }

            foreach (string raw in (markdown ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = raw.Trim();
                // A nested item: the terms indent by 3 spaces, other files by 2 or 4.
                int indent = (raw.Length - raw.TrimStart(' ').Length + 1) / 3;
                if (inComment || trimmed.StartsWith("<!--", StringComparison.Ordinal))
                {
                    // A comment may run over several lines.
                    inComment = !trimmed.Contains("-->");
                    EndParagraph();
                    continue;
                }

                if (trimmed.Length == 0)
                {
                    EndParagraph();
                    continue;
                }

                if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                {
                    EndParagraph();
                    blocks.Add((MarkdownBlockKind.Title, 0, Plain(trimmed.Substring(2))));
                }
                else if (trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    EndParagraph();
                    blocks.Add((MarkdownBlockKind.Heading, 0, Plain(trimmed.TrimStart('#').Trim())));
                }
                else if (trimmed.StartsWith("|", StringComparison.Ordinal))
                {
                    EndParagraph();
                    string[] cells = trimmed.Trim('|').Split('|');
                    if (Array.TrueForAll(cells, c => c.Trim().Trim('-', ':').Length == 0))
                    {
                        continue;
                    }

                    blocks.Add((MarkdownBlockKind.Row, 0, Plain(string.Join(" ／ ", Array.ConvertAll(cells, c => c.Trim())))));
                }
                else if (IsNumberedItem(trimmed) || trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
                {
                    EndParagraph();
                    string text = IsNumberedItem(trimmed) ? trimmed : "・" + trimmed.Substring(2);
                    blocks.Add((MarkdownBlockKind.Item, Math.Min(indent, 4), Plain(text)));
                }
                else
                {
                    Join(paragraph, Plain(trimmed));
                }
            }

            EndParagraph();
            return blocks;
        }

        /// <summary>
        /// Labels for the blocks, in order: the title and headings with <paramref name="headingClass"/>, the rest with
        /// <paramref name="textClass"/>, and nested items indented.
        /// </summary>
        public static IEnumerable<Label> Labels(string markdown, string headingClass, string textClass, string titleSize = "vv-text-subhead", string textSize = "vv-text-body")
        {
            foreach ((MarkdownBlockKind kind, int indent, string text) in Blocks(markdown))
            {
                bool heading = kind == MarkdownBlockKind.Title || kind == MarkdownBlockKind.Heading;
                var label = new Label(text) { enableRichText = false };
                QuietText.Of(label);
                label.AddToClassList(kind == MarkdownBlockKind.Title ? titleSize : textSize);
                label.AddToClassList(heading ? headingClass : textClass);
                if (heading)
                {
                    label.AddToClassList("vv-bold");
                }

                label.style.marginLeft = indent * 16;
                yield return label;
            }
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

        // Lines of English are joined with a space, and lines of Japanese without one, as the file wraps them.
        private static void Join(StringBuilder paragraph, string line)
        {
            if (paragraph.Length > 0 && !IsWide(paragraph[paragraph.Length - 1]) && line.Length > 0 && !IsWide(line[0]))
            {
                paragraph.Append(' ');
            }

            paragraph.Append(line);
        }

        // Japanese and other full-width text is written without spaces between words.
        private static bool IsWide(char c)
        {
            return c >= '⺀';
        }

        private static string Plain(string text)
        {
            string linked = s_autoLink.Replace(s_link.Replace(text, "$1 ($2)"), "$1");

            // Markdown's escapes, such as "1\." for a number that is not a list; other backslashes, as in a path, stay.
            return s_escape.Replace(linked.Replace("**", string.Empty).Replace("`", string.Empty), "$1");
        }
    }
}
