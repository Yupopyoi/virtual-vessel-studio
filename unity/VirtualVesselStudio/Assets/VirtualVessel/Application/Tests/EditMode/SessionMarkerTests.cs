using System;
using NUnit.Framework;
using VirtualVessel.Application.Session;

namespace VirtualVessel.Application.Tests
{
    public sealed class SessionMarkerTests
    {
        [Test]
        public void FormatThenParse_RoundTrips()
        {
            var session = new SessionInfo("abc123", new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero), null);

            PreviousSession previous = SessionMarker.Parse(SessionMarker.Format(session, "0.1.0"));

            Assert.That(previous.SessionId, Is.EqualTo("abc123"));
            Assert.That(previous.StartedAtUtc, Is.EqualTo("2026-10-13T12:00:00.0000000+00:00"));
            Assert.That(previous.ApplicationVersion, Is.EqualTo("0.1.0"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("{ not json")]
        public void Parse_UnreadableContent_StillReportsAnUnknownSession(string text)
        {
            PreviousSession previous = SessionMarker.Parse(text);

            Assert.That(previous, Is.Not.Null, "A leftover marker means an abnormal exit even when its content is unreadable.");
            Assert.That(previous.SessionId, Is.Null);
        }
    }
}
