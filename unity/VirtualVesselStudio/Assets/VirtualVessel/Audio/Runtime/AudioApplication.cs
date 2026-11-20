using System;

namespace VirtualVessel.Audio
{
    /// <summary>
    /// The device IDs of a program's own sound (audio detailed design 8.7): "app:" and the program's file name,
    /// such as "app:game.exe". A source with one takes only that program's sound, through Windows' process
    /// loopback, as the sound of a game on this PC. Pure.
    /// </summary>
    public static class AudioApplication
    {
        public const string Prefix = "app:";

        /// <summary>The device ID of the program's sound.</summary>
        public static string IdOf(string executable)
        {
            return Prefix + executable;
        }

        /// <summary>Whether a device ID is a program's sound rather than a device's.</summary>
        public static bool IsApplication(string deviceId)
        {
            return deviceId != null && deviceId.Length > Prefix.Length && deviceId.StartsWith(Prefix, StringComparison.Ordinal);
        }

        /// <summary>The program's file name of a program's sound, or null for a device.</summary>
        public static string ProgramOf(string deviceId)
        {
            return IsApplication(deviceId) ? deviceId.Substring(Prefix.Length) : null;
        }
    }
}
