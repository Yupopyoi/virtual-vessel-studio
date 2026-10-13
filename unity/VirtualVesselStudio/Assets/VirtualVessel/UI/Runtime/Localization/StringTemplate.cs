using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VirtualVessel.UI.Localization
{
    /// <summary>
    /// Fills named placeholders in string table text (UI foundation detailed design 7.4).
    /// Pure: the result depends only on the arguments.
    /// </summary>
    internal static class StringTemplate
    {
        public const string CountName = "count";

        /// <summary>
        /// Replaces <c>{name}</c> with the matching argument, formatted with <paramref name="culture"/>.
        /// <c>{{</c> and <c>}}</c> are literal braces. Placeholders without an argument stay as they are.
        /// </summary>
        /// <param name="count">Fills <c>{count}</c> when no argument has that name; used by plural forms.</param>
        /// <remarks>Returns <paramref name="template"/> itself, without allocating, when it has no braces.</remarks>
        public static string Fill(string template, IReadOnlyList<(string Name, object Value)> args, CultureInfo culture, int? count = null)
        {
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            if (template.IndexOf('{') < 0 && template.IndexOf('}') < 0)
            {
                return template;
            }

            var builder = new StringBuilder(template.Length + 16);
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];
                if ((c == '{' || c == '}') && i + 1 < template.Length && template[i + 1] == c)
                {
                    builder.Append(c);
                    i += 2;
                    continue;
                }

                int end = c == '{' ? template.IndexOf('}', i + 1) : -1;
                if (end < 0)
                {
                    builder.Append(c);
                    i++;
                    continue;
                }

                string name = template.Substring(i + 1, end - i - 1);
                if (TryGetArgument(name, args, count, out object value))
                {
                    builder.Append(value is IFormattable formattable ? formattable.ToString(null, culture) : value?.ToString());
                }
                else
                {
                    // Leave unknown placeholders visible; the string table tests catch mismatches.
                    builder.Append(template, i, end - i + 1);
                }

                i = end + 1;
            }

            return builder.ToString();
        }

        private static bool TryGetArgument(string name, IReadOnlyList<(string Name, object Value)> args, int? count, out object value)
        {
            if (args != null)
            {
                for (int i = 0; i < args.Count; i++)
                {
                    if (string.Equals(args[i].Name, name, StringComparison.Ordinal))
                    {
                        value = args[i].Value;
                        return true;
                    }
                }
            }

            if (count.HasValue && name == CountName)
            {
                value = count.Value;
                return true;
            }

            value = null;
            return false;
        }
    }
}
