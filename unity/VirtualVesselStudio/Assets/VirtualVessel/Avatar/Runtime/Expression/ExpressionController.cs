using System;
using System.Collections.Generic;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// Puts a <see cref="FaceTarget"/> and its gaze on one avatar (avatar detailed design 7.4): part by
    /// part on a model whose face shapes fit a <see cref="FacePartProfile"/>, otherwise as whole-face
    /// expressions, standing in for blinks the model lacks. Keeps the expression weights as the
    /// Expression Signal for Extension Motion later (system design 7.7).
    /// </summary>
    /// <remarks>
    /// How expressions override blinking and the mouth (VRM 1.0 overrideBlink and overrideMouth) is the
    /// model's own setting; the VRM runtime applies it. Main thread only; allocates nothing per frame.
    /// </remarks>
    public sealed class ExpressionController
    {
        private readonly IAvatarExpressions _expressions;
        private readonly IAvatarFaceShapes _shapes;
        private readonly int[] _partShapes;
        private readonly bool[] _has = new bool[FaceTarget.ExpressionCount];
        private readonly float[] _signal = new float[FaceTarget.ExpressionCount];

        /// <param name="shapes">The model's face shapes; null moves the face by whole-face expressions.</param>
        public ExpressionController(IAvatarExpressions expressions, IAvatarFaceShapes shapes = null)
        {
            _expressions = expressions ?? throw new ArgumentNullException(nameof(expressions));
            (FacePartProfile Profile, int[] Indices)? found = shapes == null ? null : FacePartProfile.Find(shapes.Names);
            if (found.HasValue)
            {
                _shapes = shapes;
                _partShapes = found.Value.Indices;
                Profile = found.Value.Profile;
            }
            for (int i = 0; i < _has.Length; i++)
            {
                _has[i] = expressions.Has((AvatarExpression)i);
            }
        }

        /// <summary>The weight of each <see cref="AvatarExpression"/> this frame: the Expression Signal.</summary>
        public IReadOnlyList<float> Signal => _signal;

        /// <summary>The convention the face is moved part by part with; null for whole-face expressions.</summary>
        public FacePartProfile Profile { get; }

        public void Apply(FaceTarget face)
        {
            Substitute(face.Weights, _has, _signal);
            bool parts = _partShapes != null;
            for (int i = 0; i < _signal.Length; i++)
            {
                // Moved part by part, the whole-face expressions stay off so they do not add up.
                _expressions.SetWeight((AvatarExpression)i, parts ? 0f : _signal[i]);
            }

            if (parts)
            {
                for (int i = 0; i < _partShapes.Length; i++)
                {
                    if (_partShapes[i] >= 0)
                    {
                        _shapes.SetWeight(_partShapes[i], face.Parts[i]);
                    }
                }
            }

            _expressions.SetLook(face.LookYaw, face.LookPitch);
        }

        /// <summary>
        /// Copies <paramref name="weights"/> into <paramref name="into"/>, moving a blink to what the model
        /// has: both eyes closing becomes a blink of each eye when the model has no blink of both, and a
        /// wink becomes the blink of both, as far as both eyes are closed, when it has no blink of each
        /// eye. Pure apart from writing the caller's buffer (CLAUDE.md 15.1, 23).
        /// </summary>
        public static void Substitute(float[] weights, bool[] has, float[] into)
        {
            Array.Copy(weights, into, weights.Length);
            int both = (int)AvatarExpression.Blink;
            int left = (int)AvatarExpression.BlinkLeft;
            int right = (int)AvatarExpression.BlinkRight;
            if (!has[both] && has[left] && has[right])
            {
                into[left] = Math.Max(into[left], into[both]);
                into[right] = Math.Max(into[right], into[both]);
                into[both] = 0f;
            }
            else if (!(has[left] && has[right]))
            {
                into[both] = Math.Max(into[both], Math.Min(into[left], into[right]));
                into[left] = 0f;
                into[right] = 0f;
            }
        }
    }
}
