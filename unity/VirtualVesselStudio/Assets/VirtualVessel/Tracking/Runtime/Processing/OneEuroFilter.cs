using System;
using UnityEngine;

namespace VirtualVessel.Tracking.Processing
{
    /// <summary>The two knobs of a One Euro filter, plus the cutoff used to smooth the speed.</summary>
    public readonly struct OneEuroParameters
    {
        public OneEuroParameters(float minCutoff, float beta, float derivativeCutoff = 1f)
        {
            MinCutoff = minCutoff;
            Beta = beta;
            DerivativeCutoff = derivativeCutoff;
        }

        /// <summary>Hz. The cutoff while still: lower is smoother and steadier.</summary>
        public float MinCutoff { get; }

        /// <summary>How much speed (value units per second) raises the cutoff: higher catches up faster.</summary>
        public float Beta { get; }

        /// <summary>Hz. The cutoff for the speed estimate itself.</summary>
        public float DerivativeCutoff { get; }
    }

    /// <summary>
    /// A One Euro filter (Casiez et al., 2012) for one value (tracking detailed design 8.2): a low-pass
    /// filter whose cutoff rises with speed, so slow movement is smoothed strongly and fast movement
    /// catches up. It replaces the prototype's 30-frame average, which was steady but lagged 0.5–1 s.
    /// Immutable: each sample returns the next filter.
    /// </summary>
    public readonly struct OneEuroFilter
    {
        private readonly float _derivative;
        private readonly double _time;
        private readonly bool _started;

        private OneEuroFilter(float value, float derivative, double time)
        {
            Value = value;
            _derivative = derivative;
            _time = time;
            _started = true;
        }

        /// <summary>The smoothed value; 0 before the first sample.</summary>
        public float Value { get; }

        public bool IsStarted => _started;

        /// <summary>
        /// The filter after a sample taken at <paramref name="time"/> seconds. The first sample passes
        /// through. A sample not newer than the last one is ignored, so repeated or reordered samples
        /// cannot divide by zero or run time backwards.
        /// </summary>
        public OneEuroFilter Next(float value, double time, in OneEuroParameters parameters)
        {
            if (!_started)
            {
                return new OneEuroFilter(value, 0f, time);
            }

            double elapsed = time - _time;
            if (elapsed <= 0)
            {
                return this;
            }

            float speed = (float)((value - Value) / elapsed);
            float smoothedSpeed = Mathf.Lerp(_derivative, speed, Alpha(parameters.DerivativeCutoff, elapsed));
            float cutoff = parameters.MinCutoff + (parameters.Beta * Math.Abs(smoothedSpeed));
            return new OneEuroFilter(Mathf.Lerp(Value, value, Alpha(cutoff, elapsed)), smoothedSpeed, time);
        }

        /// <summary>The smoothing factor of a first-order low-pass filter with <paramref name="cutoff"/> Hz.</summary>
        public static float Alpha(float cutoff, double elapsed)
        {
            double timeConstant = 1.0 / (2.0 * Math.PI * Math.Max(cutoff, 1e-6f));
            return (float)(1.0 / (1.0 + (timeConstant / elapsed)));
        }
    }

    /// <summary>A One Euro filter per axis of a position.</summary>
    public readonly struct PositionFilter
    {
        private readonly OneEuroFilter _x;
        private readonly OneEuroFilter _y;
        private readonly OneEuroFilter _z;

        private PositionFilter(OneEuroFilter x, OneEuroFilter y, OneEuroFilter z)
        {
            _x = x;
            _y = y;
            _z = z;
        }

        public Vector3 Value => new Vector3(_x.Value, _y.Value, _z.Value);

        public bool IsStarted => _x.IsStarted;

        public PositionFilter Next(Vector3 value, double time, in OneEuroParameters parameters)
        {
            return new PositionFilter(_x.Next(value.x, time, parameters), _y.Next(value.y, time, parameters), _z.Next(value.z, time, parameters));
        }
    }

    /// <summary>
    /// The One Euro idea for rotations: the speed is the angle between samples, and the step is a
    /// spherical interpolation. Rotations are never averaged as Euler angles, which breaks across ±180°.
    /// </summary>
    public readonly struct RotationFilter
    {
        private readonly Vector3 _velocity;
        private readonly double _time;
        private readonly bool _started;

        private RotationFilter(Quaternion value, Vector3 velocity, double time)
        {
            Value = value;
            _velocity = velocity;
            _time = time;
            _started = true;
        }

        public Quaternion Value { get; }

        public bool IsStarted => _started;

        /// <param name="parameters">Speeds are in degrees per second.</param>
        public RotationFilter Next(Quaternion value, double time, in OneEuroParameters parameters)
        {
            if (!_started)
            {
                return new RotationFilter(Quaternion.Normalize(value), Vector3.zero, time);
            }

            double elapsed = time - _time;
            if (elapsed <= 0)
            {
                return this;
            }

            // The speed is smoothed as an angular velocity with a direction, like the signed derivative
            // of the scalar filter. Smoothing only the angle (always positive) would not let noise in
            // opposite directions cancel, and would make a still value look like it is moving.
            (Quaternion.Normalize(value) * Quaternion.Inverse(Value)).ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
            {
                // q and -q are the same rotation; take the shorter way.
                angle -= 360f;
            }

            Vector3 velocity = axis * (float)(angle / elapsed);
            Vector3 smoothedVelocity = Vector3.Lerp(_velocity, velocity, OneEuroFilter.Alpha(parameters.DerivativeCutoff, elapsed));
            float cutoff = parameters.MinCutoff + (parameters.Beta * smoothedVelocity.magnitude);

            // Slerp takes the shorter way, so q and -q do not spin the result around.
            Quaternion next = Quaternion.Slerp(Value, value, OneEuroFilter.Alpha(cutoff, elapsed));
            return new RotationFilter(next, smoothedVelocity, time);
        }
    }
}
