using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VirtualVessel.Tracking.Recording
{
    /// <summary>
    /// Writes one recording file (tracking detailed design 12.2, 12.3). Observations are turned into
    /// bytes on the main thread and written on another thread, so the frame never waits for the disk.
    /// </summary>
    /// <remarks>
    /// <see cref="Append"/> is called on the main thread only. Buffers come from a pool and go back after
    /// writing, so a steady recording allocates nothing per observation (CLAUDE.md 23).
    /// </remarks>
    public sealed class TrackingRecorder
    {
        private readonly FileStream _stream;
        private readonly ConcurrentQueue<byte[]> _pending = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private readonly Task _writing;
        private volatile bool _finishing;
        private double _firstTime = double.NaN;
        private double _lastTime;

        private TrackingRecorder(FileStream stream)
        {
            _stream = stream;
            _writing = Task.Run(WriteLoopAsync);
        }

        /// <summary>The file name, for logs; never the full path, which contains the user name.</summary>
        public string Name => Path.GetFileName(_stream.Name);

        public int Frames { get; private set; }

        /// <summary>From the first to the last observation appended.</summary>
        public double Seconds => Frames < 2 ? 0 : _lastTime - _firstTime;

        /// <summary>Whether writing failed, such as on a full disk; the owner then stops the recording.</summary>
        public bool Failed => _writing.IsFaulted;

        /// <summary>Creates the file, failing if it exists, and writes the header.</summary>
        public static TrackingRecorder Create(string path, TrackingRecordingHeader header)
        {
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            try
            {
                byte[] bytes = TrackingRecordingFormat.EncodeHeader(header);
                stream.Write(bytes, 0, bytes.Length);
                return new TrackingRecorder(stream);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <summary>Queues <paramref name="frame"/> for writing. Main thread.</summary>
        public void Append(TrackingFrame frame)
        {
            if (_finishing)
            {
                return;
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(TrackingRecordingFormat.FrameSize);
            TrackingRecordingFormat.EncodeFrame(frame, buffer);
            _pending.Enqueue(buffer);
            _signal.Release();
            if (double.IsNaN(_firstTime))
            {
                _firstTime = frame.Time;
            }

            _lastTime = frame.Time;
            Frames++;
        }

        /// <summary>Writes what is queued and closes the file. Rethrows a writing failure.</summary>
        public async Task FinishAsync()
        {
            _finishing = true;
            _signal.Release();
            try
            {
                await _writing.ConfigureAwait(false);
            }
            finally
            {
                _stream.Dispose();
                _signal.Dispose();
            }
        }

        private async Task WriteLoopAsync()
        {
            while (true)
            {
                await _signal.WaitAsync().ConfigureAwait(false);
                while (_pending.TryDequeue(out byte[] buffer))
                {
                    try
                    {
                        _stream.Write(buffer, 0, TrackingRecordingFormat.FrameSize);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }

                if (_finishing && _pending.IsEmpty)
                {
                    _stream.Flush();
                    return;
                }
            }
        }
    }
}
