using System;
using System.Collections.Generic;
using NUnit.Framework;
using VirtualVessel.Application.Logging;

namespace VirtualVessel.Application.Tests
{
    public sealed class BufferedApplicationLogTests
    {
        [Test]
        public void AttachForwarder_ReplaysBufferedEntriesAsReplayed_ThenForwardsNewOnes()
        {
            var sink = new List<string>();
            var log = new BufferedApplicationLog(() => DateTimeOffset.UtcNow, entry => sink.Add(entry.Message));
            var forwarded = new List<(string Message, bool Replayed)>();

            log.Write(ApplicationLogLevel.Information, "early 1");
            log.Write(ApplicationLogLevel.Warning, "early 2");
            log.AttachForwarder((entry, replayed) => forwarded.Add((entry.Message, replayed)));
            log.Write(ApplicationLogLevel.Information, "after attach");

            Assert.That(forwarded, Is.EqualTo(new[] { ("early 1", true), ("early 2", true), ("after attach", false) }));
            Assert.That(sink, Is.EqualTo(new[] { "early 1", "early 2" }), "After attaching, the immediate sink is no longer used.");
            Assert.That(log.Snapshot(), Is.Empty);
        }

        [Test]
        public void DetachForwarder_ReturnsToImmediateSink()
        {
            var sink = new List<string>();
            var log = new BufferedApplicationLog(() => DateTimeOffset.UtcNow, entry => sink.Add(entry.Message));
            var forwarded = new List<string>();

            log.AttachForwarder((entry, _) => forwarded.Add(entry.Message));
            log.DetachForwarder();
            log.Write(ApplicationLogLevel.Information, "after detach");

            Assert.That(forwarded, Is.Empty);
            Assert.That(sink, Is.EqualTo(new[] { "after detach" }));
        }

        [Test]
        public void Write_BeyondCapacity_CountsDropped()
        {
            var log = new BufferedApplicationLog(() => DateTimeOffset.UtcNow, null);

            for (int i = 0; i < BufferedApplicationLog.Capacity + 5; i++)
            {
                log.Write(ApplicationLogLevel.Information, "x");
            }

            Assert.That(log.Snapshot().Count, Is.EqualTo(BufferedApplicationLog.Capacity));
            Assert.That(log.DroppedCount, Is.EqualTo(5));
        }
    }
}
