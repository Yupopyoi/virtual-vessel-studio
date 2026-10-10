using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Logging.Writing;

namespace VirtualVessel.Diagnostics.Tests
{
    public sealed class LogLineFormatterTests
    {
        [Test]
        public void FormatEntry_WritesExpectedFields()
        {
            var entry = new LogEntry(
                new DateTimeOffset(2026, 10, 10, 12, 0, 0, 123, TimeSpan.Zero),
                1532,
                LogLevel.Warning,
                "Tracking",
                "MediaPipe",
                "Tracking confidence dropped.",
                new[] { new LogProperty("Confidence", 0.31) },
                new[] { new KeyValuePair<string, string>("RunId", "r1") },
                null,
                null,
                12);

            string line = LogLineFormatter.FormatEntry(entry, new StringBuilder());

            Assert.That(line, Is.EqualTo(
                "{\"ts\":\"2026-10-10T12:00:00.123Z\",\"ms\":1532,\"lvl\":\"Warning\",\"mod\":\"Tracking\",\"cat\":\"MediaPipe\"," +
                "\"msg\":\"Tracking confidence dropped.\",\"ctx\":{\"RunId\":\"r1\"},\"props\":{\"Confidence\":0.31},\"thr\":12}"));
        }

        [Test]
        public void FormatEntry_OmitsEmptyOptionalFields_AndWritesRepeatAndException()
        {
            var entry = new LogEntry(
                DateTimeOffset.UnixEpoch,
                0,
                LogLevel.Error,
                "Voice",
                null,
                "failed",
                null,
                null,
                "System.InvalidOperationException",
                "boom",
                1,
                repeatCount: 4);

            string line = LogLineFormatter.FormatEntry(entry, new StringBuilder());

            Assert.That(line, Does.Not.Contain("\"cat\""));
            Assert.That(line, Does.Not.Contain("\"ctx\""));
            Assert.That(line, Does.Not.Contain("\"props\""));
            Assert.That(line, Does.Contain("\"ex\":{\"type\":\"System.InvalidOperationException\",\"text\":\"boom\"}"));
            Assert.That(line, Does.EndWith("\"repeat\":4}"));
        }

        [TestCase("quote\"", "\"quote\\\"\"")]
        [TestCase("back\\slash", "\"back\\\\slash\"")]
        [TestCase("line\nbreak\r\ttab", "\"line\\nbreak\\r\\ttab\"")]
        [TestCase("bell\u0007", "\"bell\\u0007\"")]
        [TestCase("日本語", "\"日本語\"")]
        public void WriteString_EscapesSpecialCharacters(string input, string expected)
        {
            var builder = new StringBuilder();

            JsonWriter.WriteString(builder, input);

            Assert.That(builder.ToString(), Is.EqualTo(expected));
        }

        [Test]
        public void WriteValue_UsesInvariantCultureAndHandlesSpecialValues()
        {
            Assert.That(Write(1.5), Is.EqualTo("1.5"));
            Assert.That(Write(42L), Is.EqualTo("42"));
            Assert.That(Write(true), Is.EqualTo("true"));
            Assert.That(Write(null), Is.EqualTo("null"));
            Assert.That(Write(double.NaN), Is.EqualTo("\"NaN\""));
            Assert.That(Write(LogLevel.Error), Is.EqualTo("\"Error\""));
        }

        [Test]
        public void FormatHeader_ContainsSessionAndVersions()
        {
            var header = new LogSessionHeader("3f2a9c1b00000000", DateTimeOffset.UnixEpoch, "0.1.0", "development", "6000.6.4f1", "Windows 11");

            string line = LogLineFormatter.FormatHeader(header, 2);

            Assert.That(line, Does.StartWith("{\"header\":true,\"schemaVersion\":1,"));
            Assert.That(line, Does.Contain("\"sessionId\":\"3f2a9c1b00000000\""));
            Assert.That(line, Does.Contain("\"part\":2"));
            Assert.That(line, Does.Contain("\"unityVersion\":\"6000.6.4f1\""));
        }

        private static string Write(object value)
        {
            var builder = new StringBuilder();
            JsonWriter.WriteValue(builder, value);
            return builder.ToString();
        }
    }
}
