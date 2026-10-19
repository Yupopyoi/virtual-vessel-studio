using System;

namespace VirtualVessel.Tracking.MediaPipe
{
    /// <summary>A camera image waiting for its person mask, and the buffer its painted copy goes into.</summary>
    internal sealed class PendingImage
    {
        public PendingImage(long milliseconds, ImageBuffer input, ImageBuffer output, int width, int height)
        {
            Milliseconds = milliseconds;
            Input = input;
            Output = output;
            Width = width;
            Height = height;
        }

        public long Milliseconds { get; }

        public ImageBuffer Input { get; }

        public ImageBuffer Output { get; }

        public int Width { get; }

        public int Height { get; }
    }

    /// <summary>
    /// Images waiting for their person masks, found again by their timestamp, which is all a result
    /// carries. Added on the main thread and taken on MediaPipe's; a few at most, so a short lock.
    /// </summary>
    internal sealed class PendingImages
    {
        private const int Capacity = 4;

        private readonly object _gate = new object();
        private readonly PendingImage[] _images = new PendingImage[Capacity];

        public void Add(PendingImage image)
        {
            lock (_gate)
            {
                for (int i = 0; i < Capacity; i++)
                {
                    if (_images[i] == null)
                    {
                        _images[i] = image;
                        return;
                    }
                }

                // Never more than the images in flight; replacing the oldest keeps going if MediaPipe
                // dropped one without a result.
                int oldest = 0;
                for (int i = 1; i < Capacity; i++)
                {
                    if (_images[i].Milliseconds < _images[oldest].Milliseconds)
                    {
                        oldest = i;
                    }
                }

                _images[oldest].Output.Cancel();
                _images[oldest] = image;
            }
        }

        /// <summary>The image of <paramref name="milliseconds"/>, removed; null when there is none.</summary>
        public PendingImage Take(long milliseconds)
        {
            lock (_gate)
            {
                for (int i = 0; i < Capacity; i++)
                {
                    if (_images[i] != null && _images[i].Milliseconds == milliseconds)
                    {
                        PendingImage image = _images[i];
                        _images[i] = null;
                        return image;
                    }
                }

                return null;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                Array.Clear(_images, 0, Capacity);
            }
        }
    }
}
