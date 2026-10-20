using System;
using UnityEngine;

namespace VirtualVessel.Tracking.Processing
{
    /// <summary>
    /// Turns raw observations into the smoothed frame other modules read (tracking detailed design 8):
    /// status per part, outlier removal per joint, and One Euro smoothing. It holds the filter state of
    /// one person; each step of the work is a pure function of that state and the observation.
    /// Main thread only. Nothing is allocated per frame.
    /// </summary>
    public sealed class TrackingProcessor
    {
        /// <summary>Meters per second no joint of a person reaches (tracking detailed design 8.3).</summary>
        public const float MaxJointSpeed = 8f;

        private readonly TrackingThresholds _thresholds;
        private readonly JointsState _body = new JointsState(BodyPose.JointCount);
        private readonly JointsState _leftHand = new JointsState(HandPose.JointCount);
        private readonly JointsState _rightHand = new JointsState(HandPose.JointCount);
        private readonly OneEuroFilter[] _shapes = new OneEuroFilter[FacePose.ShapeCount];
        private PartTracking _face = PartTracking.Initial;
        private RotationFilter _head;
        private PositionFilter _headPosition;
        private OneEuroFilter _leftGazeYaw;
        private OneEuroFilter _leftGazePitch;
        private OneEuroFilter _rightGazeYaw;
        private OneEuroFilter _rightGazePitch;

        public TrackingProcessor()
            : this(TrackingThresholds.Default)
        {
        }

        public TrackingProcessor(TrackingThresholds thresholds)
        {
            _thresholds = thresholds;
        }

        /// <summary>Joint values dropped as outliers since the start; for diagnostics.</summary>
        public long Outliers { get; private set; }

        /// <summary>Forgets every part, as after stopping.</summary>
        public void Reset()
        {
            _body.Reset();
            _leftHand.Reset();
            _rightHand.Reset();
            _face = PartTracking.Initial;
            ResetFace();
        }

        /// <param name="observation">The newest raw observation, or null when none arrived this frame.</param>
        /// <param name="now">The current time on the clock of the part timestamps.</param>
        /// <param name="output">The smoothed frame; values of parts not observed stay as they were.</param>
        public void Process(TrackingFrame observation, double now, TrackingRegions supported, TrackingRegions enabled, int smoothing, TrackingFrame output)
        {
            SmoothingProfile profile = SmoothingProfile.For(smoothing);
            output.Time = now;
            if (observation != null)
            {
                output.Sequence = observation.Sequence;
            }

            ProcessBody(observation?.Body, now, supported, enabled, profile, output.Body);
            ProcessHand(_leftHand, observation?.LeftHand, TrackingRegions.LeftHand, now, supported, enabled, profile, output.LeftHand);
            ProcessHand(_rightHand, observation?.RightHand, TrackingRegions.RightHand, now, supported, enabled, profile, output.RightHand);
            ProcessFace(observation?.Face, now, supported, enabled, profile, output.Face);
        }

        private void ProcessBody(BodyPose raw, double now, TrackingRegions supported, TrackingRegions enabled, in SmoothingProfile profile, BodyPose output)
        {
            bool observed = UpdateStatus(_body, raw, TrackingRegions.Body, now, supported, enabled, output);
            if (!observed)
            {
                return;
            }

            Outliers += _body.Filter(raw.Positions, raw.Timestamp, profile.Position, output.Positions);
            Array.Copy(raw.ImagePositions, output.ImagePositions, BodyPose.JointCount);
            Array.Copy(raw.JointConfidence, output.JointConfidence, BodyPose.JointCount);
        }

        private void ProcessHand(JointsState state, HandPose raw, TrackingRegions region, double now, TrackingRegions supported, TrackingRegions enabled, in SmoothingProfile profile, HandPose output)
        {
            bool observed = UpdateStatus(state, raw, region, now, supported, enabled, output);
            if (!observed)
            {
                return;
            }

            Outliers += state.Filter(raw.Positions, raw.Timestamp, profile.Position, output.Positions);
            Array.Copy(raw.ImagePositions, output.ImagePositions, HandPose.JointCount);
            state.Palm = state.Palm.Next(raw.PalmRotation, raw.Timestamp, profile.Angle);
            output.PalmRotation = state.Palm.Value;
        }

        private void ProcessFace(FacePose raw, double now, TrackingRegions supported, TrackingRegions enabled, in SmoothingProfile profile, FacePose output)
        {
            bool observed = IsNewObservation(raw, _face);
            bool wasUsable = IsUsable(_face.Status);
            _face = Next(_face, raw, TrackingRegions.Face, observed, now, supported, enabled);
            WriteHeader(_face, observed ? raw.Confidence : output.Confidence, output);
            if (!observed)
            {
                return;
            }

            if (!wasUsable)
            {
                ResetFace();
            }

            double time = raw.Timestamp;
            _head = _head.Next(raw.HeadRotation, time, profile.Angle);
            _headPosition = _headPosition.Next(raw.HeadPosition, time, profile.Position);
            output.HeadRotation = _head.Value;
            output.HeadPosition = _headPosition.Value;
            for (int i = 0; i < FacePose.ShapeCount; i++)
            {
                _shapes[i] = _shapes[i].Next(raw.Shapes[i], time, profile.Face);
                output.Shapes[i] = _shapes[i].Value;
            }

            output.SupportedShapes = raw.SupportedShapes;
            _leftGazeYaw = _leftGazeYaw.Next(raw.LeftEyeGaze.x, time, profile.Angle);
            _leftGazePitch = _leftGazePitch.Next(raw.LeftEyeGaze.y, time, profile.Angle);
            _rightGazeYaw = _rightGazeYaw.Next(raw.RightEyeGaze.x, time, profile.Angle);
            _rightGazePitch = _rightGazePitch.Next(raw.RightEyeGaze.y, time, profile.Angle);
            output.LeftEyeGaze = new Vector2(_leftGazeYaw.Value, _leftGazePitch.Value);
            output.RightEyeGaze = new Vector2(_rightGazeYaw.Value, _rightGazePitch.Value);
        }

        private bool UpdateStatus(JointsState state, TrackedPart raw, TrackingRegions region, double now, TrackingRegions supported, TrackingRegions enabled, TrackedPart output)
        {
            bool observed = IsNewObservation(raw, state.Tracking);
            bool wasUsable = IsUsable(state.Tracking.Status);
            state.Tracking = Next(state.Tracking, raw, region, observed, now, supported, enabled);
            WriteHeader(state.Tracking, observed ? raw.Confidence : output.Confidence, output);
            if (observed && !wasUsable)
            {
                // After a loss the old values say nothing about the new ones: start the filters and
                // the outlier gates afresh instead of smoothing across the gap.
                state.ResetFilters();
            }

            return observed;
        }

        private PartTracking Next(PartTracking current, TrackedPart raw, TrackingRegions region, bool observed, double now, TrackingRegions supported, TrackingRegions enabled)
        {
            return TrackingStateMachine.Next(
                current,
                (supported & region) != 0,
                (enabled & region) != 0,
                observed,
                observed ? raw.Confidence : 0f,
                observed ? raw.Timestamp : 0,
                now,
                _thresholds);
        }

        private void ResetFace()
        {
            _head = default;
            _headPosition = default;
            Array.Clear(_shapes, 0, _shapes.Length);
            _leftGazeYaw = default;
            _leftGazePitch = default;
            _rightGazeYaw = default;
            _rightGazePitch = default;
        }

        private static bool IsNewObservation(TrackedPart raw, in PartTracking tracking)
        {
            return raw != null && raw.IsUsable && raw.Timestamp > tracking.LastObserved;
        }

        private static bool IsUsable(TrackingStatus status)
        {
            return status == TrackingStatus.Tracked || status == TrackingStatus.LowConfidence;
        }

        private static void WriteHeader(in PartTracking tracking, float confidence, TrackedPart output)
        {
            output.Status = tracking.Status;
            output.Confidence = confidence;
            output.Timestamp = double.IsNegativeInfinity(tracking.LastObserved) ? 0 : tracking.LastObserved;
        }

        /// <summary>Filter state of a set of joints: the body or one hand.</summary>
        private sealed class JointsState
        {
            private readonly PositionFilter[] _filters;
            private readonly OutlierGate[] _gates;
            private readonly Vector3[] _accepted;
            private readonly double[] _acceptedTimes;

            public JointsState(int joints)
            {
                _filters = new PositionFilter[joints];
                _gates = new OutlierGate[joints];
                _accepted = new Vector3[joints];
                _acceptedTimes = new double[joints];
            }

            public PartTracking Tracking { get; set; } = PartTracking.Initial;

            public RotationFilter Palm { get; set; }

            public void Reset()
            {
                Tracking = PartTracking.Initial;
                ResetFilters();
            }

            public void ResetFilters()
            {
                Array.Clear(_filters, 0, _filters.Length);
                Array.Clear(_gates, 0, _gates.Length);
                Palm = default;
            }

            /// <summary>Smooths the plausible joints into <paramref name="output"/>; returns how many were dropped.</summary>
            public int Filter(Vector3[] raw, double time, in OneEuroParameters parameters, Vector3[] output)
            {
                int dropped = 0;
                for (int i = 0; i < raw.Length; i++)
                {
                    // A joint dropped last time is compared with its own last accepted value and time,
                    // so the allowed distance grows with the time since then.
                    if (_filters[i].IsStarted && !_gates[i].Accept(_accepted[i], raw[i], time - _acceptedTimes[i], MaxJointSpeed, out _gates[i]))
                    {
                        dropped++;
                        continue;
                    }

                    _accepted[i] = raw[i];
                    _acceptedTimes[i] = time;
                    _filters[i] = _filters[i].Next(raw[i], time, parameters);
                    output[i] = _filters[i].Value;
                }

                return dropped;
            }
        }
    }
}
