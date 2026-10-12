using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using VirtualVessel.Diagnostics.Logging;

namespace VirtualVessel.UI.Localization
{
    /// <summary>
    /// The project's own lightweight localization (UI foundation detailed design 7.4, 12.1): one
    /// string table per language, named <c>{placeholders}</c>, and runtime language switching.
    /// </summary>
    /// <remarks>
    /// Lookups do not allocate unless placeholders are filled in. Not thread-safe; call from the main thread.
    /// </remarks>
    public sealed class Localizer : ILocalizer
    {
        public const string FallbackLocale = "en";

        private const string OneSuffix = ".one";
        private const string OtherSuffix = ".other";

        private readonly Dictionary<string, StringTable> _tables = new Dictionary<string, StringTable>(StringComparer.Ordinal);
        private readonly List<LocaleInfo> _locales = new List<LocaleInfo>();
        private readonly HashSet<string> _reportedMissing = new HashSet<string>(StringComparer.Ordinal);
        private readonly StringBuilder _builder = new StringBuilder();
        private readonly ILog _log;
        private readonly StringTable _fallback;
        private StringTable _current;
        private CultureInfo _culture;

        /// <param name="tables">At least one table. A second table for the same locale is ignored.</param>
        /// <param name="locale">The starting language; see <see cref="ChooseInitialLocale"/>.</param>
        /// <param name="log">Optional; records language changes and missing keys.</param>
        public Localizer(IEnumerable<StringTable> tables, string locale, ILog log)
        {
            if (tables == null)
            {
                throw new ArgumentNullException(nameof(tables));
            }

            _log = log;
            foreach (StringTable table in tables)
            {
                if (table == null)
                {
                    continue;
                }

                if (_tables.ContainsKey(table.Locale.Code))
                {
                    _log?.Warning($"Two string tables for locale '{table.Locale.Code}'; the second is ignored.");
                    continue;
                }

                _tables.Add(table.Locale.Code, table);
                _locales.Add(table.Locale);
            }

            if (_locales.Count == 0)
            {
                throw new ArgumentException("At least one string table is required.", nameof(tables));
            }

            _tables.TryGetValue(FallbackLocale, out _fallback);
            _current = Resolve(locale);
            _culture = CultureFor(_current.Locale.Code);
        }

        public event Action<LocaleInfo> LocaleChanged;

        public LocaleInfo Locale => _current.Locale;

        public IReadOnlyList<LocaleInfo> Locales => _locales;

        /// <summary>
        /// The starting language when the user has not chosen one: Japanese on a Japanese system,
        /// otherwise English (UI foundation detailed design 8.1).
        /// </summary>
        public static string ChooseInitialLocale(SystemLanguage systemLanguage, IEnumerable<string> available)
        {
            var codes = new List<string>(available ?? Array.Empty<string>());
            if (systemLanguage == SystemLanguage.Japanese && codes.Contains("ja"))
            {
                return "ja";
            }

            if (codes.Contains(FallbackLocale) || codes.Count == 0)
            {
                return FallbackLocale;
            }

            return codes[0];
        }

        public void SetLocale(string code)
        {
            StringTable next = Resolve(code);
            if (next == _current)
            {
                return;
            }

            _current = next;
            _culture = CultureFor(next.Locale.Code);
            _log?.Information($"UI language changed to {next.Locale.Code}.");
            LocaleChanged?.Invoke(next.Locale);
        }

        public bool Contains(string key)
        {
            return key != null && TryFind(key, out _);
        }

        public string Get(string key)
        {
            return TryFind(key, out string text) ? text : Missing(key);
        }

        public string Get(string key, params (string Name, object Value)[] args)
        {
            if (!TryFind(key, out string template))
            {
                return Missing(key);
            }

            return Fill(template, args, hasCount: false, count: 0);
        }

        public string Plural(string key, int count, params (string Name, object Value)[] args)
        {
            string otherKey = key + OtherSuffix;

            // Take both forms from the same table, so that a language without a .one form does not
            // borrow the English one.
            StringTable source = _current.Strings.ContainsKey(otherKey) ? _current
                : _fallback != null && _fallback.Strings.ContainsKey(otherKey) ? _fallback
                : null;
            if (source == null)
            {
                return Missing(otherKey);
            }

            if (count != 1 || !source.Strings.TryGetValue(key + OneSuffix, out string template))
            {
                template = source.Strings[otherKey];
            }

            return Fill(template, args, hasCount: true, count);
        }

        private StringTable Resolve(string code)
        {
            if (code != null && _tables.TryGetValue(code, out StringTable table))
            {
                return table;
            }

            // A language removed in a later version, or a hand-edited settings file.
            StringTable fallback = _fallback ?? _tables[_locales[0].Code];
            _log?.Warning($"No string table for locale '{code}'; using '{fallback.Locale.Code}'.");
            return fallback;
        }

        private bool TryFind(string key, out string text)
        {
            if (key == null)
            {
                text = null;
                return false;
            }

            if (_current.Strings.TryGetValue(key, out text))
            {
                return true;
            }

            return _fallback != null && _fallback != _current && _fallback.Strings.TryGetValue(key, out text);
        }

        private string Missing(string key)
        {
            // Report each key once so that a missing string in a list or a refresh loop does not flood the log.
            if (_reportedMissing.Add(key ?? string.Empty))
            {
                _log?.Warning($"Missing UI string '{key}' in '{_current.Locale.Code}' and '{FallbackLocale}'.");
            }

            return "[" + key + "]";
        }

        /// <summary>Replaces <c>{name}</c> with the matching argument; <c>{{</c> and <c>}}</c> are literal braces.</summary>
        private string Fill(string template, (string Name, object Value)[] args, bool hasCount, int count)
        {
            if (template.IndexOf('{') < 0 && template.IndexOf('}') < 0)
            {
                return template;
            }

            _builder.Clear();
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];
                if ((c == '{' || c == '}') && i + 1 < template.Length && template[i + 1] == c)
                {
                    _builder.Append(c);
                    i += 2;
                    continue;
                }

                int end = c == '{' ? template.IndexOf('}', i + 1) : -1;
                if (end < 0)
                {
                    _builder.Append(c);
                    i++;
                    continue;
                }

                string name = template.Substring(i + 1, end - i - 1);
                if (TryGetArgument(name, args, hasCount, count, out object value))
                {
                    _builder.Append(value is IFormattable formattable ? formattable.ToString(null, _culture) : value?.ToString());
                }
                else
                {
                    // Leave unknown placeholders visible; the string table tests catch mismatches.
                    _builder.Append(template, i, end - i + 1);
                }

                i = end + 1;
            }

            return _builder.ToString();
        }

        private static bool TryGetArgument(string name, (string Name, object Value)[] args, bool hasCount, int count, out object value)
        {
            if (args != null)
            {
                foreach ((string argName, object argValue) in args)
                {
                    if (string.Equals(argName, name, StringComparison.Ordinal))
                    {
                        value = argValue;
                        return true;
                    }
                }
            }

            if (hasCount && name == "count")
            {
                value = count;
                return true;
            }

            value = null;
            return false;
        }

        private static CultureInfo CultureFor(string code)
        {
            try
            {
                return CultureInfo.GetCultureInfo(code);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }
    }
}
