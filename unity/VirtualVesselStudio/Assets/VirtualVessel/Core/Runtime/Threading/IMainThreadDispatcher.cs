using System;

namespace VirtualVessel.Core.Threading
{
    /// <summary>
    /// Passes work from any thread to the Unity main thread.
    /// </summary>
    /// <remarks>
    /// <see cref="Post"/> never blocks the caller. Real-time threads such as the audio thread should
    /// post only on state changes, not on every buffer, because each post allocates a delegate.
    /// </remarks>
    public interface IMainThreadDispatcher
    {
        bool IsMainThread { get; }

        void Post(Action action);
    }
}
