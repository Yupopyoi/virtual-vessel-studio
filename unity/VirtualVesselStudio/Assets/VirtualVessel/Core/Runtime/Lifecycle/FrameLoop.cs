using System;
using System.Collections.Generic;

namespace VirtualVessel.Core.Lifecycle
{
    /// <summary>
    /// The <see cref="IFrameLoop"/> the owner of the main thread drives by calling <see cref="Tick"/>
    /// once per frame (application foundation detailed design 5.5).
    /// </summary>
    /// <remarks>
    /// Kept free of UnityEngine so that tests can call <see cref="Tick"/> directly. Ticking allocates
    /// nothing: the list of work is copied only when a registration changes.
    /// </remarks>
    public sealed class FrameLoop : IFrameLoop
    {
        private readonly List<Registration> _registrations = new List<Registration>();
        private Registration[] _snapshot = Array.Empty<Registration>();
        private bool _changed;

        public int Count => _registrations.Count;

        public IDisposable Register(Action tick)
        {
            if (tick == null)
            {
                throw new ArgumentNullException(nameof(tick));
            }

            var registration = new Registration(this, tick);
            _registrations.Add(registration);
            _changed = true;
            return registration;
        }

        /// <summary>
        /// Calls every registered piece of work once, in registration order. Work registered during the
        /// call runs from the next call; work removed during the call is skipped at once. An exception
        /// does not stop the others; <paramref name="onError"/> receives only the first exception of
        /// each registration, so a failing tick does not flood the log every frame.
        /// </summary>
        public void Tick(Action<Exception> onError)
        {
            if (_changed)
            {
                _snapshot = _registrations.ToArray();
                _changed = false;
            }

            foreach (Registration registration in _snapshot)
            {
                if (registration.Removed)
                {
                    continue;
                }

                try
                {
                    registration.Work();
                }
                catch (Exception exception)
                {
                    if (!registration.Failed)
                    {
                        registration.Failed = true;
                        onError?.Invoke(exception);
                    }
                }
            }
        }

        private void Remove(Registration registration)
        {
            if (_registrations.Remove(registration))
            {
                _changed = true;
            }
        }

        private sealed class Registration : IDisposable
        {
            private readonly FrameLoop _owner;

            public Registration(FrameLoop owner, Action work)
            {
                _owner = owner;
                Work = work;
            }

            public Action Work { get; }

            public bool Removed { get; private set; }

            public bool Failed { get; set; }

            public void Dispose()
            {
                if (Removed)
                {
                    return;
                }

                Removed = true;
                _owner.Remove(this);
            }
        }
    }
}
