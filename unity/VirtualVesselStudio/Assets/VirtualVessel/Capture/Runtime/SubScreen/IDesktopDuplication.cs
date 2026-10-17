using System;
using System.Collections.Generic;

namespace VirtualVessel.Capture.SubScreen
{
    /// <summary>
    /// The boundary to the native Desktop Duplication plugin, so that tests can run the sub-screen
    /// source without it (capture detailed design 4). Main thread only.
    /// </summary>
    internal interface IDesktopDuplication
    {
        /// <summary>The monitors attached to the desktop, on every GPU.</summary>
        /// <exception cref="CapturePluginMissingException">The plugin is missing or of another version.</exception>
        IReadOnlyList<MonitorDescription> ListMonitors();

        /// <summary>Starts duplicating a monitor and returns at once; the outcome comes through the session's status.</summary>
        /// <exception cref="CapturePluginMissingException">The plugin is missing or of another version.</exception>
        IDuplicationSession Open(string deviceName);
    }

    /// <summary>One monitor being duplicated on a native thread. Disposing stops it and waits for the thread.</summary>
    internal interface IDuplicationSession : IDisposable
    {
        DuplicationStatus GetStatus();

        /// <summary>
        /// Takes the latest frame when it is newer than <paramref name="after"/>. Its memory stays
        /// valid and unchanged until the next take or until the session is disposed.
        /// </summary>
        bool TryTakeFrame(ulong after, out DuplicationFrame frame);
    }

    /// <summary>The native session's states (vv_capture.h).</summary>
    internal enum DuplicationState
    {
        Starting = 0,
        Running = 1,

        /// <summary>The duplication was lost (a mode change, the secure desktop) and is being created again.</summary>
        Recovering = 2,

        /// <summary>The monitor is not connected any more; the session has stopped.</summary>
        MonitorLost = 3,

        /// <summary>It gave up; the session has stopped.</summary>
        Failed = 4,
    }

    internal readonly struct MonitorDescription
    {
        public MonitorDescription(string deviceName, int width, int height, bool isPrimary)
        {
            DeviceName = deviceName;
            Width = width;
            Height = height;
            IsPrimary = isPrimary;
        }

        /// <summary>Such as \\.\DISPLAY2.</summary>
        public string DeviceName { get; }

        /// <summary>The current display mode in physical pixels.</summary>
        public int Width { get; }

        public int Height { get; }

        public bool IsPrimary { get; }
    }

    internal readonly struct DuplicationStatus
    {
        public DuplicationStatus(DuplicationState state, int lastError, int width, int height, ulong frameNumber, ulong overwritten, uint recoveries)
        {
            State = state;
            LastError = lastError;
            Width = width;
            Height = height;
            FrameNumber = frameNumber;
            Overwritten = overwritten;
            Recoveries = recoveries;
        }

        public DuplicationState State { get; }

        /// <summary>The HRESULT of the last failure; 0 when there is none.</summary>
        public int LastError { get; }

        public int Width { get; }

        public int Height { get; }

        /// <summary>The latest frame's number, one more per frame; 0 before the first.</summary>
        public ulong FrameNumber { get; }

        /// <summary>Frames replaced by a newer one before they were taken.</summary>
        public ulong Overwritten { get; }

        /// <summary>Times the duplication was created again after being lost.</summary>
        public uint Recoveries { get; }
    }

    /// <summary>A frame in native memory: B8G8R8A8 rows of Width * 4 bytes, top to bottom, without gaps.</summary>
    internal readonly struct DuplicationFrame
    {
        public DuplicationFrame(IntPtr data, int width, int height, ulong number)
        {
            Data = data;
            Width = width;
            Height = height;
            Number = number;
        }

        public IntPtr Data { get; }

        public int Width { get; }

        public int Height { get; }

        public ulong Number { get; }

        public int ByteCount => Width * Height * 4;
    }
}
