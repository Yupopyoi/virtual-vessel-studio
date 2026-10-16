using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.UI.Settings;
using VirtualVessel.UI.Theme;

namespace VirtualVessel.UI.Tests
{
    public sealed class UiSettingsResolverTests
    {
        private static readonly UiSettingsChoices s_choices = Choices(systemPrefersReducedMotion: false);

        [Test]
        public void Resolve_Defaults_UseTheInitialLanguage()
        {
            (ResolvedUiSettings value, var warnings) = UiSettingsResolver.Resolve(UiSettings.CreateDefault("ja"), s_choices);

            Assert.That(warnings, Is.Empty);
            Assert.That(value.Language, Is.EqualTo("ja"));
            Assert.That(value.Theme, Is.EqualTo(ThemeSettings.Default));
            Assert.That(value.ReducedMotionMode, Is.EqualTo(ReducedMotionMode.System));
            Assert.That(value.FontSet, Is.EqualTo("NotoSans"));
            Assert.That(value.UiScale, Is.EqualTo(1f));
            Assert.That(value.DeveloperMode, Is.False);
            Assert.That(value.LastScreen, Is.EqualTo("live"));
        }

        [Test]
        public void Resolve_KnownValues_AreUsed()
        {
            var settings = new UiSettings
            {
                Language = "en",
                Palette = "mint",
                Background = "light",
                UiScale = 1.1,
                ReducedMotion = "on",
                DeveloperMode = true,
                LastScreen = "setup",
            };

            (ResolvedUiSettings value, var warnings) = UiSettingsResolver.Resolve(settings, s_choices);

            Assert.That(warnings, Is.Empty);
            Assert.That(value.Language, Is.EqualTo("en"));
            Assert.That(value.Theme, Is.EqualTo(new ThemeSettings("mint", ThemeBackground.Light, reducedMotion: true)));
            Assert.That(value.UiScale, Is.EqualTo(1.1f), "1.1 written by hand matches the float step.");
            Assert.That(value.DeveloperMode, Is.True);
            Assert.That(value.LastScreen, Is.EqualTo("setup"));
        }

        [Test]
        public void Resolve_ValuesOutsideTheAllowedLists_FallBackWithWarnings_AndTheInputIsUnchanged()
        {
            var settings = new UiSettings
            {
                Language = "fr",
                Palette = "rainbow",
                Background = "DARK",
                UiScale = 4,
                ReducedMotion = "sometimes",
                FontSet = "Comic",
            };

            (ResolvedUiSettings value, var warnings) = UiSettingsResolver.Resolve(settings, s_choices);

            Assert.That(value.Language, Is.EqualTo("en"), "An unknown language becomes English.");
            Assert.That(value.Theme.Palette, Is.EqualTo("lavender"));
            Assert.That(value.Theme.Background, Is.EqualTo(ThemeBackground.Dark));
            Assert.That(value.UiScale, Is.EqualTo(1f), "Only the four steps are allowed.");
            Assert.That(value.ReducedMotionMode, Is.EqualTo(ReducedMotionMode.System));
            Assert.That(value.FontSet, Is.EqualTo("NotoSans"));
            Assert.That(warnings, Has.Count.EqualTo(6));
            Assert.That(settings.Palette, Is.EqualTo("rainbow"), "The written value is kept until the user changes it.");
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(0.0)]
        [TestCase(-1.25)]
        public void Resolve_BrokenScale_IsOne(double scale)
        {
            (ResolvedUiSettings value, var warnings) = UiSettingsResolver.Resolve(new UiSettings { Language = "en", UiScale = scale }, s_choices);

            Assert.That(value.UiScale, Is.EqualTo(1f));
            Assert.That(warnings, Has.Count.EqualTo(1));
        }

        [TestCase(ReducedMotionMode.System, false, false)]
        [TestCase(ReducedMotionMode.System, true, true)]
        [TestCase(ReducedMotionMode.On, false, true)]
        [TestCase(ReducedMotionMode.Off, true, false)]
        public void ReducesMotion_FollowsTheSystemOnlyForSystem(ReducedMotionMode mode, bool system, bool expected)
        {
            Assert.That(UiSettingsResolver.ReducesMotion(mode, system), Is.EqualTo(expected));
            var settings = new UiSettings { Language = "en", ReducedMotion = UiSettingsResolver.ToText(mode) };
            Assert.That(UiSettingsResolver.Resolve(settings, Choices(system)).Value.Theme.ReducedMotion, Is.EqualTo(expected));
        }

        [Test]
        public void Resolve_NoLanguage_UsesTheInitialLanguage()
        {
            var settings = new UiSettings { Language = null };

            Assert.That(UiSettingsResolver.Resolve(settings, Choices(false, "ja")).Value.Language, Is.EqualTo("ja"));
        }

        [TestCase("en", "ja", "en")]
        [TestCase(null, "en", "en")]
        [TestCase(null, null, "ja")]
        [TestCase("fr", "xx", "ja")]
        public void ChooseProblemLanguage_ShownThenSavedThenOs(string shown, string saved, string expected)
        {
            Assert.That(UiSettingsResolver.ChooseProblemLanguage(shown, saved, "ja", new[] { "en", "ja" }), Is.EqualTo(expected));
        }

        [Test]
        public void Quote_ShortensAndHidesControlCharacters()
        {
            Assert.That(UiSettingsResolver.Quote(null), Is.EqualTo("null"));
            Assert.That(UiSettingsResolver.Quote("a\nb"), Is.EqualTo("'a?b'"));
            string quoted = UiSettingsResolver.Quote(new string('x', 1000));
            Assert.That(quoted.Length, Is.LessThan(50), "A planted value cannot flood the log.");
            Assert.That(quoted, Does.EndWith("…'"));
        }

        internal static UiSettingsChoices Choices(bool systemPrefersReducedMotion, string initialLanguage = "ja")
        {
            return new UiSettingsChoices(
                new[] { "lavender", "mint" },
                new[] { "NotoSans" },
                new[] { "en", "ja" },
                initialLanguage,
                systemPrefersReducedMotion);
        }
    }

    public sealed class UiSettingsPeekTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_directory, recursive: true);
        }

        [Test]
        public void ReadLanguage_ReadsOnlyAndNeverThrows()
        {
            string path = Path.Combine(_directory, UiSettings.FileName);
            Assert.That(UiSettingsPeek.ReadLanguage(null), Is.Null);
            Assert.That(UiSettingsPeek.ReadLanguage(_directory), Is.Null, "No file.");

            File.WriteAllText(path, "{ \"schemaVersion\": 1, \"language\": \"en\" }");
            Assert.That(UiSettingsPeek.ReadLanguage(_directory), Is.EqualTo("en"));

            File.WriteAllText(path, "{ broken");
            Assert.That(UiSettingsPeek.ReadLanguage(_directory), Is.Null);
            Assert.That(File.ReadAllText(path), Is.EqualTo("{ broken"), "A broken file is left alone.");

            File.WriteAllText(path, "{ \"language\": 3 }");
            Assert.That(UiSettingsPeek.ReadLanguage(_directory), Is.EqualTo("3"), "A wrong type never throws; the language is checked when chosen.");

            File.WriteAllText(path, "{ \"language\": \"en\", \"pad\": \"" + new string('x', (int)ConfigFileStore<UiSettings>.MaxFileBytes) + "\" }");
            Assert.That(UiSettingsPeek.ReadLanguage(_directory), Is.Null, "Oversized files are not read.");
        }
    }

    public sealed class SaveDebounceTests
    {
        [Test]
        public void Debounce_IsDueAfterTheDelayFromTheLastChange()
        {
            SaveDebounce debounce = SaveDebounce.Idle;
            Assert.That(debounce.IsDue(1000), Is.False, "Nothing to save.");

            debounce = debounce.Changed(100, 50);
            Assert.That(debounce.IsDue(149), Is.False);
            Assert.That(debounce.IsDue(150), Is.True);

            debounce = debounce.Changed(140, 50);
            Assert.That(debounce.IsDue(150), Is.False, "A later change moves the save.");
            Assert.That(debounce.IsDue(190), Is.True);
        }
    }

    public sealed class UiSettingsServiceTests
    {
        private string _directory;
        private ManualMonotonicClock _clock;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _clock = new ManualMonotonicClock();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Test]
        public void Load_WithoutFile_UsesDefaultsAndWritesNothing()
        {
            UiSettingsService service = CreateService();

            Assert.That(service.Load().Status, Is.EqualTo(ConfigLoadStatus.Created));
            Assert.That(service.Current.Language, Is.EqualTo("ja"));
            Assert.That(service.HasPendingSave, Is.False);
            Assert.That(File.Exists(service.FilePath), Is.False);
        }

        [Test]
        public async Task Change_IsReportedAtOnce_AndSavedHalfASecondAfterTheLastChange()
        {
            UiSettingsService service = CreateService();
            service.Load();
            ResolvedUiSettings? reported = null;
            service.Changed += settings => reported = settings;

            service.SetPalette("mint");
            Assert.That(reported?.Theme.Palette, Is.EqualTo("mint"), "Applied at once.");
            Assert.That(service.HasPendingSave, Is.True);

            _clock.Advance(0.4);
            service.SetUiScale(1.25f);
            _clock.Advance(0.4);
            service.Tick();
            Assert.That(File.Exists(service.FilePath), Is.False, "0.4 s after the last change: not yet.");

            _clock.Advance(0.1);
            service.Tick();
            await service.PendingSave;
            Assert.That(service.HasPendingSave, Is.False);

            string text = File.ReadAllText(service.FilePath);
            Assert.That(text, Does.Contain("\"palette\": \"mint\""));
            Assert.That(text, Does.Contain("\"uiScale\": 1.25"));
            Assert.That(text, Does.Contain("\"schemaVersion\": 1"));
        }

        [Test]
        public void ChoosingTheCurrentValue_SavesNothing()
        {
            UiSettingsService service = CreateService();
            service.Load();
            int changes = 0;
            service.Changed += _ => changes++;

            service.SetPalette("lavender");
            service.SetLastScreen("live");

            Assert.That(changes, Is.EqualTo(0));
            Assert.That(service.HasPendingSave, Is.False);
        }

        [Test]
        public async Task SetUiScale_WritesTheStepAsWritten()
        {
            UiSettingsService service = CreateService();
            service.Load();

            service.SetUiScale(1.1f);
            await service.FlushAsync();

            Assert.That(File.ReadAllText(service.FilePath), Does.Contain("\"uiScale\": 1.1,"));
        }

        [Test]
        public void SetUiScale_RejectsOtherValues()
        {
            UiSettingsService service = CreateService();

            Assert.Throws<ArgumentOutOfRangeException>(() => service.SetUiScale(3f));
        }

        [Test]
        public async Task UnknownValuesAndFields_SurviveAChangeOfAnotherSetting()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, UiSettings.FileName), "{ \"schemaVersion\": 1, \"language\": \"en\", \"palette\": \"rainbow\", \"future\": { \"x\": 1 } }");
            UiSettingsService service = CreateService();
            service.Load();
            Assert.That(service.Current.Theme.Palette, Is.EqualTo("lavender"));

            service.SetBackground(ThemeBackground.Light);
            await service.FlushAsync();

            string text = File.ReadAllText(service.FilePath);
            Assert.That(text, Does.Contain("\"palette\": \"rainbow\""), "Kept until the user picks a palette.");
            Assert.That(text, Does.Contain("\"future\""));
            Assert.That(text, Does.Contain("\"background\": \"light\""));
        }

        [Test]
        public async Task FlushAsync_SavesAWaitingChangeAtOnce()
        {
            UiSettingsService service = CreateService();
            service.Load();
            service.SetDeveloperMode(true);

            await service.FlushAsync();

            Assert.That(service.HasPendingSave, Is.False);
            Assert.That(File.ReadAllText(service.FilePath), Does.Contain("\"developerMode\": true"));
        }

        [Test]
        public async Task SaveFailure_IsReportedOnce_UntilASaveSucceeds()
        {
            // A folder where the file should be makes every save fail.
            string blocker = Path.Combine(_directory, UiSettings.FileName);
            Directory.CreateDirectory(blocker);
            UiSettingsService service = CreateService();
            service.Load();
            int failures = 0;
            service.SaveFailed += () => failures++;

            for (int i = 0; i < 2; i++)
            {
                service.SetPalette(i == 0 ? "mint" : "lavender");
                _clock.Advance(0.5);
                service.Tick();
                await service.PendingSave;
                service.Tick();
            }

            Assert.That(failures, Is.EqualTo(1), "The same failure is not repeated.");

            Directory.Delete(blocker);
            service.SetPalette("mint");
            _clock.Advance(0.5);
            service.Tick();
            await service.PendingSave;
            service.Tick();
            File.Delete(service.FilePath);
            Directory.CreateDirectory(blocker);
            service.SetPalette("lavender");
            _clock.Advance(0.5);
            service.Tick();
            await service.PendingSave;
            service.Tick();

            Assert.That(failures, Is.EqualTo(2), "A new failure after a success is reported again.");
        }

        private UiSettingsService CreateService()
        {
            var store = new ConfigFileStore<UiSettings>(_directory, UiSettings.FileName, UiSettings.SchemaVersion, () => UiSettings.CreateDefault("ja"), new FixedSystemClock());
            return new UiSettingsService(store, UiSettingsResolverTests.Choices(false), _clock, null);
        }

        private sealed class ManualMonotonicClock : IMonotonicClock
        {
            private long _now = 1_000_000;

            public long Frequency => 1_000_000;

            public long GetTimestamp()
            {
                return _now;
            }

            public TimeSpan GetElapsed(long startTimestamp, long endTimestamp)
            {
                return TimeSpan.FromSeconds((endTimestamp - startTimestamp) / (double)Frequency);
            }

            public void Advance(double seconds)
            {
                _now += (long)(seconds * Frequency);
            }
        }

        private sealed class FixedSystemClock : ISystemClock
        {
            public DateTimeOffset UtcNow => new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        }
    }
}
