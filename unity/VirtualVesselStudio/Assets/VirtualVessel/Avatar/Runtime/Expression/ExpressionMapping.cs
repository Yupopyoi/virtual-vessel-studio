using System;
using UnityEngine;
using VirtualVessel.Tracking;

namespace VirtualVessel.Avatar
{
    /// <summary>
    /// From the tracked face values (<see cref="FaceShape"/>, 0 to 1) to the avatar's expressions and
    /// gaze (avatar detailed design 7.4). The defaults hold what the Voom prototype learned: both eyes
    /// blink together unless one clearly winks, the mouth shapes never add up past fully open, and the
    /// feelings stay weak so they do not take over the face.
    /// </summary>
    /// <remarks>
    /// Settings, set when built and not changed while in use. The values are provisional until tuned
    /// with a camera (avatar detailed design 17). MediaPipe's face values rarely reach 1, so the curves
    /// end below it.
    /// </remarks>
    public sealed class ExpressionMapping
    {
        public static readonly ExpressionMapping Default = new ExpressionMapping();

        /// <summary>Eye closing: below the lower end the eye is open, above the upper end closed.</summary>
        public ExpressionCurve Blink { get; set; } = new ExpressionCurve(0.3f, 0.7f);

        /// <summary>Close both eyes alike unless one eye is closed this much more than the other (a wink).</summary>
        public bool SyncBlink { get; set; } = true;

        public float WinkDifference { get; set; } = 0.5f;

        public ExpressionCurve JawOpen { get; set; } = new ExpressionCurve(0.05f, 0.55f);

        public ExpressionCurve Funnel { get; set; } = new ExpressionCurve(0.1f, 0.5f);

        public ExpressionCurve Pucker { get; set; } = new ExpressionCurve(0.2f, 0.8f);

        public ExpressionCurve Stretch { get; set; } = new ExpressionCurve(0.1f, 0.5f);

        public ExpressionCurve Smile { get; set; } = new ExpressionCurve(0.2f, 0.7f);

        /// <summary>How much a closed-mouth smile shows as the "i" mouth.</summary>
        public float IhFromSmile { get; set; } = 0.5f;

        public ExpressionCurve Happy { get; set; } = new ExpressionCurve(0.2f, 0.7f, 0.6f);

        public ExpressionCurve CheekSquint { get; set; } = new ExpressionCurve(0.2f, 0.6f, 0.6f);

        public ExpressionCurve BrowInnerUp { get; set; } = new ExpressionCurve(0.3f, 0.8f);

        public ExpressionCurve EyeWide { get; set; } = new ExpressionCurve(0.05f, 0.4f);

        public ExpressionCurve MouthFrown { get; set; } = new ExpressionCurve(0.05f, 0.4f);

        public ExpressionCurve BrowDown { get; set; } = new ExpressionCurve(0.2f, 0.7f);

        /// <summary>The weight of the feelings Surprised, Sad, and Angry at their strongest.</summary>
        public float FeelingGain { get; set; } = 0.6f;

        /// <summary>The look value (in minus out, up minus down) at which the eyes reach their range.</summary>
        public float GazeFullScale { get; set; } = 0.5f;

        /// <summary>How far the eyes turn at most, as a share of the model's eye range.</summary>
        public float MaxLook { get; set; } = 1f;

        /// <summary>
        /// Writes the expressions and gaze for <paramref name="face"/> into <paramref name="into"/>; the
        /// head is left as it is. With <paramref name="mirror"/>, the person's left drives the avatar's
        /// right (avatar detailed design 6.6). Pure apart from writing the caller's buffer, which is
        /// reused per observation (CLAUDE.md 15.1, 23).
        /// </summary>
        public void Evaluate(FacePose face, bool mirror, FaceTarget into)
        {
            if (face == null)
            {
                throw new ArgumentNullException(nameof(face));
            }

            // The avatar's own side: the person's same side, or the other side when mirrored.
            float Side(FaceShape personLeft, FaceShape personRight, bool avatarLeft)
            {
                return face[avatarLeft != mirror ? personLeft : personRight];
            }

            float Both(FaceShape left, FaceShape right)
            {
                return (face[left] + face[right]) * 0.5f;
            }

            Array.Clear(into.Weights, 0, into.Weights.Length);

            // Eyes.
            float blinkLeft = Blink.Evaluate(Side(FaceShape.EyeBlinkLeft, FaceShape.EyeBlinkRight, avatarLeft: true));
            float blinkRight = Blink.Evaluate(Side(FaceShape.EyeBlinkLeft, FaceShape.EyeBlinkRight, avatarLeft: false));
            (float left, float right, bool together) = Eyes(blinkLeft, blinkRight, SyncBlink, WinkDifference);
            if (together)
            {
                into[AvatarExpression.Blink] = left;
            }
            else
            {
                into[AvatarExpression.BlinkLeft] = left;
                into[AvatarExpression.BlinkRight] = right;
            }

            // Mouth: the five shapes share one opening.
            float jaw = JawOpen.Evaluate(face[FaceShape.JawOpen]);
            float funnel = Funnel.Evaluate(face[FaceShape.MouthFunnel]);
            float pucker = Pucker.Evaluate(face[FaceShape.MouthPucker]);
            float stretch = Stretch.Evaluate(Both(FaceShape.MouthStretchLeft, FaceShape.MouthStretchRight));
            float smile = Smile.Evaluate(Both(FaceShape.MouthSmileLeft, FaceShape.MouthSmileRight));
            float aa = jaw * (1f - funnel);
            float oh = jaw * funnel;
            float ou = Mathf.Max(pucker, funnel) * (1f - jaw);
            float ee = stretch;
            float ih = smile * IhFromSmile * (1f - jaw);
            float scale = MouthScale(aa + oh + ou + ee + ih);
            into[AvatarExpression.Aa] = aa * scale;
            into[AvatarExpression.Oh] = oh * scale;
            into[AvatarExpression.Ou] = ou * scale;
            into[AvatarExpression.Ee] = ee * scale;
            into[AvatarExpression.Ih] = ih * scale;

            // Feelings: each needs all of its signs, so one moving part alone does not show a feeling.
            float browUp = BrowInnerUp.Evaluate(face[FaceShape.BrowInnerUp]);
            into[AvatarExpression.Happy] = Mathf.Max(
                Happy.Evaluate(Both(FaceShape.MouthSmileLeft, FaceShape.MouthSmileRight)),
                CheekSquint.Evaluate(Both(FaceShape.CheekSquintLeft, FaceShape.CheekSquintRight)));
            into[AvatarExpression.Surprised] = FeelingGain * Mathf.Min(browUp, EyeWide.Evaluate(Both(FaceShape.EyeWideLeft, FaceShape.EyeWideRight)));
            into[AvatarExpression.Sad] = FeelingGain * Mathf.Min(browUp, MouthFrown.Evaluate(Both(FaceShape.MouthFrownLeft, FaceShape.MouthFrownRight)));
            into[AvatarExpression.Angry] = FeelingGain * BrowDown.Evaluate(Both(FaceShape.BrowDownLeft, FaceShape.BrowDownRight));

            // Gaze: both eyes together. Looking in is toward the nose, the person's right for the left eye.
            float yaw = ((face[FaceShape.EyeLookInLeft] - face[FaceShape.EyeLookOutLeft]) + (face[FaceShape.EyeLookOutRight] - face[FaceShape.EyeLookInRight])) * 0.5f;
            float pitch = ((face[FaceShape.EyeLookUpLeft] - face[FaceShape.EyeLookDownLeft]) + (face[FaceShape.EyeLookUpRight] - face[FaceShape.EyeLookDownRight])) * 0.5f;
            into.LookYaw = (mirror ? -1f : 1f) * Mathf.Clamp(yaw / GazeFullScale, -1f, 1f) * MaxLook;

            // A closing lid reads as looking down, so the eyes look up and down only as far as they are open.
            into.LookPitch = Mathf.Clamp(pitch / GazeFullScale, -1f, 1f) * MaxLook * (1f - Mathf.Max(blinkLeft, blinkRight));
        }

        /// <summary>
        /// The eyes' closing: alike (the mean of both) unless one is closed at least
        /// <paramref name="winkDifference"/> more, which keeps a wink. Pure.
        /// </summary>
        public static (float Left, float Right, bool Together) Eyes(float left, float right, bool sync, float winkDifference)
        {
            if (sync && Mathf.Abs(left - right) < winkDifference)
            {
                float both = (left + right) * 0.5f;
                return (both, both, true);
            }

            return (left, right, false);
        }

        /// <summary>The factor that keeps the mouth shapes at most fully open together: 1, or less above it. Pure.</summary>
        public static float MouthScale(float total)
        {
            return total > 1f ? 1f / total : 1f;
        }
    }
}
