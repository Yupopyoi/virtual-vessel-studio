using System;
using System.Runtime.InteropServices;

namespace VirtualVessel.Application
{
    /// <summary>
    /// The player's own top-level window.
    /// </summary>
    internal static class ApplicationWindow
    {
        private const string UnityWindowClass = "UnityWndClass";
        private const int SwHide = 0;

        /// <summary>
        /// Hides the player window. Does nothing in the Editor or off Windows.
        /// </summary>
        /// <remarks>
        /// Used when quitting: the shutdown takes the UI, the avatar, and the stage down while frames are still
        /// drawn, so without this the window shows the bare default sky for a moment before it closes.
        /// </remarks>
        public static void Hide()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            IntPtr window = FindOwnWindow();
            if (window != IntPtr.Zero)
            {
                ShowWindow(window, SwHide);
            }
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        // By process and class rather than GetActiveWindow: the quit may come while another window is active, as
        // from the taskbar's close.
        private static IntPtr FindOwnWindow()
        {
            uint processId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr found = IntPtr.Zero;
            var className = new System.Text.StringBuilder(64);
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out uint owner);
                if (owner != processId)
                {
                    return true;
                }

                className.Clear();
                GetClassName(window, className, className.Capacity);
                if (className.ToString() != UnityWindowClass)
                {
                    return true;
                }

                found = window;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, System.Text.StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);
#endif
    }
}
