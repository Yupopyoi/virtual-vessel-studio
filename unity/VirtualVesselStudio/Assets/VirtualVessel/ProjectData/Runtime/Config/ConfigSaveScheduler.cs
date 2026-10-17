using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using VirtualVessel.Core.Time;

namespace VirtualVessel.ProjectData.Config
{
    /// <summary>
    /// Saves a settings file once a burst of changes settles: a fixed delay after the last change, on
    /// a background thread, one save after another (UI foundation detailed design 7.9, Stage detailed
    /// design 6.3). Call <see cref="Changed"/> and <see cref="Tick"/> on the main thread.
    /// </summary>
    public sealed class ConfigSaveScheduler<T>
        where T : class
    {
        private readonly ConfigFileStore<T> _store;
        private readonly IMonotonicClock _clock;
        private readonly long _delayTicks;
        private readonly Action<Exception> _onFailure;
        private readonly ConcurrentQueue<Exception> _failures = new ConcurrentQueue<Exception>();
        private Task _saving = Task.CompletedTask;
        private T _pending;
        private long _due;

        /// <param name="onFailure">Told about each failed save, on the main thread from <see cref="Tick"/> or from <see cref="FlushAsync"/>.</param>
        public ConfigSaveScheduler(ConfigFileStore<T> store, IMonotonicClock clock, TimeSpan delay, Action<Exception> onFailure)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _delayTicks = (long)(delay.TotalSeconds * clock.Frequency);
            _onFailure = onFailure;
        }

        public string FilePath => _store.FilePath;

        public bool Pending => _pending != null;

        /// <summary>The settings changed to <paramref name="snapshot"/>, which must not change afterwards: saved after the delay.</summary>
        public void Changed(T snapshot)
        {
            _pending = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _due = _clock.GetTimestamp() + _delayTicks;
        }

        /// <summary>Once per frame: reports failed saves and starts a save when it is due.</summary>
        public void Tick()
        {
            ReportFailures();
            if (_pending != null && _clock.GetTimestamp() >= _due)
            {
                StartSave();
            }
        }

        /// <summary>Saves a waiting change now and waits for every save. For shutdown; never waits for the main thread.</summary>
        public async Task FlushAsync()
        {
            if (_pending != null)
            {
                StartSave();
            }

            // ConfigureAwait(false): the Editor's synchronous shutdown blocks the main thread on this task.
            await _saving.ConfigureAwait(false);
            ReportFailures();
        }

        private void StartSave()
        {
            T snapshot = _pending;
            _pending = null;
            _saving = _saving.ContinueWith(_ => Save(snapshot), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        private void Save(T snapshot)
        {
            try
            {
                _store.Save(snapshot);
            }
            catch (Exception exception)
            {
                _failures.Enqueue(exception);
            }
        }

        private void ReportFailures()
        {
            while (_failures.TryDequeue(out Exception failure))
            {
                _onFailure?.Invoke(failure);
            }
        }
    }
}
