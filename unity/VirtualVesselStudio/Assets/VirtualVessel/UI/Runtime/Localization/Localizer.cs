using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using VirtualVessel.Diagnostics.Logging;

namespace VirtualVessel.UI.Localization
{
    /// <summary>
    /// The project's own lightweight localization (UI foundation detailed design 7.4, 12.1): one
    /// string table per language, named <c>{placeholders}</c>, and runtime language switching.
    /// </summary>
    /// <remarks>
    /// The lookups and text filling are pure static functions; this class only holds the current
    /// language and does the side effects (the change event and log entries). Lookups do not
    /// allocate unless placeholders are filled in. Not thread-safe; call from the main thread.
    /// </remarks>
    public sealed class Localizer : ILocalizer
    {
        public const string FallbackLocale = "en";

        private const string OneSuffix = ".one";
        private const string OtherSuffix = ".other";

        private readonly Dictionary<string, StringTable> _tables = new Dictionary<string, StringTable>(StringComparer.Ordinal);
        private readonly List<LocaleInfo> _locales = new List<LocaleInfo>();
        private readonly HashSet<string> _reportedMissing = new HashSet<string>(StringComparer.Ordinal);
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
            _current = ResolveAndReport(locale);
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
            StringTable next = ResolveAndReport(code);
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
            return TryFind(_current, _fallback, key, out _);
        }

        public string Get(string key)
        {
            // Still unescape {{ and }}, so a text reads the same with or without arguments.
            return TryFind(_current, _fallback, key, out string text) ? StringTemplate.Fill(text, null, _culture) : ReportMissing(key);
        }

        public string Get(string key, params (string Name, object Value)[] args)
        {
            return TryFind(_current, _fallback, key, out string template) ? StringTemplate.Fill(template, args, _culture) : ReportMissing(key);
        }

        public string Plural(string key, int count, params (string Name, object Value)[] args)
        {
            return TryFindPlural(_current, _fallback, key, count, out string template)
                ? StringTemplate.Fill(template, args, _culture, count)
                : ReportMissing(key + OtherSuffix);
        }

        /// <summary>Looks up <paramref name="key"/> in <paramref name="current"/>, then in <paramref name="fallback"/>.</summary>
        internal static bool TryFind(StringTable current, StringTable fallback, string key, out string text)
        {
            text = null;
            if (key == null)
            {
                return false;
            }

            return current.Strings.TryGetValue(key, out text)
                || (fallback != null && fallback != current && fallback.Strings.TryGetValue(key, out text));
        }

        /// <summary>
        /// Picks <c>key.one</c> for a count of 1 and <c>key.other</c> otherwise. Both forms come from
        /// the same table, so that a language without a .one form does not borrow the English one.
        /// </summary>
        internal static bool TryFindPlural(StringTable current, StringTable fallback, string key, int count, out string template)
        {
            template = null;
            if (key == null)
            {
                return false;
            }

            string otherKey = key + OtherSuffix;
            StringTable source = current.Strings.ContainsKey(otherKey) ? current
                : fallback != null && fallback.Strings.ContainsKey(otherKey) ? fallback
                : null;
            if (source == null)
            {
                return false;
            }

            if (count != 1 || !source.Strings.TryGetValue(key + OneSuffix, out template))
            {
                template = source.Strings[otherKey];
            }

            return true;
        }

        private StringTable ResolveAndReport(string code)
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

        private string ReportMissing(string key)
        {
            // Report each key once so that a missing string in a list or a refresh loop does not flood the log.
            if (_reportedMissing.Add(key ?? string.Empty))
            {
                _log?.Warning($"Missing UI string '{key}' in '{_current.Locale.Code}' and '{FallbackLocale}'.");
            }

            return "[" + key + "]";
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
