using System;

namespace VirtualVessel.UI.Localization
{
    /// <summary>A language the UI can show, as declared by its string table.</summary>
    public sealed class LocaleInfo
    {
        public LocaleInfo(string code, string displayName, string fontSet)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("Locale code must not be empty.", nameof(code));
            }

            Code = code;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? code : displayName;
            FontSet = string.IsNullOrWhiteSpace(fontSet) ? null : fontSet;
        }

        /// <summary>Such as "ja" or "en"; also the string table file name.</summary>
        public string Code { get; }

        /// <summary>The language's own name, such as "日本語", shown in the language setting.</summary>
        public string DisplayName { get; }

        /// <summary>The recommended font set for this language, or null for the default.</summary>
        public string FontSet { get; }

        public override string ToString()
        {
            return Code;
        }
    }
}
