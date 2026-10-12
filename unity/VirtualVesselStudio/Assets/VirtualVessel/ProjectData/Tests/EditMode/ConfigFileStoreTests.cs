using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using VirtualVessel.Core.Time;
using VirtualVessel.ProjectData.Config;

namespace VirtualVessel.ProjectData.Tests
{
    public sealed class ConfigFileStoreTests
    {
        private string _directory;
        private FixedClock _clock;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "VirtualVesselTests", Guid.NewGuid().ToString("N"));
            _clock = new FixedClock(new DateTimeOffset(2026, 10, 12, 12, 30, 0, TimeSpan.Zero));
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
        public void Load_WithoutFile_ReturnsDefault()
        {
            ConfigLoadResult<SampleSettings> result = CreateStore().Load();

            Assert.That(result.Status, Is.EqualTo(ConfigLoadStatus.Created));
            Assert.That(result.Value.Palette, Is.EqualTo("lavender"));
            Assert.That(result.Warnings, Is.Empty);
        }

        [Test]
        public void SaveThenLoad_RoundTrips_WithVersionAndCamelCase()
        {
            ConfigFileStore<SampleSettings> store = CreateStore();
            store.Save(new SampleSettings { Palette = "mint", UiScale = 1.25, DeveloperMode = true, Recent = new List<string> { "a", "b" } });

            ConfigLoadResult<SampleSettings> result = CreateStore().Load();
            JObject file = ReadFile();

            Assert.That(result.Status, Is.EqualTo(ConfigLoadStatus.Loaded));
            Assert.That(result.Value.Palette, Is.EqualTo("mint"));
            Assert.That(result.Value.UiScale, Is.EqualTo(1.25));
            Assert.That(result.Value.DeveloperMode, Is.True);
            Assert.That(result.Value.Recent, Is.EqualTo(new[] { "a", "b" }), "Lists replace the default instead of appending to it.");
            Assert.That((int)file["schemaVersion"], Is.EqualTo(1));
            Assert.That((string)file["palette"], Is.EqualTo("mint"));
        }

        [Test]
        public void Load_OlderVersion_MigratesStepByStep()
        {
            Write("{ \"schemaVersion\": 1, \"colorScheme\": \"sky\" }");
            var applied = new List<int>();
            var migrations = new[]
            {
                new ConfigMigration(1, doc =>
                {
                    applied.Add(1);
                    doc.Rename("colorScheme", "theme.palette");
                }),
                new ConfigMigration(2, doc =>
                {
                    applied.Add(2);
                    doc.Rename("theme.palette", "palette");
                    doc.Remove("theme");
                }),
            };

            ConfigLoadResult<SampleSettings> result = CreateStore(3, migrations).Load();

            Assert.That(applied, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(result.Status, Is.EqualTo(ConfigLoadStatus.Migrated));
            Assert.That(result.FileVersion, Is.EqualTo(1));
            Assert.That(result.Value.Palette, Is.EqualTo("sky"));
        }

        [Test]
        public void Save_KeepsFieldsThisVersionDoesNotKnow()
        {
            Write("{ \"schemaVersion\": 1, \"palette\": \"peach\", \"futureOption\": 42, \"theme\": { \"known\": true, \"futureNested\": \"x\" } }");
            ConfigFileStore<SampleSettings> store = CreateStore();
            ConfigLoadResult<SampleSettings> result = store.Load();

            result.Value.Palette = "lemon";
            store.Save(result.Value);
            JObject file = ReadFile();

            Assert.That((string)file["palette"], Is.EqualTo("lemon"));
            Assert.That((int)file["futureOption"], Is.EqualTo(42));
            Assert.That((string)file["theme"]["futureNested"], Is.EqualTo("x"), "Unknown fields inside known objects are kept too.");
        }

        [Test]
        public void Load_NewerVersion_ReadsKnownFieldsAndKeepsVersionOnSave()
        {
            Write("{ \"schemaVersion\": 5, \"palette\": \"sakura\", \"addedInV5\": [1, 2] }");
            ConfigFileStore<SampleSettings> store = CreateStore();

            ConfigLoadResult<SampleSettings> result = store.Load();
            store.Save(result.Value);
            JObject file = ReadFile();

            Assert.That(result.Status, Is.EqualTo(ConfigLoadStatus.NewerVersion));
            Assert.That(result.Value.Palette, Is.EqualTo("sakura"));
            Assert.That(result.Warnings, Has.Count.EqualTo(1));
            Assert.That((int)file["schemaVersion"], Is.EqualTo(5), "Writing 1 would make version 5 migrate it again.");
            Assert.That(file["addedInV5"], Is.Not.Null);
        }

        [TestCase("{ not json")]
        [TestCase("[1, 2, 3]")]
        [TestCase("{ \"palette\": \"mint\" }")]
        [TestCase("{ \"schemaVersion\": \"one\" }")]
        [TestCase("{ \"schemaVersion\": 0 }")]
        public void Load_UnreadableFile_IsMovedAsideAndDefaultsAreUsed(string content)
        {
            Write(content);

            ConfigLoadResult<SampleSettings> result = CreateStore().Load();

            Assert.That(result.Status, Is.EqualTo(ConfigLoadStatus.Recovered));
            Assert.That(result.Value.Palette, Is.EqualTo("lavender"));
            Assert.That(result.MovedAsidePath, Does.Contain("settings.broken-"));
            Assert.That(File.Exists(result.MovedAsidePath), Is.True);
            Assert.That(File.ReadAllText(result.MovedAsidePath), Is.EqualTo(content), "The broken file is kept for investigation.");
            Assert.That(File.Exists(Path.Combine(_directory, "settings.json")), Is.False);
            Assert.That(result.Warnings, Has.Count.EqualTo(2));
        }

        [Test]
        public void Load_TwoBrokenFilesInTheSameSecond_DoNotOverwriteEachOther()
        {
            Write("{ broken");
            string first = CreateStore().Load().MovedAsidePath;
            Write("{ broken again");
            string second = CreateStore().Load().MovedAsidePath;

            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(File.Exists(first) && File.Exists(second), Is.True);
        }

        [Test]
        public void Load_FailingMigration_RecoversWithDefaults()
        {
            Write("{ \"schemaVersion\": 1, \"palette\": \"mint\" }");
            var migrations = new[] { new ConfigMigration(1, _ => throw new InvalidOperationException("bad step")) };

            ConfigLoadResult<SampleSettings> result = CreateStore(2, migrations).Load();

            Assert.That(result.Status, Is.EqualTo(ConfigLoadStatus.Recovered));
            Assert.That(result.Warnings[0], Does.Contain("bad step"));
        }

        [Test]
        public void Load_FieldWithWrongType_FallsBackForThatFieldOnly()
        {
            Write("{ \"schemaVersion\": 1, \"palette\": \"sky\", \"uiScale\": \"big\" }");

            ConfigLoadResult<SampleSettings> result = CreateStore().Load();

            Assert.That(result.Status, Is.EqualTo(ConfigLoadStatus.Loaded));
            Assert.That(result.Value.Palette, Is.EqualTo("sky"));
            Assert.That(result.Value.UiScale, Is.EqualTo(1.0));
            Assert.That(result.Warnings.Any(w => w.Contains("uiScale")), Is.True);
        }

        [Test]
        public void Save_WhenTargetIsLocked_LeavesOriginalAndNoTemporaryFile()
        {
            ConfigFileStore<SampleSettings> store = CreateStore();
            store.Save(new SampleSettings { Palette = "mint" });
            string path = Path.Combine(_directory, "settings.json");
            string before = File.ReadAllText(path);

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Catch<IOException>(() => store.Save(new SampleSettings { Palette = "lemon" }));
            }

            Assert.That(File.ReadAllText(path), Is.EqualTo(before));
            Assert.That(File.Exists(path + ".tmp"), Is.False);
        }

        [Test]
        public void LeftoverTemporaryFile_IsIgnoredAndOverwritten()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, "settings.json.tmp"), "{ half written");
            ConfigFileStore<SampleSettings> store = CreateStore();

            ConfigLoadResult<SampleSettings> result = store.Load();
            store.Save(new SampleSettings { Palette = "sky" });

            Assert.That(result.Status, Is.EqualTo(ConfigLoadStatus.Created));
            Assert.That((string)ReadFile()["palette"], Is.EqualTo("sky"));
            Assert.That(File.Exists(Path.Combine(_directory, "settings.json.tmp")), Is.False);
        }

        [Test]
        public void Constructor_RejectsMissingOrDuplicateMigrations()
        {
            Assert.Throws<ArgumentException>(() => CreateStore(3, new[] { new ConfigMigration(1, _ => { }) }));
            Assert.Throws<ArgumentException>(() => CreateStore(2, new[] { new ConfigMigration(1, _ => { }), new ConfigMigration(1, _ => { }) }));
            Assert.Throws<ArgumentException>(() => CreateStore(2, new[] { new ConfigMigration(1, _ => { }), new ConfigMigration(2, _ => { }) }));
        }

        [Test]
        public void Load_DateLikeString_KeepsItsText()
        {
            // Regression: the default JSON reader turned this into a date and Palette became "10/13/2026 12:00:00".
            Write("{ \"schemaVersion\": 1, \"palette\": \"2026-10-13T12:00:00Z\", \"note\": \"2026-10-13\" }");
            ConfigFileStore<SampleSettings> store = CreateStore();

            ConfigLoadResult<SampleSettings> result = store.Load();
            store.Save(result.Value);

            Assert.That(result.Value.Palette, Is.EqualTo("2026-10-13T12:00:00Z"));
            Assert.That(ConfigDocument.Parse(File.ReadAllText(Path.Combine(_directory, "settings.json"))).Get<string>("note"), Is.EqualTo("2026-10-13"), "Unknown fields are written back unchanged.");
        }

        [Test]
        public void ConfigDocument_Parse_ReadsObjectsOnly()
        {
            ConfigDocument document = ConfigDocument.Parse("{ \"strings\": { \"live.start\": \"Go live\", \"day\": \"2026-10-13\" } }");
            Dictionary<string, string> strings = document.Get<Dictionary<string, string>>("strings");

            Assert.That(strings["live.start"], Is.EqualTo("Go live"), "Keys may contain dots when read as a whole object.");
            Assert.That(strings["day"], Is.EqualTo("2026-10-13"));
            Assert.Throws<FormatException>(() => ConfigDocument.Parse("[1, 2]"));
            Assert.Throws<FormatException>(() => ConfigDocument.Parse("{ \"a\": 1 } trailing"));
            Assert.Throws<FormatException>(() => ConfigDocument.Parse(""));
            Assert.Throws<ArgumentNullException>(() => ConfigDocument.Parse(null));
        }

        [Test]
        public void ConfigDocument_GetSetRemoveRename()
        {
            var document = new ConfigDocument(JObject.Parse("{ \"a\": { \"b\": 3 }, \"s\": \"x\" }"));

            document.Set("c.d", true);
            bool renamed = document.Rename("a.b", "e");
            bool removed = document.Remove("s");

            Assert.That(document.Get<bool>("c.d"), Is.True);
            Assert.That(renamed && removed, Is.True);
            Assert.That(document.Get<int>("e"), Is.EqualTo(3));
            Assert.That(document.Contains("a.b"), Is.False);
            Assert.That(document.Contains("s"), Is.False);
            Assert.That(document.Get("missing", "fallback"), Is.EqualTo("fallback"));
            Assert.That(document.Get("e", -1.5), Is.EqualTo(3.0));
        }

        private ConfigFileStore<SampleSettings> CreateStore(int version = 1, IEnumerable<ConfigMigration> migrations = null)
        {
            return new ConfigFileStore<SampleSettings>(_directory, "settings.json", version, () => new SampleSettings(), _clock, migrations);
        }

        private void Write(string content)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, "settings.json"), content);
        }

        private JObject ReadFile()
        {
            return JObject.Parse(File.ReadAllText(Path.Combine(_directory, "settings.json")));
        }

        public sealed class SampleSettings
        {
            public string Palette { get; set; } = "lavender";

            public double UiScale { get; set; } = 1.0;

            public bool DeveloperMode { get; set; }

            public List<string> Recent { get; set; } = new List<string> { "default" };

            public SampleTheme Theme { get; set; } = new SampleTheme();
        }

        public sealed class SampleTheme
        {
            public bool Known { get; set; }
        }

        private sealed class FixedClock : ISystemClock
        {
            public FixedClock(DateTimeOffset now)
            {
                UtcNow = now;
            }

            public DateTimeOffset UtcNow { get; }
        }
    }
}
