using System;
using System.Collections.Generic;
using System.Threading;
using VirtualVessel.Diagnostics.Logging.Pipeline;

namespace VirtualVessel.Diagnostics.Logging.Writing
{
    /// <summary>
    /// Drains the pipeline queue into the sinks on a dedicated background thread, so that file I/O
    /// never runs on the main, audio, or tracking threads (system design 15.7).
    /// </summary>
    internal sealed class LogWriterThread
    {
        // The writer wakes at least this often to emit summaries of repeat windows that have ended.
        private static readonly TimeSpan s_housekeepingInterval = TimeSpan.FromSeconds(1);

        private readonly LogPipeline _pipeline;
        private readonly List<ILogSink> _sinks;
        private readonly Action<string> _reportFailure;
        private readonly List<LogEntry> _summaries = new List<LogEntry>();
        private readonly object _sinkGate = new object();

        private Thread _thread;
        private volatile bool _stopRequested;
        private long _reportedDropCount;
        private long _writtenCount;

        /// <param name="reportFailure">
        /// Receives sink failures. Must not log through the pipeline, which may be the thing failing.
        /// </param>
        public LogWriterThread(LogPipeline pipeline, IEnumerable<ILogSink> sinks, Action<string> reportFailure)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _sinks = new List<ILogSink>(sinks ?? throw new ArgumentNullException(nameof(sinks)));
            _reportFailure = reportFailure;
        }

        public long WrittenCount => Interlocked.Read(ref _writtenCount);

        public IReadOnlyList<string> ActiveSinkNames
        {
            get
            {
                lock (_sinkGate)
                {
                    return _sinks.ConvertAll(sink => sink.Name);
                }
            }
        }

        public void Start()
        {
            if (_thread != null)
            {
                throw new InvalidOperationException("The writer thread has already been started.");
            }

            _thread = new Thread(Run)
            {
                Name = "VirtualVessel.LogWriter",
                IsBackground = true,
            };
            _thread.Start();
        }

        /// <summary>
        /// Writes everything still queued, then stops. Returns false if the thread did not finish within
        /// <paramref name="timeout"/>; entries still queued at that point are lost.
        /// </summary>
        public bool Stop(TimeSpan timeout)
        {
            if (_thread == null)
            {
                return true;
            }

            _stopRequested = true;
            _pipeline.WakeWriter();
            bool finished = _thread.Join(timeout);
            if (finished)
            {
                _thread = null;
            }

            return finished;
        }

        /// <summary>Writes queued entries on the calling thread. Used only when the thread is not running, such as in tests.</summary>
        internal void DrainForTesting()
        {
            DrainQueue();
            WriteSummaries(_pipeline.GetTimestamp());
            FlushSinks();
        }

        private void Run()
        {
            while (!_stopRequested)
            {
                _pipeline.EntryAvailable.WaitOne(s_housekeepingInterval);
                DrainQueue();
                ReportDrops();
                WriteSummaries(_pipeline.GetTimestamp());
                FlushSinks();
            }

            // Final pass: everything enqueued before the stop request, plus every open repeat window.
            DrainQueue();
            ReportDrops();
            WriteSummaries(long.MaxValue);
            FlushSinks();
        }

        private void DrainQueue()
        {
            while (_pipeline.TryDequeue(out LogEntry entry))
            {
                WriteToSinks(entry);
            }
        }

        private void ReportDrops()
        {
            long dropped = _pipeline.DroppedCount;
            long newlyDropped = dropped - _reportedDropCount;
            if (newlyDropped <= 0)
            {
                return;
            }

            _reportedDropCount = dropped;
            WriteToSinks(_pipeline.CreateInternalEntry(
                LogLevel.Warning,
                $"{newlyDropped} log entries were dropped because the log queue was full."));
        }

        private void WriteSummaries(long nowTicks)
        {
            _summaries.Clear();
            _pipeline.FlushRepeatSummaries(nowTicks, _summaries);
            foreach (LogEntry summary in _summaries)
            {
                WriteToSinks(summary);
            }
        }

        private void WriteToSinks(LogEntry entry)
        {
            lock (_sinkGate)
            {
                for (int i = _sinks.Count - 1; i >= 0; i--)
                {
                    ILogSink sink = _sinks[i];
                    try
                    {
                        sink.Write(entry);
                    }
                    catch (Exception exception)
                    {
                        DisableSink(i, exception);
                    }
                }
            }

            Interlocked.Increment(ref _writtenCount);
        }

        private void FlushSinks()
        {
            lock (_sinkGate)
            {
                for (int i = _sinks.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        _sinks[i].Flush();
                    }
                    catch (Exception exception)
                    {
                        DisableSink(i, exception);
                    }
                }
            }
        }

        private void DisableSink(int index, Exception exception)
        {
            ILogSink sink = _sinks[index];
            _sinks.RemoveAt(index);
            try
            {
                sink.Dispose();
            }
            catch (Exception)
            {
                // The sink is already broken; a second failure while disposing adds no information.
            }

            _reportFailure?.Invoke($"Log sink '{sink.Name}' failed and was disabled: {exception}");
        }
    }
}
