using System;

namespace VirtualVessel.Video
{
    /// <summary>
    /// Counts frames over windows of about a second to give the actual frame rate. Immutable: each
    /// frame returns the next meter, so counting is a pure function of the frame times.
    /// </summary>
    public readonly struct StreamFrameMeter
    {
        private readonly long _windowStart;
        private readonly int _frames;

        private StreamFrameMeter(long windowStart, int frames, double? completed)
        {
            _windowStart = windowStart;
            _frames = frames;
            Completed = completed;
        }

        /// <summary>The rate of the window that the last frame completed, or null when it completed none.</summary>
        public double? Completed { get; }

        public static StreamFrameMeter Start(long now)
        {
            return new StreamFrameMeter(now, 0, null);
        }

        public StreamFrameMeter Next(long now, long frequency)
        {
            int frames = _frames + 1;
            long elapsed = now - _windowStart;
            if (elapsed < frequency)
            {
                return new StreamFrameMeter(_windowStart, frames, null);
            }

            return new StreamFrameMeter(now, 0, Math.Round(frames * (double)frequency / elapsed, 1));
        }
    }
}
