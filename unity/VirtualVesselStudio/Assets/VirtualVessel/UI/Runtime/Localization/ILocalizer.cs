using System;
using System.Collections.Generic;

namespace VirtualVessel.UI.Localization
{
    /// <summary>
    /// Looks up user-facing text by key and switches the language at runtime
    /// (UI foundation detailed design 7.4). Call from the main thread.
    /// </summary>
    public interface ILocalizer
    {
        /// <summary>Raised after the language changes, so that shown text can be refreshed.</summary>
        event Action<LocaleInfo> LocaleChanged;

        LocaleInfo Locale { get; }

        /// <summary>Every language with a string table, for the language setting.</summary>
        IReadOnlyList<LocaleInfo> Locales { get; }

        /// <summary>Switches the language. An unknown code falls back to English.</summary>
        void SetLocale(string code);

        /// <summary>Returns whether the current language or the English fallback has <paramref name="key"/>.</summary>
        bool Contains(string key);

        /// <summary>
        /// Returns the text for <paramref name="key"/>: the current language, then English, then
        /// <c>[key]</c> so that a missing string is visible instead of blank.
        /// </summary>
        string Get(string key);

        /// <summary>Returns the text with each <c>{name}</c> replaced by the matching argument.</summary>
        string Get(string key, params (string Name, object Value)[] args);

        /// <summary>
        /// Returns <c>key.one</c> when <paramref name="count"/> is 1 and <c>key.other</c> otherwise,
        /// with <c>{count}</c> and the other arguments filled in.
        /// </summary>
        string Plural(string key, int count, params (string Name, object Value)[] args);
    }
}
