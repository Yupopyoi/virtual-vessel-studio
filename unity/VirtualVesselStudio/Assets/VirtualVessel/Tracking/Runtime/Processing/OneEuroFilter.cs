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

    /// <summary>
    /// One Euro for a set of positions that make one shape, such as the body's joints or one hand's
    /// (tracking detailed design 8.2): every position moves the same share of the way each sample, with the
    /// cutoff of the fastest of them. The same low-pass filter on every joint keeps the shape: a straight
    /// arm stays straight while it moves. A cutoff per joint and axis let the slower elbow lag behind the
    /// wrist: on the user's recording "1024不自然解消用" at the steadiest level, 88 observations showed an
    /// arm bent over 15° more than it was at any time in the second before; smoothed together, 13
    /// (2026-10-24, tracking detailed design 8.2).
    /// </summary>
    /// <remarks>Holds the state of one set; the step itself is pure (<see cref="Cutoff"/>). Nothing is allocated per sample.</remarks>
    public sealed class LinkedPositionFilter
    {
        private readonly Vector3[] _values;
        private readonly Vector3[] _velocities;
        private readonly bool[] _started;
        private double _time;
        private bool _timed;

        public LinkedPositionFilter(int count)
        {
            _values = new Vector3[count];
            _velocities = new Vector3[count];
            _started = new bool[count];
        }

        /// <summary>The smoothed position <paramref name="index"/>; zero before its first sample.</summary>
        public Vector3 Value(int index)
        {
            return _values[index];
        }

        public bool IsStarted(int index)
        {
            return _started[index];
        }

        public void Reset()
        {
            Array.Clear(_values, 0, _values.Length);
            Array.Clear(_velocities, 0, _velocities.Length);
            Array.Clear(_started, 0, _started.Length);
            _timed = false;
        }

        /// <summary>
        /// Takes the samples of <paramref name="raw"/> taken at <paramref name="time"/> whose
        /// <paramref name="accepted"/> is true; the others keep their values. Only positions whose
        /// <paramref name="leads"/> is true set the cutoff, so joints guessed out of view do not quicken the
        /// rest. A position's first sample passes through. Samples not newer than the last are ignored.
        /// </summary>
        public void Next(Vector3[] raw, bool[] accepted, bool[] leads, double time, in OneEuroParameters parameters)
        {
            double elapsed = time - _time;
            if (_timed && elapsed <= 0)
            {
                return;
            }

            if (_timed)
            {
                // The speed of each position is smoothed with its direction, so noise in opposite
                // directions cancels, as the scalar filter's signed derivative does.
                float derivativeAlpha = OneEuroFilter.Alpha(parameters.DerivativeCutoff, elapsed);
                for (int i = 0; i < raw.Length; i++)
                {
                    if (accepted[i] && _started[i])
                    {
                        _velocities[i] = Vector3.Lerp(_velocities[i], (raw[i] - _values[i]) / (float)elapsed, derivativeAlpha);
                    }
                }

                float alpha = OneEuroFilter.Alpha(Cutoff(_velocities, leads, parameters), elapsed);
                for (int i = 0; i < raw.Length; i++)
                {
                    if (accepted[i] && _started[i])
                    {
                        _values[i] = Vector3.Lerp(_values[i], raw[i], alpha);
                    }
                }
            }

            for (int i = 0; i < raw.Length; i++)
            {
                if (accepted[i] && !_started[i])
                {
                    _values[i] = raw[i];
                    _velocities[i] = Vector3.zero;
                    _started[i] = true;
                }
            }

            _time = time;
            _timed = true;
        }

        /// <summary>The cutoff shared by the set: the still cutoff raised by the fastest smoothed speed among <paramref name="leads"/>. Pure.</summary>
        public static float Cutoff(Vector3[] velocities, bool[] leads, in OneEuroParameters parameters)
        {
            float fastest = 0f;
            for (int i = 0; i < velocities.Length; i++)
            {
                if (leads[i])
                {
                    fastest = Math.Max(fastest, velocities[i].magnitude);
                }
            }

            return parameters.MinCutoff + (parameters.Beta * fastest);
        }
    }
}
