using System;

namespace VirtualVessel.Core.State
{
    /// <summary>
    /// A value owned by a module that others can read and watch, such as a runtime state shown in the UI.
    /// </summary>
    /// <remarks>
    /// Subscribing does not deliver the current value; read <see cref="Value"/> first, then subscribe.
    /// Notifications run on the thread that changed the value, so UI code must move them to the main
    /// thread itself.
    /// </remarks>
    public interface IReadOnlyObservable<T>
    {
        T Value { get; }

        /// <summary>Calls <paramref name="onChanged"/> after every change until the result is disposed.</summary>
        IDisposable Subscribe(Action<T> onChanged);
    }
}
