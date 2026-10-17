using System;

namespace VirtualVessel.Core.Lifecycle
{
    /// <summary>
    /// Registers work that runs on the main thread once per Unity frame, such as uploading captured
    /// video to a texture (application foundation detailed design 5.5).
    /// </summary>
    /// <remarks>
    /// The composition hands it to the services that need it; there is no global instance. Per-frame
    /// work must stay light and avoid allocations (CLAUDE.md chapter 23).
    /// </remarks>
    public interface IFrameLoop
    {
        /// <summary>Calls <paramref name="tick"/> every frame until the returned value is disposed.</summary>
        IDisposable Register(Action tick);
    }
}
