using System;
using System.Collections.Generic;
using UnityEngine;
using UniVRM10;

namespace VirtualVessel.Avatar.Vrm
{
    /// <summary><see cref="IAvatarExpressions"/> on VRM 1.0 expression presets and LookAt (avatar detailed design 5.2).</summary>
    internal sealed class VrmAvatarExpressions : IAvatarExpressions
    {
        private static readonly ExpressionPreset[] s_presets =
        {
            ExpressionPreset.blink,
            ExpressionPreset.blinkLeft,
            ExpressionPreset.blinkRight,
            ExpressionPreset.aa,
            ExpressionPreset.ih,
            ExpressionPreset.ou,
            ExpressionPreset.ee,
            ExpressionPreset.oh,
            ExpressionPreset.happy,
            ExpressionPreset.angry,
            ExpressionPreset.sad,
            ExpressionPreset.relaxed,
            ExpressionPreset.surprised,
            ExpressionPreset.neutral,
        };

        private readonly Vrm10Runtime _runtime;
        private readonly float _horizontalRange;
        private readonly float _upRange;
        private readonly float _downRange;
        private readonly ExpressionKey[] _keys = new ExpressionKey[s_presets.Length];
        private readonly bool[] _has = new bool[s_presets.Length];

        /// <param name="lookAt">The model's look-at settings; null uses UniVRM's default of 90 degrees.</param>
        public VrmAvatarExpressions(Vrm10Runtime runtime, VRM10ObjectLookAt lookAt)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

            // VRM maps a look angle up to each range onto the eyes' full turn; most models use 90 degrees.
            _horizontalRange = Range(Mathf.Max(Range(lookAt?.HorizontalOuter), Range(lookAt?.HorizontalInner)));
            _upRange = Range(lookAt?.VerticalUp);
            _downRange = Range(lookAt?.VerticalDown);
            var custom = new List<string>();
            var present = new HashSet<ExpressionKey>(runtime.Expression.ExpressionKeys);
            for (int i = 0; i < s_presets.Length; i++)
            {
                _keys[i] = ExpressionKey.CreateFromPreset(s_presets[i]);
                _has[i] = present.Contains(_keys[i]);
            }

            foreach (ExpressionKey key in runtime.Expression.ExpressionKeys)
            {
                if (key.Preset == ExpressionPreset.custom)
                {
                    custom.Add(key.Name);
                }
            }

            CustomNames = custom;
        }

        /// <summary>The VRM preset of each standard expression, in <see cref="AvatarExpression"/> order.</summary>
        internal static IReadOnlyList<ExpressionPreset> Presets => s_presets;

        public IReadOnlyList<string> CustomNames { get; }

        public bool Has(AvatarExpression expression)
        {
            return _has[(int)expression];
        }

        public void SetWeight(AvatarExpression expression, float weight)
        {
            int i = (int)expression;
            if (_has[i])
            {
                _runtime.Expression.SetWeight(_keys[i], Mathf.Clamp01(weight));
            }
        }

        public void SetLook(float yaw, float pitch)
        {
            (float yawDegrees, float pitchDegrees) = ToDegrees(yaw, pitch, _horizontalRange, _upRange, _downRange);
            _runtime.LookAt.SetYawPitchManually(yawDegrees, pitchDegrees);
        }

        /// <summary>
        /// The look angles for shares of the eye range: the share times the input range VRM maps onto the
        /// eyes' full turn, up and down separately. Pure.
        /// </summary>
        internal static (float Yaw, float Pitch) ToDegrees(float yaw, float pitch, float horizontalRange, float upRange, float downRange)
        {
            yaw = Mathf.Clamp(yaw, -1f, 1f);
            pitch = Mathf.Clamp(pitch, -1f, 1f);
            return (yaw * horizontalRange, pitch * (pitch >= 0f ? upRange : downRange));
        }

        private static float Range(CurveMapper mapper)
        {
            return Range(mapper?.CurveXRangeDegree ?? 0f);
        }

        private static float Range(float degrees)
        {
            return degrees > 0f ? degrees : 90f;
        }
    }
}
