using System;

namespace VirtualVessel.Tracking.Threading
{
    /// <summary>
    /// Hands the latest value from one writer thread to one reader thread (tracking detailed design 9.1).
    /// Three objects take turns: being written, latest, being read. Publishing and taking only swap
    /// references under a short lock, so neither side waits for the other to finish its work, and a
    /// value the reader has not taken is simply replaced by a newer one. No allocation after creation.
    /// </summary>
    public sealed class FrameSlot<T>
        where T : class
    {
        private readonly object _gate = new object();
        private T _writing;
        private T _latest;
        private T _reading;
        private bool _hasNew;

        public FrameSlot(Func<T> create)
        {
            if (create == null)
            {
                throw new ArgumentNullException(nameof(create));
            }

            _writing = create();
            _latest = create();
            _reading = create();
        }

        /// <summary>The object to fill. Writer thread only, until <see cref="Publish"/>.</summary>
        public T Writing => _writing;

        /// <summary>Makes the filled object the latest. Writer thread only.</summary>
        public void Publish()
        {
            lock (_gate)
            {
                (_writing, _latest) = (_latest, _writing);
                _hasNew = true;
            }
        }

        /// <summary>
        /// The latest object, if one was published since the last take. It stays the reader's until the
        /// next take. Reader thread only.
        /// </summary>
        public bool TryTake(out T latest)
        {
            lock (_gate)
            {
                if (!_hasNew)
                {
                    latest = null;
                    return false;
                }

                (_reading, _latest) = (_latest, _reading);
                _hasNew = false;
            }

            latest = _reading;
            return true;
        }
    }
}
