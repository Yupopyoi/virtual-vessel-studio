using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Tests
{
    /// <summary>Checks the shipped string tables in UI/Strings (UI foundation detailed design 7.4).</summary>
    public sealed class StringTableContentTests
    {
        private static readonly string s_uiFolder = Path.Combine(Application.dataPath, "VirtualVessel", "UI");
        private static readonly Regex s_keyFormat = new Regex(@"^[a-z0-9]+(\.[a-z0-9-]+)+$");
        private static readonly Regex s_placeholder = new Regex(@"(?<!\{)\{([a-zA-Z0-9_]+)\}(?!\})");
        private static readonly Regex s_uxmlKey = new Regex(@"[a-z-]+-key\s*=\s*""([^""]*)""");

        private static IReadOnlyList<(string File, StringTable Table)> s_tables;

        private static IReadOnlyList<(string File, StringTable Table)> Tables
        {
            get
            {
                return s_tables ??= Directory.GetFiles(Path.Combine(s_uiFolder, "Strings"), "*.json")
                    .Select(file => (Path.GetFileNameWithoutExtension(file), StringTable.Parse(File.ReadAllText(file, Encoding.UTF8))))
                    .ToList();
            }
        }

        [Test]
        public void Tables_IncludeJapaneseAndEnglish_NamedByLocale()
        {
            Assert.That(Tables.Select(t => t.Table.Locale.Code), Is.SupersetOf(new[] { "ja", Localizer.FallbackLocale }));
            foreach ((string file, StringTable table) in Tables)
            {
                Assert.That(table.Locale.Code, Is.EqualTo(file), "The file name is the locale code.");
            }
        }

        [Test]
        public void Tables_HaveTheSameKeys()
        {
            StringTable english = Tables.Single(t => t.Table.Locale.Code == Localizer.FallbackLocale).Table;
            foreach ((string file, StringTable table) in Tables)
            {
                Assert.That(table.Strings.Keys.Except(english.Strings.Keys), Is.Empty, $"{file}: keys missing from en.");
                Assert.That(english.Strings.Keys.Except(table.Strings.Keys), Is.Empty, $"{file}: keys missing from {file}.");
            }
        }

        [Test]
        public void Keys_UseLowerCaseAndDots_AndValuesAreNotEmpty()
        {
            foreach ((string file, StringTable table) in Tables)
            {
                foreach (KeyValuePair<string, string> entry in table.Strings)
                {
                    Assert.That(s_keyFormat.IsMatch(entry.Key), Is.True, $"{file}: key '{entry.Key}' must be lower case with dots, such as 'live.start'.");
                    Assert.That(string.IsNullOrWhiteSpace(entry.Value), Is.False, $"{file}: '{entry.Key}' is empty.");
                }
            }
        }

        [Test]
        public void Placeholders_MatchAcrossLanguages()
        {
            StringTable english = Tables.Single(t => t.Table.Locale.Code == Localizer.FallbackLocale).Table;
            foreach ((string file, StringTable table) in Tables)
            {
                foreach (KeyValuePair<string, string> entry in table.Strings)
                {
                    if (!english.Strings.TryGetValue(entry.Key, out string englishText))
                    {
                        continue;
                    }

                    Assert.That(Placeholders(entry.Value), Is.EquivalentTo(Placeholders(englishText)), $"{file}: placeholders of '{entry.Key}' differ from en.");
                }
            }
        }

        [Test]
        public void CodeKeys_ExistInEveryTable()
        {
            IEnumerable<string> keys = typeof(Localizer).Assembly.GetTypes()
                .Where(type => type.GetCustomAttribute<StringKeysAttribute>() != null)
                .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue());

            AssertKeysExist(keys, "C# key constant");
        }

        [Test]
        public void UxmlKeys_ExistInEveryTable()
        {
            IEnumerable<string> keys = Directory.GetFiles(s_uiFolder, "*.uxml", SearchOption.AllDirectories)
                .SelectMany(file => s_uxmlKey.Matches(File.ReadAllText(file)).Cast<Match>())
                .Select(match => match.Groups[1].Value);

            AssertKeysExist(keys, "UXML *-key attribute");
        }

        private static void AssertKeysExist(IEnumerable<string> keys, string source)
        {
            foreach (string key in keys)
            {
                foreach ((string file, StringTable table) in Tables)
                {
                    // A plural key names its .one/.other pair.
                    bool found = table.Strings.ContainsKey(key) || table.Strings.ContainsKey(key + ".other");
                    Assert.That(found, Is.True, $"{source} '{key}' is missing from {file}.json.");
                }
            }
        }

        private static string[] Placeholders(string text)
        {
            return s_placeholder.Matches(text).Cast<Match>().Select(match => match.Groups[1].Value).Distinct().ToArray();
        }
    }
}
