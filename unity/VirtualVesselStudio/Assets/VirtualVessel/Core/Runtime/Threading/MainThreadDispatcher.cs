using System;
using System.Collections.Concurrent;
using System.Threading;

namespace VirtualVessel.Core.Threading
{
    /// <summary>
    /// Queue-based <see cref="IMainThreadDispatcher"/>. The owner of the main thread calls
    /// <see cref="Drain"/> once per frame.
    /// </summary>
    /// <remarks>
    /// Kept free of UnityEngine so that ordering and limits can be tested without Play Mode.
    /// </remarks>
    public sealed class MainThreadDispatcher : IMainThreadDispatcher
    {
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private readonly int _mainThreadId;

        public MainThreadDispatcher()
            : this(Thread.CurrentThread.ManagedThreadId)
        {
        }

        public MainThreadDispatcher(int mainThreadId)
        {
            _mainThreadId = mainThreadId;
        }

        public bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        public int PendingCount => _queue.Count;

        public void Post(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            _queue.Enqueue(action);
        }

        /// <summary>
        /// Runs up to <paramref name="maxActions"/> queued actions in posting order.
        /// </summary>
        /// <remarks>
        /// The limit keeps a burst of posts from stalling a single frame. Remaining actions run on
        /// later frames. An exception in one action is reported and does not stop the others.
        /// </remarks>
        /// <returns>The number of actions that were run.</returns>
        public int Drain(int maxActions, Action<Exception> onActionException)
        {
            if (maxActions <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxActions));
            }

            int executed = 0;
            while (executed < maxActions && _queue.TryDequeue(out Action action))
            {
                executed++;
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    onActionException?.Invoke(exception);
                }
            }

            return executed;
        }
    }
}
