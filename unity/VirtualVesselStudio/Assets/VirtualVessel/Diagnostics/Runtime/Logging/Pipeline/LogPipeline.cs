using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using VirtualVessel.Core.Time;

namespace VirtualVessel.Diagnostics.Logging.Pipeline
{
    /// <summary>
    /// The caller-side half of logging: level check, masking, repeat suppression, and a bounded queue.
    /// </summary>
    /// <remarks>
    /// Everything here runs on the calling thread and must never block on I/O. The writer thread
    /// drains the queue. When the queue is full, new entries are dropped and counted rather than
    /// blocking the caller, because a stalled audio or tracking thread is worse than a lost log line.
    /// </remarks>
    internal sealed class LogPipeline
    {
        // Reused per thread so that submitting an entry does not allocate a list each time.
        // Submit never re-enters itself on the same thread, so one list per thread is enough.
        [ThreadStatic]
        private static List<LogEntry> t_summaries;

        private readonly ISystemClock _systemClock;
        private readonly IMonotonicClock _monotonicClock;
        private readonly long _sessionStartTimestamp;
        private readonly int _capacity;
        private readonly ConcurrentQueue<LogEntry> _queue = new ConcurrentQueue<LogEntry>();
        private readonly ConcurrentDictionary<string, LogLevel> _moduleLevels = new ConcurrentDictionary<string, LogLevel>(StringComparer.Ordinal);
        private readonly RepeatSuppressor _repeatSuppressor;
        private readonly AutoResetEvent _entryAvailable = new AutoResetEvent(false);

        private int _minimumLevel;
        private int _moduleOverrideCount;
        private int _queuedCount;
        private long _droppedCount;

        public LogPipeline(LoggingSettings settings, ISystemClock systemClock, IMonotonicClock monotonicClock)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            _systemClock = systemClock ?? throw new ArgumentNullException(nameof(systemClock));
            _monotonicClock = monotonicClock ?? throw new ArgumentNullException(nameof(monotonicClock));
            _sessionStartTimestamp = monotonicClock.GetTimestamp();
            _capacity = settings.QueueCapacity;
            _minimumLevel = (int)settings.MinimumLevel;

            long windowTicks = (long)(settings.RepeatSuppressionWindow.TotalSeconds * monotonicClock.Frequency);
            _repeatSuppressor = new RepeatSuppressor(windowTicks);
        }

        public LogLevel MinimumLevel
        {
            get => (LogLevel)Volatile.Read(ref _minimumLevel);
            set => Volatile.Write(ref _minimumLevel, (int)value);
        }

        public long DroppedCount => Interlocked.Read(ref _droppedCount);

        public long SuppressedCount => _repeatSuppressor.SuppressedTotal;

        public int QueuedCount => Volatile.Read(ref _queuedCount);

        /// <summary>Signalled whenever an entry is enqueued. Waited on by the writer thread.</summary>
        public WaitHandle EntryAvailable => _entryAvailable;

        public void SetModuleLevel(string module, LogLevel? level)
        {
            if (module == null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            if (level.HasValue)
            {
                _moduleLevels[module] = level.Value;
            }
            else
            {
                _moduleLevels.TryRemove(module, out _);
            }

            Volatile.Write(ref _moduleOverrideCount, _moduleLevels.Count);
        }

        public bool IsEnabled(LogLevel level, string module)
        {
            // Allocation-free: an int comparison, plus a dictionary lookup only when overrides exist.
            // A plain counter is used instead of ConcurrentDictionary.IsEmpty, which takes every
            // internal lock and would make this hot check contend.
            if (Volatile.Read(ref _moduleOverrideCount) > 0 && module != null && _moduleLevels.TryGetValue(module, out LogLevel moduleLevel))
            {
                return level >= moduleLevel;
            }

            return (int)level >= Volatile.Read(ref _minimumLevel);
        }

        public void Write(
            LogLevel level,
            string module,
            string category,
            string message,
            IReadOnlyList<LogProperty> properties,
            IReadOnlyList<KeyValuePair<string, string>> context,
            Exception exception)
        {
            if (!IsEnabled(level, module))
            {
                return;
            }

            long now = _monotonicClock.GetTimestamp();
            var entry = new LogEntry(
                _systemClock.UtcNow,
                ToElapsedMilliseconds(now),
                level,
                module,
                category,
                SecretMasker.MaskText(message),
                MaskProperties(properties),
                context,
                exception?.GetType().FullName,
                null,
                Thread.CurrentThread.ManagedThreadId,
                sourceException: exception);

            Submit(entry, now);
        }

        /// <summary>
        /// Adds an entry produced outside <see cref="ILog"/>, such as a captured Unity log or a replayed
        /// startup entry. The text is masked here as well.
        /// </summary>
        public void WriteExternal(
            DateTimeOffset timestampUtc,
            LogLevel level,
            string module,
            string category,
            string message,
            string exceptionType,
            string exceptionText,
            bool excludeFromConsole)
        {
            if (!IsEnabled(level, module))
            {
                return;
            }

            long now = _monotonicClock.GetTimestamp();
            var entry = new LogEntry(
                timestampUtc,
                ToElapsedMilliseconds(now),
                level,
                module,
                category,
                SecretMasker.MaskText(message),
                null,
                null,
                exceptionType,
                SecretMasker.MaskText(exceptionText),
                Thread.CurrentThread.ManagedThreadId,
                excludeFromConsole: excludeFromConsole);

            Submit(entry, now);
        }

        /// <summary>Removes one entry. Called only by the writer thread.</summary>
        public bool TryDequeue(out LogEntry entry)
        {
            if (_queue.TryDequeue(out entry))
            {
                Interlocked.Decrement(ref _queuedCount);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Collects summaries of repeat windows that have ended. Pass <c>long.MaxValue</c> to flush all.
        /// </summary>
        public void FlushRepeatSummaries(long nowTicks, List<LogEntry> summaries)
        {
            _repeatSuppressor.Flush(nowTicks, summaries);
        }

        public long GetTimestamp()
        {
            return _monotonicClock.GetTimestamp();
        }

        /// <summary>Wakes the writer thread without enqueuing anything, for example to stop it.</summary>
        public void WakeWriter()
        {
            _entryAvailable.Set();
        }

        public LogEntry CreateInternalEntry(LogLevel level, string message)
        {
            return new LogEntry(
                _systemClock.UtcNow,
                ToElapsedMilliseconds(_monotonicClock.GetTimestamp()),
                level,
                "Diagnostics",
                "Logging",
                message,
                null,
                null,
                null,
                null,
                Thread.CurrentThread.ManagedThreadId);
        }

        private void Submit(LogEntry entry, long now)
        {
            List<LogEntry> summaries = t_summaries ??= new List<LogEntry>();
            bool write = _repeatSuppressor.ShouldWrite(entry, now, summaries);

            for (int i = 0; i < summaries.Count; i++)
            {
                Enqueue(summaries[i]);
            }

            summaries.Clear();

            if (write)
            {
                Enqueue(entry);
            }
        }

        private void Enqueue(LogEntry entry)
        {
            if (Interlocked.Increment(ref _queuedCount) > _capacity)
            {
                Interlocked.Decrement(ref _queuedCount);
                Interlocked.Increment(ref _droppedCount);
                return;
            }

            _queue.Enqueue(entry);
            _entryAvailable.Set();
        }

        private long ToElapsedMilliseconds(long timestamp)
        {
            return (long)_monotonicClock.GetElapsed(_sessionStartTimestamp, timestamp).TotalMilliseconds;
        }

        private static IReadOnlyList<LogProperty> MaskProperties(IReadOnlyList<LogProperty> properties)
        {
            if (properties == null || properties.Count == 0)
            {
                return null;
            }

            // Copy so that a caller reusing its list cannot change an entry after it was queued.
            var copy = new LogProperty[properties.Count];
            for (int i = 0; i < properties.Count; i++)
            {
                LogProperty property = properties[i];
                object value = property.Value;

                if (SecretMasker.IsSensitiveKey(property.Key))
                {
                    value = SecretMasker.Mask;
                }
                else if (value is string text)
                {
                    value = SecretMasker.MaskText(text);
                }

                copy[i] = new LogProperty(property.Key, value);
            }

            return copy;
        }
    }
}
