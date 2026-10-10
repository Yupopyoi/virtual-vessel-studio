using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Logging.Writing;

namespace VirtualVessel.Diagnostics.Tests
{
    public sealed class FileSinkAndRetentionTests
    {
        private TempDirectory _temp;
        private LogSessionHeader _header;

        [SetUp]
        public void SetUp()
        {
            _temp = new TempDirectory();
            _header = new LogSessionHeader("3f2a9c1bdeadbeef", new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero), "0.1.0", "dev", "6000.6.4f1", "test");
        }

        [TearDown]
        public void TearDown()
        {
            _temp.Dispose();
        }

        [Test]
        public void Open_CreatesFirstPartWithHeader()
        {
            using (var sink = new JsonLinesFileSink(_temp.Path, _header, 10 * 1024))
            {
                sink.Open();

                Assert.That(Path.GetFileName(sink.CurrentFilePath), Is.EqualTo("20261010-120000_3f2a9c1b_001.jsonl"));
            }

            string[] lines = File.ReadAllLines(Directory.GetFiles(_temp.Path).Single());
            Assert.That(lines.Single(), Does.StartWith("{\"header\":true"));
        }

        [Test]
        public void Write_ExceedingSizeLimit_ContinuesInNextPartWithItsOwnHeader()
        {
            using (var sink = new JsonLinesFileSink(_temp.Path, _header, 1024))
            {
                sink.Open();
                for (int i = 0; i < 30; i++)
                {
                    sink.Write(Entry($"message number {i} with some padding to fill the file"));
                }
            }

            string[] files = Directory.GetFiles(_temp.Path).OrderBy(f => f).ToArray();
            Assert.That(files.Length, Is.GreaterThan(1));
            foreach (string file in files)
            {
                Assert.That(new FileInfo(file).Length, Is.LessThanOrEqualTo(1024));
                Assert.That(File.ReadLines(file).First(), Does.StartWith("{\"header\":true"));
            }

            int written = files.Sum(f => File.ReadLines(f).Count(line => line.Contains("message number")));
            Assert.That(written, Is.EqualTo(30), "No entry may be lost across parts.");
        }

        [Test]
        public void CurrentFile_CanBeReadWhileOpen()
        {
            using (var sink = new JsonLinesFileSink(_temp.Path, _header, 10 * 1024))
            {
                sink.Open();
                sink.Write(Entry("visible"));
                sink.Flush();

                using (var reader = new FileStream(sink.CurrentFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    Assert.That(reader.Length, Is.GreaterThan(0));
                }
            }
        }

        [Test]
        public void Retention_KeepsNewestWithinCount()
        {
            for (int i = 0; i < 5; i++)
            {
                CreateOldFile($"old{i}.jsonl", ageDays: i + 1, sizeBytes: 10);
            }

            RetentionResult result = LogFileRetention.Apply(_temp.Path, "current_", 3, TimeSpan.FromDays(30), long.MaxValue, DateTimeOffset.UtcNow);

            Assert.That(result.DeletedCount, Is.EqualTo(2));
            Assert.That(Directory.GetFiles(_temp.Path).Select(Path.GetFileName).OrderBy(n => n), Is.EqualTo(new[] { "old0.jsonl", "old1.jsonl", "old2.jsonl" }));
        }

        [Test]
        public void Retention_DeletesFilesOlderThanMaxAge()
        {
            CreateOldFile("recent.jsonl", ageDays: 1, sizeBytes: 10);
            CreateOldFile("ancient.jsonl", ageDays: 30, sizeBytes: 10);

            LogFileRetention.Apply(_temp.Path, "current_", 50, TimeSpan.FromDays(14), long.MaxValue, DateTimeOffset.UtcNow);

            Assert.That(Directory.GetFiles(_temp.Path).Select(Path.GetFileName), Is.EqualTo(new[] { "recent.jsonl" }));
        }

        [Test]
        public void Retention_RespectsTotalSize()
        {
            CreateOldFile("a.jsonl", ageDays: 1, sizeBytes: 600);
            CreateOldFile("b.jsonl", ageDays: 2, sizeBytes: 600);

            LogFileRetention.Apply(_temp.Path, "current_", 50, TimeSpan.FromDays(14), 1000, DateTimeOffset.UtcNow);

            Assert.That(Directory.GetFiles(_temp.Path).Select(Path.GetFileName), Is.EqualTo(new[] { "a.jsonl" }));
        }

        [Test]
        public void Retention_NeverDeletesCurrentSessionOrOtherFiles()
        {
            CreateOldFile("current_001.jsonl", ageDays: 100, sizeBytes: 10);
            CreateOldFile("notes.txt", ageDays: 100, sizeBytes: 10);

            LogFileRetention.Apply(_temp.Path, "current_", 1, TimeSpan.FromDays(1), 1, DateTimeOffset.UtcNow);

            Assert.That(Directory.GetFiles(_temp.Path).Select(Path.GetFileName).OrderBy(n => n), Is.EqualTo(new[] { "current_001.jsonl", "notes.txt" }));
        }

        private void CreateOldFile(string name, int ageDays, int sizeBytes)
        {
            string path = Path.Combine(_temp.Path, name);
            File.WriteAllBytes(path, new byte[sizeBytes]);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-ageDays));
        }

        private static LogEntry Entry(string message)
        {
            return new LogEntry(DateTimeOffset.UtcNow, 0, LogLevel.Information, "Test", null, message, null, null, null, null, 1);
        }
    }
}
