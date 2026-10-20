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
        private readonly ExpressionKey[] _keys = new ExpressionKey[s_presets.Length];
        private readonly bool[] _has = new bool[s_presets.Length];

        public VrmAvatarExpressions(Vrm10Runtime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
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

        public void SetLookAt(float yawDegrees, float pitchDegrees)
        {
            _runtime.LookAt.SetYawPitchManually(yawDegrees, pitchDegrees);
        }
    }
}
