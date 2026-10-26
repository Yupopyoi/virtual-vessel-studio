using System;
using System.Collections.Generic;
using VirtualVessel.Core.State;

namespace VirtualVessel.Tracking.Recording
{
    /// <summary>
    /// Plays recorded or synthetic observations as a provider (tracking detailed design 12), so the
    /// same movement can be reproduced without a camera. Frame times are moved so the first frame plays
    /// at the start; each frame becomes available when its time has come on <paramref name="now"/>'s clock.
    /// </summary>
    public sealed class TrackingReplayProvider : ITrackingProvider
    {
        private readonly IReadOnlyList<TrackingFrame> _frames;
        private readonly Func<double> _now;
        private readonly bool _loop;
        private readonly ObservableValue<TrackingState> _state = new ObservableValue<TrackingState>(TrackingState.Stopped);
        private double _startedAt;
        private double _loopOffset;
        private int _next;
        private int _pending = -1;
        private long _sequence;

        /// <param name="now">Seconds on the clock the processor uses.</param>
        /// <param name="loop">Start over after the last frame.</param>
        /// <param name="traits">What the recorded provider observed, so its corrections apply again; none for synthetic motion.</param>
        public TrackingReplayProvider(IReadOnlyList<TrackingFrame> frames, TrackingRegions supported, Func<double> now, bool loop, ObservationTraits traits = ObservationTraits.None)
        {
            _frames = frames ?? throw new ArgumentNullException(nameof(frames));
            _now = now ?? throw new ArgumentNullException(nameof(now));
            Supported = supported;
            Traits = traits;
            _loop = loop;
        }

        public TrackingRegions Supported { get; }

        public ObservationTraits Traits { get; }

        public IReadOnlyObservable<TrackingState> State => _state;

        public void Start(TrackingOptions options)
        {
            _startedAt = _now();
            _loopOffset = 0;
            _next = 0;
            _pending = -1;
            _state.Set(_frames.Count > 0 ? TrackingState.Running : TrackingState.Error);
        }

        /// <summary>A recording has what it has; the processor leaves out disabled parts.</summary>
        public void SetOptions(TrackingOptions options)
        {
        }

        public void Stop()
        {
            _pending = -1;
            _state.Set(TrackingState.Stopped);
        }

        public void Tick()
        {
            if (_state.Value != TrackingState.Running)
            {
                return;
            }

            double elapsed = _now() - _startedAt;
            double first = _frames[0].Time;
            while (true)
            {
                if (_next >= _frames.Count)
                {
                    if (!_loop)
                    {
                        return;
                    }

                    // The next round starts one frame interval after the last frame.
                    double length = _frames[_frames.Count - 1].Time - first + FrameInterval();
                    _loopOffset += length;
                    _next = 0;
                }

                if (_frames[_next].Time - first + _loopOffset > elapsed)
                {
                    return;
                }

                _pending = _next;
                _next++;
            }
        }

        public bool TryTakeLatest(TrackingFrame into)
        {
            if (_pending < 0)
            {
                return false;
            }

            into.CopyFrom(_frames[_pending]);
            into.ShiftTime(_startedAt + _loopOffset - _frames[0].Time);
            into.Sequence = ++_sequence;
            _pending = -1;
            return true;
        }

        public void Dispose()
        {
            Stop();
        }

        private double FrameInterval()
        {
            // At least a millisecond, so a recording whose frames share one time cannot loop forever in Tick.
            double interval = _frames.Count > 1 ? (_frames[_frames.Count - 1].Time - _frames[0].Time) / (_frames.Count - 1) : 1.0 / 30.0;
            return Math.Max(interval, 0.001);
        }
    }
}
