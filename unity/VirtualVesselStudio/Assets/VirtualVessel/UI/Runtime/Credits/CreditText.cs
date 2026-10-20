using System;
using System.Collections.Generic;
using System.Text;

namespace VirtualVessel.UI.Credits
{
    /// <summary>Prepares a license text for showing.</summary>
    public static class CreditText
    {
        /// <summary>
        /// Characters per label. A UI Toolkit text element draws into one mesh of at most 65,535
        /// vertices, four per glyph, so a 140 KB notices file must be split into several labels.
        /// </summary>
        public const int MaxCharactersPerPart = 8000;

        /// <summary>
        /// Splits <paramref name="text"/> into parts of at most <paramref name="maxCharacters"/>,
        /// breaking between lines, with line endings made "\n". A single longer line is cut. Pure.
        /// </summary>
        public static IReadOnlyList<string> Split(string text, int maxCharacters = MaxCharactersPerPart)
        {
            if (maxCharacters < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCharacters), maxCharacters, "Parts need room for at least one character.");
            }

            var parts = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return parts;
            }

            var part = new StringBuilder();
            foreach (string line in text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n'))
            {
                // +1 for the line break that joins it to the part.
                if (part.Length > 0 && part.Length + 1 + line.Length > maxCharacters)
                {
                    parts.Add(part.ToString());
                    part.Clear();
                }

                string rest = line;
                while (rest.Length > maxCharacters)
                {
                    parts.Add(rest.Substring(0, maxCharacters));
                    rest = rest.Substring(maxCharacters);
                }

                if (part.Length > 0)
                {
                    part.Append('\n');
                }

                part.Append(rest);
            }

            if (part.Length > 0)
            {
                parts.Add(part.ToString());
            }

            return parts;
        }
    }
}
