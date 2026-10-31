using System.Diagnostics;

namespace VirtualVessel.UI.Dialogs
{
    /// <summary>Pages of the system's own settings that the user changes there, such as microphone access.</summary>
    public interface ISystemSettings
    {
        /// <summary>Opens Windows' microphone privacy page, where access for desktop apps is allowed.</summary>
        void OpenMicrophonePrivacy();
    }

    /// <summary>Windows Settings, started for the user; the application does not own or stop it.</summary>
    public sealed class WindowsSystemSettings : ISystemSettings
    {
        public const string MicrophonePrivacyUri = "ms-settings:privacy-microphone";

        public void OpenMicrophonePrivacy()
        {
            using (Process.Start(new ProcessStartInfo(MicrophonePrivacyUri) { UseShellExecute = true }))
            {
            }
        }
    }
}
