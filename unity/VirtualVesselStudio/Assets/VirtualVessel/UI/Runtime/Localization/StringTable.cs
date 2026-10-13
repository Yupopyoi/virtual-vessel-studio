using System;
using System.Collections.Generic;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.UI.Localization
{
    /// <summary>
    /// The text of one language, read from <c>UI/Strings/&lt;locale&gt;.json</c>
    /// (UI foundation detailed design 7.4).
    /// </summary>
    public sealed class StringTable
    {
        public const int SchemaVersion = 1;

        public StringTable(LocaleInfo locale, IReadOnlyDictionary<string, string> strings)
        {
            Locale = locale ?? throw new ArgumentNullException(nameof(locale));
            Strings = strings ?? throw new ArgumentNullException(nameof(strings));
        }

        public LocaleInfo Locale { get; }

        public IReadOnlyDictionary<string, string> Strings { get; }

        /// <exception cref="FormatException">The text is not a string table this version can read.</exception>
        public static StringTable Parse(string json)
        {
            ConfigDocument document = ConfigDocument.Parse(json);
            int version = document.Get("schemaVersion", 0);
            if (version != SchemaVersion)
            {
                // Tables ship with the application, so a different version is a build mistake, not user data.
                throw new FormatException($"String table schemaVersion is {version}; expected {SchemaVersion}.");
            }

            string code = document.Get<string>("locale");
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new FormatException("String table has no locale.");
            }

            Dictionary<string, string> strings = document.Get<Dictionary<string, string>>("strings");
            if (strings == null)
            {
                throw new FormatException($"String table '{code}' has no strings object, or a value in it is not text.");
            }

            var locale = new LocaleInfo(code, document.Get<string>("displayName"), document.Get<string>("fontSet"));
            return new StringTable(locale, new Dictionary<string, string>(strings, StringComparer.Ordinal));
        }
    }
}
