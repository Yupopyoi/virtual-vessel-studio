using System;
using System.Runtime.InteropServices;

namespace VirtualVessel.UI.Settings
{
    /// <summary>
    /// Reads whether Windows asks for less motion: "Animation effects" turned off in the accessibility
    /// settings (UI foundation detailed design 7.9).
    /// </summary>
    public static class SystemMotionPreference
    {
        /// <summary>True when the OS asks for less motion. False on other platforms or when it cannot be read.</summary>
        public static bool PrefersReducedMotion()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                int animationsEnabled = 1;
                if (SystemParametersInfo(GetClientAreaAnimation, 0, ref animationsEnabled, 0))
                {
                    return animationsEnabled == 0;
                }
            }
            catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException)
            {
                // Not reachable on supported Windows versions; motion simply stays on.
            }
#endif
            return false;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // SPI_GETCLIENTAREAANIMATION: pvParam receives a BOOL that is TRUE while animations are on.
        private const uint GetClientAreaAnimation = 0x1042;

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(uint action, uint parameter, ref int value, uint winIni);
#endif
    }
}
