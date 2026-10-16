using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine.UIElements;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Screens;
using VirtualVessel.UI.Settings;

namespace VirtualVessel.UI.Tests
{
    public sealed class ScreenNavigationTests
    {
        private static readonly ScreenDefinition[] s_screens =
        {
            Screen("live"),
            Screen("setup"),
            Screen("lab"),
            Screen("diagnostics", developerOnly: true),
        };

        [TestCase("setup", false, "setup")]
        [TestCase("diagnostics", true, "diagnostics")]
        [TestCase("diagnostics", false, "live")]
        [TestCase("removed-screen", true, "live")]
        [TestCase(null, false, "live")]
        public void Choose_OpensTheWantedScreenOnlyWhenItShows(string wanted, bool developerMode, string expected)
        {
            Assert.That(ScreenNavigation.Choose(s_screens, wanted, developerMode), Is.EqualTo(expected));
        }

        [Test]
        public void IsVisible_DeveloperOnlyScreensNeedDeveloperMode()
        {
            Assert.That(ScreenNavigation.IsVisible(s_screens[3], false), Is.False);
            Assert.That(ScreenNavigation.IsVisible(s_screens[3], true), Is.True);
            Assert.That(ScreenNavigation.IsVisible(s_screens[0], false), Is.True);
        }

        [TestCase(0, -1, 3, 0)]
        [TestCase(0, 1, 3, 1)]
        [TestCase(2, 1, 3, 2)]
        [TestCase(-1, 1, 3, 0)]
        [TestCase(0, 1, 0, -1)]
        public void Step_StaysWithinTheList(int current, int step, int count, int expected)
        {
            Assert.That(ScreenNavigation.Step(current, step, count), Is.EqualTo(expected));
        }

        internal static ScreenDefinition Screen(string id, bool developerOnly = false, Func<VisualElement> create = null)
        {
            return new ScreenDefinition(id, IconName.Live, "nav." + id, "tip.nav." + id, create ?? (() => new VisualElement()), developerOnly);
        }
    }

    public sealed class DiagnosticsFormatTests
    {
        [Test]
        public void Row_Timer_HasStatisticsInMilliseconds_AndTheBudget()
        {
            var metrics = Metrics();
            PerfTimer timer = metrics.Timer("Voice", "Convert", TimeSpan.FromMilliseconds(10));
            timer.Record(TimeSpan.FromMilliseconds(4));
            // FromMilliseconds rounds to whole milliseconds on this runtime; ticks keep the half.
            timer.Record(TimeSpan.FromTicks(125_000));

            string[] row = DiagnosticsFormat.Row(Find(metrics, "Convert"));

            Assert.That(row, Has.Length.EqualTo(DiagnosticsFormat.ColumnCount));
            Assert.That(row[0], Is.EqualTo("Voice"));
            Assert.That(row[1], Is.EqualTo("Convert"));
            Assert.That(row[2], Is.EqualTo(ScreenStrings.KindTimer));
            Assert.That(row[3], Is.EqualTo("12.5 ms"));
            Assert.That(row[5], Is.EqualTo("12.5 ms"));
            Assert.That(row[8], Is.EqualTo("2"));
            Assert.That(row[9], Is.EqualTo("1 (> 10 ms)"));
        }

        [Test]
        public void Row_Counter_ShowsOnlyTheTotal()
        {
            var metrics = Metrics();
            metrics.Counter("Audio", "Underruns").Add(1234);

            string[] row = DiagnosticsFormat.Row(Find(metrics, "Underruns"));

            Assert.That(row[2], Is.EqualTo(ScreenStrings.KindCounter));
            Assert.That(row[3], Is.EqualTo("1,234"));
            Assert.That(row[4] + row[5] + row[6] + row[7] + row[8] + row[9], Is.Empty, "Counters have no statistics.");
        }

        [Test]
        public void Value_RoundsGroupsAndAddsTheUnit()
        {
            Assert.That(DiagnosticsFormat.Value(1536.256, "MB"), Is.EqualTo("1,536.26 MB"));
            Assert.That(DiagnosticsFormat.Value(3, null), Is.EqualTo("3"));
            Assert.That(DiagnosticsFormat.Value(double.NaN, "ms"), Is.EqualTo("-"));
        }

        private static PerformanceMetricsService Metrics()
        {
            return new PerformanceMetricsService(new PerformanceSettings { DetailedEnabled = true, BuiltinMetricsEnabled = false }, new StopwatchMonotonicClock(), new UtcSystemClock(), null);
        }

        private static MetricSnapshot Find(PerformanceMetricsService metrics, string name)
        {
            foreach (MetricSnapshot snapshot in metrics.GetSnapshot().Metrics)
            {
                if (snapshot.Name == name)
                {
                    return snapshot;
                }
            }

            throw new AssertionException($"No metric named {name}.");
        }
    }

    public sealed class DeveloperModeServiceTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
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
        public void Start_TellsTheCurrentState_ThenFollowsOnlyDeveloperModeChanges()
        {
            UiSettingsService settings = Settings();
            var told = new List<bool>();
            int changed = 0;
            var service = new DeveloperModeService(settings, new Action<bool>[] { told.Add }, null);
            service.Changed += _ => changed++;

            service.Start();
            Assert.That(told, Is.EqualTo(new[] { false }), "The current state is told once at start.");

            settings.SetPalette("mint");
            Assert.That(told, Has.Count.EqualTo(1), "Other settings do not reach the receivers.");

            settings.SetDeveloperMode(true);
            settings.SetDeveloperMode(false);
            Assert.That(told, Is.EqualTo(new[] { false, true, false }));
            Assert.That(changed, Is.EqualTo(2));

            service.Dispose();
            settings.SetDeveloperMode(true);
            Assert.That(told, Has.Count.EqualTo(3), "Nothing after Dispose.");
        }

        [Test]
        public void FailingReceiver_DoesNotStopTheOthers()
        {
            UiSettingsService settings = Settings();
            var told = new List<bool>();
            var service = new DeveloperModeService(settings, new Action<bool>[] { _ => throw new InvalidOperationException("broken"), told.Add }, null);

            service.Start();
            settings.SetDeveloperMode(true);

            Assert.That(told, Is.EqualTo(new[] { false, true }));
            Assert.That(service.IsEnabled, Is.True);
        }

        private UiSettingsService Settings()
        {
            var store = new ConfigFileStore<UiSettings>(_directory, UiSettings.FileName, UiSettings.SchemaVersion, () => UiSettings.CreateDefault("ja"), new UtcSystemClock());
            var service = new UiSettingsService(store, UiSettingsResolverTests.Choices(false), new StopwatchMonotonicClock(), null);
            service.Load();
            return service;
        }
    }
}
