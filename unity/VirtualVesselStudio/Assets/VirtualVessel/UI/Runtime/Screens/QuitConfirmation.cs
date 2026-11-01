using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Recording;
using VirtualVessel.UI.Streaming;

namespace VirtualVessel.UI.Screens
{
    /// <summary>What the quit dialog says, from what is running (components.md 3.16). Pure.</summary>
    public static class QuitConfirmation
    {
        public const string ItemText = "nav.quit";
        public const string ItemTooltip = "tip.nav.quit";

        private const string Title = "quit.title";
        private const string Confirm = "quit.confirm";
        private const string Body = "quit.body";
        private const string BodyStreaming = "quit.body.streaming";
        private const string BodyRecording = "quit.body.recording";
        private const string BodyStreamingAndRecording = "quit.body.streaming-recording";

        /// <summary>Whether quitting would end a stream. A stream already stopping ends anyway.</summary>
        public static bool WouldEnd(StreamingState state)
        {
            return state == StreamingState.Connecting || state == StreamingState.Live || state == StreamingState.Reconnecting;
        }

        /// <summary>Whether quitting would end a recording. One already stopping ends anyway.</summary>
        public static bool WouldEnd(RecordingState state)
        {
            return state == RecordingState.Starting || state == RecordingState.Recording;
        }

        public static ConfirmationRequest Request(bool streaming, bool recording)
        {
            string body = streaming && recording ? BodyStreamingAndRecording
                : streaming ? BodyStreaming
                : recording ? BodyRecording
                : Body;
            return new ConfirmationRequest(Title, body, Confirm);
        }

        /// <summary>Danger only when something in progress ends, so that Cancel gets focus then.</summary>
        public static ConfirmStyle Style(bool streaming, bool recording)
        {
            return streaming || recording ? ConfirmStyle.Danger : ConfirmStyle.Normal;
        }
    }
}
