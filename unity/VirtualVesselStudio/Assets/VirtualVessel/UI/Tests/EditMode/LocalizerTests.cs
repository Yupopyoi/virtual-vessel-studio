using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Tests
{
    public sealed class LocalizerTests
    {
        private RecordingLog _log;

        [SetUp]
        public void SetUp()
        {
            _log = new RecordingLog();
        }

        [Test]
        public void Get_ReturnsCurrentLanguage_AndSwitchesAtRuntime()
        {
            Localizer localizer = Create("ja");
            var changes = new List<string>();
            localizer.LocaleChanged += locale => changes.Add(locale.Code);

            string ja = localizer.Get("live.start");
            localizer.SetLocale("en");
            localizer.SetLocale("en");

            Assert.That(ja, Is.EqualTo("配信を開始"));
            Assert.That(localizer.Get("live.start"), Is.EqualTo("Go live"));
            Assert.That(changes, Is.EqualTo(new[] { "en" }), "Setting the same language again does not raise the event.");
            Assert.That(localizer.Locales, Has.Count.EqualTo(2));
        }

        [Test]
        public void Get_MissingInCurrentLanguage_UsesEnglish()
        {
            Localizer localizer = Create("ja");

            Assert.That(localizer.Get("only.english"), Is.EqualTo("English only"));
            Assert.That(localizer.Contains("only.english"), Is.True);
            Assert.That(_log.Warnings, Is.Empty);
        }

        [Test]
        public void Get_MissingEverywhere_ShowsKeyAndWarnsOnce()
        {
            Localizer localizer = Create("ja");

            string first = localizer.Get("no.such.key");
            localizer.Get("no.such.key");

            Assert.That(first, Is.EqualTo("[no.such.key]"));
            Assert.That(localizer.Contains("no.such.key"), Is.False);
            Assert.That(_log.Warnings, Has.Count.EqualTo(1));
        }

        [Test]
        public void Get_FillsNamedPlaceholders()
        {
            Localizer localizer = Create("en");

            Assert.That(localizer.Get("live.elapsed", ("time", "01:24:36")), Is.EqualTo("Live 01:24:36"));
            Assert.That(localizer.Get("format.braces", ("name", 3)), Is.EqualTo("{literal} 3 {unknown}"));
            Assert.That(localizer.Get("live.elapsed"), Is.EqualTo("Live {time}"), "Without arguments placeholders stay visible.");
            Assert.That(localizer.Get("format.braces"), Is.EqualTo("{literal} {name} {unknown}"), "Braces are unescaped even without arguments.");
        }

        [Test]
        public void Get_FormatsNumbersForTheLanguage()
        {
            Localizer localizer = Create("en");

            Assert.That(localizer.Get("format.number", ("value", 1.5)), Is.EqualTo("1.5"));
        }

        [Test]
        public void Plural_ChoosesOneOrOther()
        {
            Localizer en = Create("en");
            Localizer ja = Create("ja");

            Assert.That(en.Plural("files", 1), Is.EqualTo("1 file"));
            Assert.That(en.Plural("files", 0, ("where", "now")), Is.EqualTo("0 files now"));
            Assert.That(en.Plural("files", 3, ("where", "here")), Is.EqualTo("3 files here"));
            Assert.That(ja.Plural("files", 1), Is.EqualTo("1 個のファイル"), "Languages without a .one form use .other.");
        }

        [Test]
        public void UnknownLocale_FallsBackToEnglishWithWarning()
        {
            Localizer localizer = Create("xx");

            Assert.That(localizer.Locale.Code, Is.EqualTo("en"));
            Assert.That(_log.Warnings, Has.Count.EqualTo(1));
        }

        [Test]
        public void Constructor_IgnoresDuplicateLocales_AndRequiresATable()
        {
            var localizer = new Localizer(new[] { Table("en", ("a.b", "first")), Table("en", ("a.b", "second")) }, "en", _log);

            Assert.That(localizer.Get("a.b"), Is.EqualTo("first"));
            Assert.That(_log.Warnings, Has.Count.EqualTo(1));
            Assert.Throws<ArgumentException>(() => new Localizer(Array.Empty<StringTable>(), "en", _log));
        }

        [Test]
        public void ChooseInitialLocale_FollowsTheSystemLanguage()
        {
            string[] both = { "en", "ja" };

            Assert.That(Localizer.ChooseInitialLocale(SystemLanguage.Japanese, both), Is.EqualTo("ja"));
            Assert.That(Localizer.ChooseInitialLocale(SystemLanguage.French, both), Is.EqualTo("en"));
            Assert.That(Localizer.ChooseInitialLocale(SystemLanguage.Japanese, new[] { "en" }), Is.EqualTo("en"));
        }

        [Test]
        public void StringTable_Parse_ReadsAndRejects()
        {
            StringTable table = StringTable.Parse("{ \"schemaVersion\": 1, \"locale\": \"ja\", \"displayName\": \"日本語\", \"strings\": { \"a.b\": \"x\" } }");

            Assert.That(table.Locale.Code, Is.EqualTo("ja"));
            Assert.That(table.Locale.DisplayName, Is.EqualTo("日本語"));
            Assert.That(table.Locale.FontSet, Is.Null);
            Assert.That(table.Strings["a.b"], Is.EqualTo("x"));
            Assert.Throws<FormatException>(() => StringTable.Parse("{ \"schemaVersion\": 2, \"locale\": \"ja\", \"strings\": {} }"));
            Assert.Throws<FormatException>(() => StringTable.Parse("{ \"schemaVersion\": 1, \"strings\": {} }"));
            Assert.Throws<FormatException>(() => StringTable.Parse("{ \"schemaVersion\": 1, \"locale\": \"ja\", \"strings\": { \"a.b\": { \"nested\": 1 } } }"));
            Assert.Throws<FormatException>(() => StringTable.Parse("not json"));
        }

        private Localizer Create(string locale)
        {
            StringTable ja = Table("ja", ("live.start", "配信を開始"), ("files.other", "{count} 個のファイル"));
            StringTable en = Table(
                "en",
                ("live.start", "Go live"),
                ("live.elapsed", "Live {time}"),
                ("only.english", "English only"),
                ("format.braces", "{{literal}} {name} {unknown}"),
                ("format.number", "{value}"),
                ("files.one", "{count} file"),
                ("files.other", "{count} files {where}"));
            return new Localizer(new[] { ja, en }, locale, _log);
        }

        private static StringTable Table(string locale, params (string Key, string Value)[] strings)
        {
            var map = new Dictionary<string, string>();
            foreach ((string key, string value) in strings)
            {
                map[key] = value;
            }

            return new StringTable(new LocaleInfo(locale, locale, null), map);
        }

        private sealed class RecordingLog : ILog
        {
            public List<string> Warnings { get; } = new List<string>();

            public bool IsEnabled(LogLevel level)
            {
                return true;
            }

            public void Write(LogLevel level, string message, Exception exception = null)
            {
                if (level == LogLevel.Warning)
                {
                    Warnings.Add(message);
                }
            }

            public void Write(LogLevel level, string message, IReadOnlyList<LogProperty> properties, Exception exception = null)
            {
                Write(level, message, exception);
            }

            public ILog WithContext(string key, string value)
            {
                return this;
            }
        }
    }
}
