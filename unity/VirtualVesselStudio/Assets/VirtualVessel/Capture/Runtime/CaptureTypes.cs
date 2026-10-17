using System;

namespace VirtualVessel.Capture
{
    public enum CaptureSourceKind
    {
        /// <summary>A capture board with a game device, through WebCamTexture.</summary>
        GameCapture,

        /// <summary>A sub monitor of this PC, through DXGI Desktop Duplication.</summary>
        SubScreenCapture,
    }

    /// <summary>The states of a capture source (system design 10.24, capture detailed design 6.1).</summary>
    public enum CaptureState
    {
        /// <summary>Stopped by the user, or never started.</summary>
        Stopped,

        /// <summary>Opening the device.</summary>
        Starting,

        /// <summary>The device is open but no video is arriving: not yet, or it stopped.</summary>
        Ready,

        /// <summary>Video is arriving.</summary>
        Capturing,

        /// <summary>The chosen device or monitor is missing; checked again at intervals.</summary>
        Unavailable,

        /// <summary>It could not open, or failed for good; starting again may help.</summary>
        Error,
    }

    /// <summary>What to tell the user about a source (capture detailed design 6.2).</summary>
    public enum CaptureProblem
    {
        None,
        DeviceNotFound,
        MonitorNotFound,

        /// <summary>The capture board is open but nothing arrives: the game device may be off.</summary>
        NoSignal,
        StartFailed,

        /// <summary>The native plugin is missing: a developer problem, shown to users as StartFailed.</summary>
        PluginMissing,
    }

    /// <summary>A device a source can capture from: a capture board or a monitor.</summary>
    public sealed class CaptureDevice
    {
        public CaptureDevice(string id, string displayName)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("A device needs an id.", nameof(id));
            }

            Id = id;
            DisplayName = displayName ?? id;
        }

        /// <summary>The identifier kept in the settings, such as a camera name or \\.\DISPLAY2.</summary>
        public string Id { get; }

        public string DisplayName { get; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    /// <summary>What a source actually delivers: its resolution, frame rate, and device.</summary>
    public readonly struct CaptureSourceInfo : IEquatable<CaptureSourceInfo>
    {
        public CaptureSourceInfo(int width, int height, double frameRate, string deviceName)
        {
            Width = width;
            Height = height;
            FrameRate = frameRate;
            DeviceName = deviceName;
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>Frames per second; 0 when unknown.</summary>
        public double FrameRate { get; }

        public string DeviceName { get; }

        public bool Equals(CaptureSourceInfo other)
        {
            return Width == other.Width && Height == other.Height && FrameRate.Equals(other.FrameRate) && string.Equals(DeviceName, other.DeviceName, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is CaptureSourceInfo other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (((Width * 397) ^ Height) * 397) ^ (DeviceName?.GetHashCode() ?? 0);
            }
        }
    }
}
