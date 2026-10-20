using System;

namespace VirtualVessel.Capture
{
    /// <summary>
    /// The device is already open in another source of this application, such as the same camera
    /// chosen for the game screen and for tracking (tracking detailed design 5.4).
    /// </summary>
    internal sealed class CaptureDeviceInUseException : InvalidOperationException
    {
        public CaptureDeviceInUseException(string deviceName)
            : base($"The device '{deviceName}' is already open in another capture source.")
        {
        }
    }
}
