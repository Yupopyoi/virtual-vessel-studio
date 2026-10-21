using System;
using UnityEngine;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// What the face of the avatar should show: a weight per <see cref="AvatarExpression"/> for models
    /// with whole-face expressions, a weight per <see cref="FacePart"/> for models moved part by part,
    /// the model-independent <see cref="ExpressionSignal"/>, the gaze, and the head's rotation in the
    /// avatar root's space (avatar detailed design 7.1, 7.4). Objects are reused every frame, never
    /// allocated per observation (CLAUDE.md 23).
    /// </summary>
    public sealed class FaceTarget
    {
        public static readonly int ExpressionCount = Enum.GetValues(typeof(AvatarExpression)).Length;
        public static readonly int SignalCount = Enum.GetValues(typeof(ExpressionSignal)).Length;

        /// <summary>0 to 1 per <see cref="AvatarExpression"/>.</summary>
        public float[] Weights { get; } = new float[ExpressionCount];

        /// <summary>0 to 1 per <see cref="FacePart"/>.</summary>
        public float[] Parts { get; } = new float[FacePartProfile.PartCount];

        /// <summary>0 to 1 per <see cref="ExpressionSignal"/>: the Expression Signal.</summary>
        public float[] Signals { get; } = NeutralSignals();

        /// <summary>A share of the model's eye range, -1 to 1, positive toward the avatar's own right.</summary>
        public float LookYaw { get; set; }

        /// <summary>A share of the model's eye range, -1 to 1, positive up.</summary>
        public float LookPitch { get; set; }

        /// <summary>In the avatar root's space; identity looks straight ahead.</summary>
        public Quaternion Head { get; set; } = Quaternion.identity;

        public float this[AvatarExpression expression]
        {
            get => Weights[(int)expression];
            set => Weights[(int)expression] = value;
        }

        public float this[FacePart part]
        {
            get => Parts[(int)part];
            set => Parts[(int)part] = value;
        }

        public float this[ExpressionSignal signal]
        {
            get => Signals[(int)signal];
            set => Signals[(int)signal] = value;
        }

        /// <summary>No expression, eyes open and ahead, head straight: what a lost face goes back to (avatar detailed design 8.4).</summary>
        public void SetNeutral()
        {
            Array.Clear(Weights, 0, Weights.Length);
            Array.Clear(Parts, 0, Parts.Length);
            Array.Clear(Signals, 0, Signals.Length);
            this[ExpressionSignal.EyeOpenLeft] = 1f;
            this[ExpressionSignal.EyeOpenRight] = 1f;
            LookYaw = 0f;
            LookPitch = 0f;
            Head = Quaternion.identity;
        }

        public void CopyFrom(FaceTarget other)
        {
            Array.Copy(other.Weights, Weights, Weights.Length);
            Array.Copy(other.Parts, Parts, Parts.Length);
            Array.Copy(other.Signals, Signals, Signals.Length);
            LookYaw = other.LookYaw;
            LookPitch = other.LookPitch;
            Head = other.Head;
        }

        /// <summary>
        /// Writes into <paramref name="into"/> the target <paramref name="t"/> of the way from
        /// <paramref name="from"/> to <paramref name="to"/>: weights and gaze linearly, the head along
        /// the shortest arc. <paramref name="into"/> may be either input. Pure apart from writing the
        /// caller's buffer, which is reused every frame (CLAUDE.md 15.1, 23).
        /// </summary>
        public static void Lerp(FaceTarget from, FaceTarget to, float t, FaceTarget into)
        {
            t = Mathf.Clamp01(t);
            for (int i = 0; i < into.Weights.Length; i++)
            {
                into.Weights[i] = Mathf.Lerp(from.Weights[i], to.Weights[i], t);
            }

            for (int i = 0; i < into.Parts.Length; i++)
            {
                into.Parts[i] = Mathf.Lerp(from.Parts[i], to.Parts[i], t);
            }

            for (int i = 0; i < into.Signals.Length; i++)
            {
                into.Signals[i] = Mathf.Lerp(from.Signals[i], to.Signals[i], t);
            }

            into.LookYaw = Mathf.Lerp(from.LookYaw, to.LookYaw, t);
            into.LookPitch = Mathf.Lerp(from.LookPitch, to.LookPitch, t);
            into.Head = Quaternion.Slerp(from.Head, to.Head, t);
        }

        /// <summary>The Expression Signal of a neutral face: no feeling, the mouth closed, both eyes open.</summary>
        private static float[] NeutralSignals()
        {
            var signals = new float[SignalCount];
            signals[(int)ExpressionSignal.EyeOpenLeft] = 1f;
            signals[(int)ExpressionSignal.EyeOpenRight] = 1f;
            return signals;
        }
    }
}
