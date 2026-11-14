using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VirtualVessel.Application
{
    /// <summary>
    /// The player's own top-level window.
    /// </summary>
    internal static class ApplicationWindow
    {
        private const string UnityWindowClass = "UnityWndClass";
        private const int SwHide = 0;
        private const int SwMaximize = 3;
        private const int RestoredWidth = 1280;
        private const int RestoredHeight = 720;

        /// <summary>
        /// Opens the player as a normal window, maximized, so the taskbar stays. Does nothing in the Editor or off
        /// Windows. Run as a coroutine from startup.
        /// </summary>
        /// <remarks>
        /// The app is used beside a browser, the chat, and the game, so it is a window rather than a full screen
        /// that covers the taskbar (user decision, 2026-11-14). The player settings ask for a window, but Unity
        /// restores the last mode it saved for the user, and earlier versions saved full screen.
        /// </remarks>
        public static IEnumerator OpenMaximized()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (Screen.fullScreenMode != FullScreenMode.Windowed)
            {
                // The size it returns to when restored; the full-screen size would not fit inside the borders.
                Screen.SetResolution(RestoredWidth, RestoredHeight, FullScreenMode.Windowed);

                // The mode changes at the end of the frame, and would undo a maximize made before it.
                yield return null;
                yield return null;
            }

            IntPtr window = FindOwnWindow();
            if (window != IntPtr.Zero)
            {
                ShowWindow(window, SwMaximize);
            }
#endif
            yield break;
        }

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
