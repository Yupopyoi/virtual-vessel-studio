using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace VirtualVessel.Capture.SubScreen
{
    /// <summary>
    /// <see cref="IDesktopDuplication"/> and <see cref="IWindowCapture"/> through VirtualVessel.Capture.Native.dll,
    /// built from native/capture (capture detailed design 8.2, 8.6). Its types stay inside this class.
    /// </summary>
    internal sealed class DesktopDuplicationNative : IDesktopDuplication, IWindowCapture
    {
        // With the extension, because Windows would read ".Native" as one and not add ".dll".
        private const string Library = "VirtualVessel.Capture.Native.dll";

        // vv_capture.h: VV_CAPTURE_API_VERSION.
        private const int ApiVersion = 3;

        // vv_capture.h: VV_CAPTURE_DRAW_POINTER.
        private const uint DrawPointerFlag = 1;

        private const int MonitorCapacity = 16;

        private const int WindowCapacity = 128;

        private bool _loaded;

        public IReadOnlyList<MonitorDescription> ListMonitors()
        {
            EnsureLoaded();
            var buffer = new NativeMonitor[MonitorCapacity];
            int count = ListMonitorsNative(buffer, buffer.Length);
            if (count < 0)
            {
                throw new InvalidOperationException($"Listing the monitors failed with result {count}.");
            }

            var monitors = new MonitorDescription[Math.Min(count, buffer.Length)];
            for (int i = 0; i < monitors.Length; i++)
            {
                NativeMonitor monitor = buffer[i];
                monitors[i] = new MonitorDescription(monitor.DeviceName, monitor.Width, monitor.Height, monitor.IsPrimary != 0);
            }

            return monitors;
        }

        public IDuplicationSession Open(string deviceName, bool drawPointer)
        {
            EnsureLoaded();
            int result = OpenNative(deviceName, drawPointer ? DrawPointerFlag : 0, out IntPtr handle);
            if (result != 0)
            {
                throw new InvalidOperationException($"Duplicating '{deviceName}' could not start: result {result}.");
            }

            return new Session(handle);
        }

        public IReadOnlyList<WindowDescription> ListWindows()
        {
            EnsureLoaded();
            var buffer = new NativeWindow[WindowCapacity];
            int count = ListWindowsNative(buffer, buffer.Length);
            if (count < 0)
            {
                throw new InvalidOperationException($"Listing the windows failed with result {count}.");
            }

            var windows = new WindowDescription[Math.Min(count, buffer.Length)];
            for (int i = 0; i < windows.Length; i++)
            {
                NativeWindow window = buffer[i];
                windows[i] = new WindowDescription(window.Executable, window.Title, window.Width, window.Height);
            }

            return windows;
        }

        public IDuplicationSession OpenWindow(string executable, bool drawPointer)
        {
            EnsureLoaded();
            int result = OpenWindowNative(executable, drawPointer ? DrawPointerFlag : 0, out IntPtr handle);
            if (result != 0)
            {
                throw new InvalidOperationException($"Capturing the window of '{executable}' could not start: result {result}.");
            }

            return new Session(handle);
        }

        private void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            int version;
            try
            {
                version = GetApiVersion();
            }
            catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException || exception is BadImageFormatException)
            {
                throw new CapturePluginMissingException($"{Library} could not be loaded. Build it with tools/native/build-capture.ps1.", exception);
            }

            if (version != ApiVersion)
            {
                throw new CapturePluginMissingException($"{Library} has interface version {version}, but version {ApiVersion} is needed. Build it again with tools/native/build-capture.ps1.");
            }

            _loaded = true;
        }

        [DllImport(Library, EntryPoint = "vv_capture_api_version", CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetApiVersion();

        [DllImport(Library, EntryPoint = "vv_capture_list_monitors", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ListMonitorsNative([Out] NativeMonitor[] monitors, int capacity);

        [DllImport(Library, EntryPoint = "vv_capture_open", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int OpenNative(string deviceName, uint flags, out IntPtr session);

        [DllImport(Library, EntryPoint = "vv_capture_list_windows", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ListWindowsNative([Out] NativeWindow[] windows, int capacity);

        [DllImport(Library, EntryPoint = "vv_capture_open_window", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int OpenWindowNative(string executable, uint flags, out IntPtr session);

        [DllImport(Library, EntryPoint = "vv_capture_close", CallingConvention = CallingConvention.Cdecl)]
        private static extern void CloseNative(IntPtr session);

        [DllImport(Library, EntryPoint = "vv_capture_get_status", CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetStatusNative(IntPtr session, out NativeStatus status);

        [DllImport(Library, EntryPoint = "vv_capture_take_frame", CallingConvention = CallingConvention.Cdecl)]
        private static extern int TakeFrameNative(IntPtr session, ulong after, out NativeFrame frame);

        // The layouts of vv_capture_monitor, vv_capture_window, vv_capture_status, and vv_capture_frame.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeMonitor
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            public int Width;
            public int Height;
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Rotation;
            public int IsPrimary;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeWindow
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string Executable;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Title;
            public uint ProcessId;
            public int Width;
            public int Height;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeStatus
        {
            public int State;
            public int LastError;
            public int Width;
            public int Height;
            public ulong FrameNumber;
            public ulong Overwritten;
            public uint Recoveries;
            public uint Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeFrame
        {
            public IntPtr Data;
            public int Width;
            public int Height;
            public ulong FrameNumber;
        }

        private sealed class Session : IDuplicationSession
        {
            private IntPtr _handle;

            public Session(IntPtr handle)
            {
                _handle = handle;
            }

            // A backstop for a session nobody disposed: its thread would keep capturing the monitor or window.
            ~Session()
            {
                Close();
            }

            public DuplicationStatus GetStatus()
            {
                int result = GetStatusNative(Handle, out NativeStatus status);
                if (result != 0)
                {
                    throw new InvalidOperationException($"Reading the duplication status failed with result {result}.");
                }

                return new DuplicationStatus((DuplicationState)status.State, status.LastError, status.Width, status.Height, status.FrameNumber, status.Overwritten, status.Recoveries);
            }

            public bool TryTakeFrame(ulong after, out DuplicationFrame frame)
            {
                int result = TakeFrameNative(Handle, after, out NativeFrame native);
                if (result < 0)
                {
                    throw new InvalidOperationException($"Taking a frame failed with result {result}.");
                }

                frame = result == 1 ? new DuplicationFrame(native.Data, native.Width, native.Height, native.FrameNumber) : default;
                return result == 1;
            }

            public void Dispose()
            {
                Close();
                GC.SuppressFinalize(this);
            }

            private IntPtr Handle => _handle != IntPtr.Zero ? _handle : throw new ObjectDisposedException(nameof(IDuplicationSession));

            private void Close()
            {
                IntPtr handle = _handle;
                _handle = IntPtr.Zero;
                if (handle != IntPtr.Zero)
                {
                    CloseNative(handle);
                }
            }
        }
    }
}
