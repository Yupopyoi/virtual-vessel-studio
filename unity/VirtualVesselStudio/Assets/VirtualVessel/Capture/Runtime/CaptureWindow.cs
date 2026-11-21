using System;

namespace VirtualVessel.Capture
{
    /// <summary>
    /// The device IDs of a game's window on this PC among the game source's devices (capture detailed design 8.6):
    /// "window:" and the program's file name, such as "window:game.exe". A window is chosen by its program, so the
    /// choice still holds when the game is started again. Pure.
    /// </summary>
    public static class CaptureWindow
    {
        public const string Prefix = "window:";

        /// <summary>The device ID of the program's window.</summary>
        public static string IdOf(string executable)
        {
            return Prefix + executable;
        }

        /// <summary>Whether a device ID is a window's rather than a capture board's.</summary>
        public static bool IsWindow(string deviceId)
        {
            return deviceId != null && deviceId.Length > Prefix.Length && deviceId.StartsWith(Prefix, StringComparison.Ordinal);
        }

        /// <summary>The program's file name of a window's device ID, or null for a capture board's.</summary>
        public static string ProgramOf(string deviceId)
        {
            return IsWindow(deviceId) ? deviceId.Substring(Prefix.Length) : null;
        }
    }
}
