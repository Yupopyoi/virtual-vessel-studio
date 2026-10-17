using System;

namespace VirtualVessel.Capture
{
    /// <summary>
    /// A source's native plugin is missing or of another version. A developer problem: the source
    /// becomes <see cref="CaptureProblem.PluginMissing"/> and the reason goes to the developer log
    /// (capture detailed design 12).
    /// </summary>
    internal sealed class CapturePluginMissingException : Exception
    {
        public CapturePluginMissingException(string message, Exception innerException = null)
            : base(message, innerException)
        {
        }
    }
}
