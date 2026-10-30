using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;

namespace VirtualVessel.Video.Recording
{
    /// <summary>Reads the stream video for an encoder, a frame at a time; the service using it ticks it each frame.</summary>
    public interface IStreamFrameReader : IDisposable
    {
        /// <summary>Frames not read because two readbacks were still waiting, or the video was missing or of another size.</summary>
        long Skipped { get; }

        /// <summary>How many frames the last readback took to arrive.</summary>
        int LatencyFrames { get; }

        /// <summary>Readbacks requested and not arrived yet.</summary>
        int Waiting { get; }

        void Tick();

        /// <summary>No more frames are requested; readbacks on their way are still pushed, as the recording's last frames.</summary>
        void Stop();

        /// <summary>Nothing more is pushed: readbacks still waiting are let go when they arrive. Before the encoder closes.</summary>
        void Release();
    }

    /// <summary>Takes a frame at a time: a recording's encoder, or the stream's publisher.</summary>
    public interface IFrameSink
    {
        /// <summary>Copies the frame; false when it could not take it.</summary>
        bool Push(IntPtr data, int length, long timestamp);
    }

    /// <summary>How a frame is got from the GPU, and how it reaches the sink once it has arrived.</summary>
    public interface IFrameReadback
    {
        void Request(Texture texture, Action<AsyncGPUReadbackRequest> arrived);

        void Deliver(NativeArray<byte> data, long time, IFrameSink sink);
    }

    /// <summary>The video as it is, BGRA, for recording.</summary>
    public sealed class BgraFrameReadback : IFrameReadback
    {
        public void Request(Texture texture, Action<AsyncGPUReadbackRequest> arrived)
        {
            AsyncGPUReadback.Request(texture, 0, TextureFormat.BGRA32, arrived);
        }

        public unsafe void Deliver(NativeArray<byte> data, long time, IFrameSink sink)
        {
            sink.Push((IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(data), data.Length, time);
        }
    }

    /// <summary>When frames are due at the output rate. Pure.</summary>
    public static class FramePacing
    {
        /// <summary>
        /// Whether the frame rendered at <paramref name="now"/> is encoded, and when the next is due. A
        /// frame up to a quarter interval early counts, so a 60 fps render gives every second frame at
        /// 30 fps despite jitter; after a stall the schedule starts again from now instead of catching up.
        /// </summary>
        public static (bool Due, long Next) Pace(long now, long next, long interval)
        {
            if (now < next - (interval / 4))
            {
                return (false, next);
            }

            long following = next + interval;
            return (true, now - following >= interval ? now + interval : following);
        }
    }

    /// <summary>
    /// Reads the Stream Render Target back with AsyncGPUReadback at the output rate and pushes each frame
    /// to the sink with the shared clock's time of the frame (recording detailed design 8.1). At most
    /// two readbacks wait; the spike measured them arriving a frame later at 0.35–0.48 ms of main thread
    /// a frame (recording detailed design 1).
    /// </summary>
    public sealed class StreamFrameReader : IStreamFrameReader
    {
        public const int MaxWaiting = 2;

        private readonly IReadOnlyObservable<Texture> _video;
        private readonly int _width;
        private readonly int _height;
        private readonly IFrameSink _sink;
        private readonly IFrameReadback _readback;
        private readonly IMonotonicClock _clock;
        private readonly long _interval;

        // One callback for every request, and the requests' times in a ring, so requesting a frame does
        // not allocate a closure (recording detailed design 15). Readbacks arrive in the order requested.
        private readonly Action<AsyncGPUReadbackRequest> _arrived;
        private readonly long[] _times = new long[MaxWaiting];
        private readonly int[] _frames = new int[MaxWaiting];
        private int _first;
        private long _next;
        private bool _started;
        private bool _stopped;
        private bool _released;

        public StreamFrameReader(IReadOnlyObservable<Texture> video, int width, int height, int frameRate, IFrameSink sink, IFrameReadback readback, IMonotonicClock clock)
        {
            _video = video ?? throw new ArgumentNullException(nameof(video));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _readback = readback ?? throw new ArgumentNullException(nameof(readback));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _width = width;
            _height = height;
            _interval = clock.Frequency / Math.Max(1, frameRate);
            _arrived = Arrived;
        }

        public long Skipped { get; private set; }

        public int LatencyFrames { get; private set; }

        public int Waiting { get; private set; }

        public void Tick()
        {
            if (_stopped)
            {
                return;
            }

            long now = _clock.GetTimestamp();
            if (!_started)
            {
                _next = now;
                _started = true;
            }

            (bool due, long next) = FramePacing.Pace(now, _next, _interval);
            _next = next;
            if (!due)
            {
                return;
            }

            Texture texture = _video.Value;
            if (Waiting >= MaxWaiting || texture == null || texture.width != _width || texture.height != _height)
            {
                Skipped++;
                return;
            }

            int slot = (_first + Waiting) % MaxWaiting;
            _times[slot] = now;
            _frames[slot] = Time.frameCount;
            Waiting++;
            _readback.Request(texture, _arrived);
        }

        public void Stop()
        {
            _stopped = true;
        }

        public void Release()
        {
            _stopped = true;
            _released = true;
        }

        public void Dispose()
        {
            Release();
            (_readback as IDisposable)?.Dispose();
        }

        private void Arrived(AsyncGPUReadbackRequest request)
        {
            long time = _times[_first];
            int frame = _frames[_first];
            _first = (_first + 1) % MaxWaiting;
            Waiting--;
            if (_released)
            {
                return;
            }

            if (request.hasError)
            {
                Skipped++;
                return;
            }

            LatencyFrames = Time.frameCount - frame;
            _readback.Deliver(request.GetData<byte>(), time, _sink);
        }
    }
}
