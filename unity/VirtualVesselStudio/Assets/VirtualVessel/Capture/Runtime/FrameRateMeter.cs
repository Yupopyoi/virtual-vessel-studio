using System;

namespace VirtualVessel.Capture
{
    /// <summary>
    /// Measures the frame rate that actually arrives, over windows of about a second. Immutable:
    /// each frame returns the next meter, so the counting is a pure function of the frame times.
    /// </summary>
    internal readonly struct FrameRateMeter
    {
        private readonly long _windowStart;
        private readonly long _lastFrame;
        private readonly int _frames;

        private FrameRateMeter(long windowStart, long lastFrame, int frames, double rate)
        {
            _windowStart = windowStart;
            _lastFrame = lastFrame;
            _frames = frames;
            Rate = rate;
        }

        /// <summary>Frames per second over the last full window, rounded to 0.1; 0 until a window is complete.</summary>
        public double Rate { get; }

        /// <summary>
        /// The meter after a frame at <paramref name="now"/>. The first frame, or one after a gap of a
        /// second or more, starts over so that the gap does not lower the rate.
        /// </summary>
        public FrameRateMeter Next(long now, long frequency)
        {
            if (_lastFrame == 0 || now - _lastFrame >= frequency)
            {
                return new FrameRateMeter(now, now, 0, 0);
            }

            int frames = _frames + 1;
            long elapsed = now - _windowStart;
            if (elapsed < frequency)
            {
                return new FrameRateMeter(_windowStart, now, frames, Rate);
            }

            double rate = Math.Round(frames * (double)frequency / elapsed, 1);
            return new FrameRateMeter(now, now, 0, rate);
        }
    }
}
