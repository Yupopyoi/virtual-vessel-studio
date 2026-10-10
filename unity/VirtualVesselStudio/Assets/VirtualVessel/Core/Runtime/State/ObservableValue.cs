using System;
using System.Collections.Generic;

namespace VirtualVessel.Core.State
{
    /// <summary>
    /// Writable <see cref="IReadOnlyObservable{T}"/>. The owning module keeps this and exposes it as the
    /// read-only interface.
    /// </summary>
    /// <remarks>
    /// Thread-safe. Setting an equal value does nothing. Subscribers are kept in a copy-on-write array,
    /// so a change notifies without allocating; only subscribing and unsubscribing allocate.
    /// </remarks>
    public sealed class ObservableValue<T> : IReadOnlyObservable<T>
    {
        private static readonly Action<T>[] s_noSubscribers = Array.Empty<Action<T>>();

        private readonly object _gate = new object();
        private readonly IEqualityComparer<T> _comparer;
        private T _value;
        private Action<T>[] _subscribers = s_noSubscribers;

        public ObservableValue(T initialValue = default, IEqualityComparer<T> comparer = null)
        {
            _value = initialValue;
            _comparer = comparer ?? EqualityComparer<T>.Default;
        }

        public T Value
        {
            get
            {
                lock (_gate)
                {
                    return _value;
                }
            }

            set => Set(value);
        }

        /// <summary>Changes the value and notifies subscribers. Returns false if the value was equal.</summary>
        /// <exception cref="AggregateException">
        /// One or more subscribers threw. Every subscriber is still called before this is thrown.
        /// </exception>
        public bool Set(T value)
        {
            Action<T>[] subscribers;
            lock (_gate)
            {
                if (_comparer.Equals(_value, value))
                {
                    return false;
                }

                _value = value;
                subscribers = _subscribers;
            }

            // Notify outside the lock so that a subscriber may read or set the value again.
            List<Exception> failures = null;
            for (int i = 0; i < subscribers.Length; i++)
            {
                try
                {
                    subscribers[i](value);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            if (failures != null)
            {
                throw new AggregateException("One or more subscribers of an observable value threw.", failures);
            }

            return true;
        }

        public IDisposable Subscribe(Action<T> onChanged)
        {
            if (onChanged == null)
            {
                throw new ArgumentNullException(nameof(onChanged));
            }

            lock (_gate)
            {
                var updated = new Action<T>[_subscribers.Length + 1];
                Array.Copy(_subscribers, updated, _subscribers.Length);
                updated[_subscribers.Length] = onChanged;
                _subscribers = updated;
            }

            return new Subscription(this, onChanged);
        }

        private void Unsubscribe(Action<T> onChanged)
        {
            lock (_gate)
            {
                int index = Array.IndexOf(_subscribers, onChanged);
                if (index < 0)
                {
                    return;
                }

                if (_subscribers.Length == 1)
                {
                    _subscribers = s_noSubscribers;
                    return;
                }

                var updated = new Action<T>[_subscribers.Length - 1];
                Array.Copy(_subscribers, 0, updated, 0, index);
                Array.Copy(_subscribers, index + 1, updated, index, _subscribers.Length - index - 1);
                _subscribers = updated;
            }
        }

        private sealed class Subscription : IDisposable
        {
            private ObservableValue<T> _owner;
            private readonly Action<T> _onChanged;

            public Subscription(ObservableValue<T> owner, Action<T> onChanged)
            {
                _owner = owner;
                _onChanged = onChanged;
            }

            public void Dispose()
            {
                // Idempotent: a second Dispose must not remove another subscription of the same delegate.
                ObservableValue<T> owner = System.Threading.Interlocked.Exchange(ref _owner, null);
                owner?.Unsubscribe(_onChanged);
            }
        }
    }
}
