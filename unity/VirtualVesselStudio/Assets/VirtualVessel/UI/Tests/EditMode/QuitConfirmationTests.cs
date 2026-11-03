using NUnit.Framework;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Recording;
using VirtualVessel.UI.Screens;
using VirtualVessel.UI.Streaming;

namespace VirtualVessel.UI.Tests
{
    public sealed class QuitConfirmationTests
    {
        [TestCase(StreamingState.Idle, false)]
        [TestCase(StreamingState.Connecting, true)]
        [TestCase(StreamingState.Live, true)]
        [TestCase(StreamingState.Reconnecting, true)]
        [TestCase(StreamingState.Stopping, false)]
        [TestCase(StreamingState.Failed, false)]
        public void WouldEnd_Stream(StreamingState state, bool expected)
        {
            Assert.That(QuitConfirmation.WouldEnd(state), Is.EqualTo(expected));
        }

        [TestCase(RecordingState.Idle, false)]
        [TestCase(RecordingState.Starting, true)]
        [TestCase(RecordingState.Recording, true)]
        [TestCase(RecordingState.Stopping, false)]
        [TestCase(RecordingState.Failed, false)]
        public void WouldEnd_Recording(RecordingState state, bool expected)
        {
            Assert.That(QuitConfirmation.WouldEnd(state), Is.EqualTo(expected));
        }

        [TestCase(false, false, "quit.body", ConfirmStyle.Normal)]
        [TestCase(true, false, "quit.body.streaming", ConfirmStyle.Danger)]
        [TestCase(false, true, "quit.body.recording", ConfirmStyle.Danger)]
        [TestCase(true, true, "quit.body.streaming-recording", ConfirmStyle.Danger)]
        public void SaysWhatEnds(bool streaming, bool recording, string body, ConfirmStyle style)
        {
            Assert.That(QuitConfirmation.Request(streaming, recording).BodyKey, Is.EqualTo(body));
            Assert.That(QuitConfirmation.Style(streaming, recording), Is.EqualTo(style));
        }
    }
}
