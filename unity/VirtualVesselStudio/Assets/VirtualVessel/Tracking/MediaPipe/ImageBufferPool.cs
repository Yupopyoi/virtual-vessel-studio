using System;
using System.Collections.Concurrent;
using System.Threading;
using Mediapipe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace VirtualVessel.Tracking.MediaPipe
{
    /// <summary>
    /// Camera pixels for inference. MediaPipe images wrap this memory without copying it and use it on
    /// MediaPipe's threads after DetectAsync returns, so it is reused only after every image made over
    /// it has been released (tracking detailed design 9.1).
    /// </summary>
    /// <remarks>
    /// A buffer is taken (reserved), then filled by a readback, then held by MediaPipe images until each
    /// is released. It is free again only when none of these applies.
    /// </remarks>
    internal sealed class ImageBuffer
    {
        private int _uses;
        private int _reserved;

        /// <summary>A field, not a property: AsyncGPUReadback writes into it by reference.</summary>
        public NativeArray<byte> Data;

        public IntPtr Pointer { get; set; }

        /// <summary>Main thread: a readback into it is pending.</summary>
        public bool Reading { get; set; }

        /// <summary>When it was handed to MediaPipe; for noticing images that are never released.</summary>
        public long HeldSince { get; private set; }

        public bool IsFree => !Reading && Volatile.Read(ref _reserved) == 0 && Volatile.Read(ref _uses) == 0;

        /// <summary>Taken and not yet free: being filled or held by MediaPipe.</summary>
        public bool InUse => !IsFree;

        public bool IsHeld => Volatile.Read(ref _uses) > 0;

        /// <summary>Taken from the pool, to be filled and handed to MediaPipe.</summary>
        public void Reserve()
        {
            Volatile.Write(ref _reserved, 1);
        }

        /// <summary>Not used after all, as when an image is dropped; on any thread.</summary>
        public void Cancel()
        {
            Volatile.Write(ref _reserved, 0);
        }

        /// <summary>
        /// Hands it to <paramref name="images"/> MediaPipe images at <paramref name="now"/> (clock ticks),
        /// on any thread. The uses are counted before the reservation ends, so it never looks free between.
        /// </summary>
        public void Hold(int images, long now)
        {
            HeldSince = now;
            Interlocked.Exchange(ref _uses, images);
            Volatile.Write(ref _reserved, 0);
        }

        /// <summary>One image over it was released, on whatever thread MediaPipe releases it.</summary>
        public void Release()
        {
            Interlocked.Decrement(ref _uses);
        }
    }

    /// <summary>
    /// A few <see cref="ImageBuffer"/>s taken in turn. MediaPipe tells that it released an image by
    /// calling a native callback with the pixel pointer and nothing else, so the pool keeps a static
    /// table from pointer to buffer for that callback.
    /// </summary>
    internal sealed class ImageBufferPool : IDisposable
    {
        /// <summary>Two images in flight and one being read.</summary>
        public const int Count = 3;

        private static readonly ConcurrentDictionary<IntPtr, ImageBuffer> s_owners = new ConcurrentDictionary<IntPtr, ImageBuffer>();

        private readonly ImageBuffer[] _buffers = new ImageBuffer[Count];

        public ImageBufferPool()
        {
            for (int i = 0; i < Count; i++)
            {
                _buffers[i] = new ImageBuffer();
            }
        }

        /// <summary>Passed to every MediaPipe image made over a buffer. Kept in a field so the delegate is never collected.</summary>
        public static ImageFrame.Deleter Deleter { get; } = OnImageReleased;

        /// <summary>Buffers taken and not yet free.</summary>
        public int InUse
        {
            get
            {
                int inUse = 0;
                foreach (ImageBuffer buffer in _buffers)
                {
                    if (buffer.InUse)
                    {
                        inUse++;
                    }
                }

                return inUse;
            }
        }

        /// <summary>Reserves a free buffer of <paramref name="bytes"/>; null when all are in use. Main thread.</summary>
        public ImageBuffer Take(int bytes)
        {
            foreach (ImageBuffer buffer in _buffers)
            {
                if (!buffer.IsFree)
                {
                    continue;
                }

                if (!buffer.Data.IsCreated || buffer.Data.Length != bytes)
                {
                    Free(buffer);
                    Allocate(buffer, bytes);
                }

                buffer.Reserve();
                return buffer;
            }

            return null;
        }

        /// <summary>The longest time, in ticks, a buffer has been held at <paramref name="now"/>; 0 when none is.</summary>
        public long LongestHeld(long now)
        {
            long longest = 0;
            foreach (ImageBuffer buffer in _buffers)
            {
                if (buffer.IsHeld)
                {
                    longest = Math.Max(longest, now - buffer.HeldSince);
                }
            }

            return longest;
        }

        /// <summary>
        /// Frees the buffers MediaPipe no longer holds; returns how many it still holds. Those are left
        /// allocated rather than freed under MediaPipe, a leak preferred to a crash. Call after closing
        /// the tasks, which releases their images.
        /// </summary>
        public int FreeAll()
        {
            int leaked = 0;
            foreach (ImageBuffer buffer in _buffers)
            {
                if (buffer.InUse)
                {
                    leaked++;
                    continue;
                }

                Free(buffer);
            }

            return leaked;
        }

        void IDisposable.Dispose()
        {
            FreeAll();
        }

        [AOT.MonoPInvokeCallback(typeof(ImageFrame.Deleter))]
        private static void OnImageReleased(IntPtr pixels)
        {
            if (s_owners.TryGetValue(pixels, out ImageBuffer buffer))
            {
                buffer.Release();
            }
        }

        private static unsafe void Allocate(ImageBuffer buffer, int bytes)
        {
            buffer.Data = new NativeArray<byte>(bytes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            buffer.Pointer = (IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(buffer.Data);
            s_owners[buffer.Pointer] = buffer;
        }

        private static void Free(ImageBuffer buffer)
        {
            if (!buffer.Data.IsCreated)
            {
                return;
            }

            s_owners.TryRemove(buffer.Pointer, out _);
            buffer.Data.Dispose();
            buffer.Pointer = IntPtr.Zero;
        }
    }
}
