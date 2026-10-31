using System.Collections.Generic;
using VirtualVessel.Tracking;
using VirtualVessel.Tracking.Processing;
using VirtualVessel.Tracking.Recording;

namespace VirtualVessel.Avatar.Tests
{
    /// <summary>
    /// One recording as the app plays it: each observation corrected for its kind of input (tracking
    /// detailed design 8.4), flattened for 2D (8.6), and smoothed, then read 60 frames a second.
    /// </summary>
    internal sealed class RecordingReplay
    {
        private readonly TrackingRecordingHeader _header;
        private readonly List<TrackingFrame> _frames;
        private readonly ObservationCorrections _corrections;
        private readonly TrackingProcessor _processor = new TrackingProcessor();
        private readonly TrackingFrame _corrected = new TrackingFrame();
        private readonly int _smoothing;
        private readonly bool _flat;

        public RecordingReplay(string path, int smoothing, bool flat)
        {
            (_header, _frames) = TrackingRecordingFile.ReadAll(path);
            _corrections = ObservationCorrections.For(_header.Traits, ObservationCorrections.Catalog);
            _smoothing = smoothing;
            _flat = flat;
        }

        /// <summary>The smoothed frame, as of the last time <see cref="Frames"/> gave.</summary>
        public TrackingFrame Output { get; } = new TrackingFrame();

        /// <summary>When the first observation was taken.</summary>
        public double Start => _frames[0].Time;

        /// <summary>Seconds from the first observation to the last.</summary>
        public double Length => _frames[_frames.Count - 1].Time - Start;

        /// <summary>The render frames' times, from the first observation to the last; each after the observations taken by then are processed.</summary>
        public IEnumerable<double> Frames()
        {
            int next = 0;
            for (double now = Start; now <= _frames[_frames.Count - 1].Time; now += 1.0 / 60.0)
            {
                for (; next < _frames.Count && _frames[next].Time <= now; next++)
                {
                    _corrections.Apply(_frames[next], _corrected);
                    if (_flat)
                    {
                        BodyFlattening.Flatten(_corrected.Body);
                    }

                    _processor.Process(_corrected, _frames[next].Time, _header.Supported, TrackingRegions.All, _smoothing, Output);
                }

                yield return now;
            }
        }
    }
}
