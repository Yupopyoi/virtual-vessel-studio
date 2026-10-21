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
    /// Settings, set when built and not changed while in use; <see cref="For"/> builds one from the
    /// user's <see cref="FaceTuning"/>. MediaPipe's face values rarely reach 1, so the curves end well
    /// below it. The defaults come from camera checks: raised after the first (2026-10-20), where the
    /// eyes, brows, and mouth moved too little, then set to what the user tuned on later checks
    /// (2026-10-21): eyes 92% (70%, then 110%, then 120% of that), brows 144% (120%, then 120%),
    /// mouth 70%, and gaze 70% of the raised curves (avatar detailed design 7.4).
    /// </remarks>
    public sealed class ExpressionMapping
    {
        public static readonly ExpressionMapping Default = new ExpressionMapping();

        /// <summary>Eye closing: below the lower end the eye is open, above the upper end closed.</summary>
        public ExpressionCurve Blink { get; set; } = new ExpressionCurve(0.275f, 0.65f);

        /// <summary>Close both eyes alike unless one eye is closed this much more than the other (a wink).</summary>
        public bool SyncBlink { get; set; } = true;

        /// <summary>
        /// Closing one eye squints the other a little, so a wink shows as a difference rather than one
        /// eye alone; this is the difference, after <see cref="Blink"/>, that counts as one.
        /// </summary>
        public float WinkDifference { get; set; } = 0.3f;

        public ExpressionCurve JawOpen { get; set; } = new ExpressionCurve(0.03f, 0.57f);

        /// <summary>
        /// How far the lips are apart, from the jaw: the rounded and wide mouths (u, e, i) show only
        /// as far as the mouth is open. Pucker and smile values stay well above 0 with the lips at
        /// rest, which otherwise showed a small open "u" on a closed mouth (2026-10-21).
        /// </summary>
        public ExpressionCurve LipsApart { get; set; } = new ExpressionCurve(0.04f, 0.12f);

        public ExpressionCurve Funnel { get; set; } = new ExpressionCurve(0.07f, 0.57f);

        public ExpressionCurve Pucker { get; set; } = new ExpressionCurve(0.14f, 0.86f);

        public ExpressionCurve Stretch { get; set; } = new ExpressionCurve(0.07f, 0.5f);

        public ExpressionCurve Smile { get; set; } = new ExpressionCurve(0.21f, 0.86f);

        /// <summary>How much a closed-mouth smile shows as the "i" mouth.</summary>
        public float IhFromSmile { get; set; } = 0.5f;

        public ExpressionCurve Happy { get; set; } = new ExpressionCurve(0.15f, 0.6f, 0.8f);

        public ExpressionCurve CheekSquint { get; set; } = new ExpressionCurve(0.15f, 0.5f, 0.8f);

        public ExpressionCurve BrowInnerUp { get; set; } = new ExpressionCurve(0.104f, 0.35f);

        public ExpressionCurve EyeWide { get; set; } = new ExpressionCurve(0.014f, 0.175f);

        public ExpressionCurve MouthFrown { get; set; } = new ExpressionCurve(0.035f, 0.242f);

        public ExpressionCurve BrowDown { get; set; } = new ExpressionCurve(0.069f, 0.275f);

        /// <summary>The outer brows raised, for raised brows on a face moved part by part.</summary>
        public ExpressionCurve BrowOuterUp { get; set; } = new ExpressionCurve(0.104f, 0.35f);

        /// <summary>The mouth's corners raised, on a face moved part by part.</summary>
        public ExpressionCurve SmileMouth { get; set; } = new ExpressionCurve(0.15f, 0.6f);

        /// <summary>How far drawn-down brows narrow the eyes on a face moved part by part.</summary>
        public float EyeAngryFromBrows { get; set; } = 0.5f;

        /// <summary>The weight of the feelings Surprised, Sad, and Angry at their strongest.</summary>
        public float FeelingGain { get; set; } = 0.8f;

        /// <summary>The look value (in minus out, up minus down) at which the eyes reach their range.</summary>
        public float GazeFullScale { get; set; } = 0.5f;

        /// <summary>How far the eyes turn at most, as a share of the model's eye range.</summary>
        public float MaxLook { get; set; } = 1f;

        /// <summary>
        /// The mapping for the user's <paramref name="tuning"/>: each part's curves made that many times
        /// as sensitive, and a wink told apart by a difference that many times smaller. Pure.
        /// </summary>
        public static ExpressionMapping For(FaceTuning tuning)
        {
            ExpressionMapping d = Default;
            if (tuning == null)
            {
                return d;
            }

            float eyes = tuning.Get(FaceTuningPart.Eyes);
            float brows = tuning.Get(FaceTuningPart.Brows);
            float mouth = tuning.Get(FaceTuningPart.Mouth);
            float smile = tuning.Get(FaceTuningPart.Smile);
            return new ExpressionMapping
            {
                Blink = d.Blink.Sensitive(eyes),
                SyncBlink = d.SyncBlink,
                WinkDifference = d.WinkDifference / tuning.Get(FaceTuningPart.Wink),
                JawOpen = d.JawOpen.Sensitive(mouth),
                LipsApart = d.LipsApart.Sensitive(mouth),
                Funnel = d.Funnel.Sensitive(mouth),
                Pucker = d.Pucker.Sensitive(mouth),
                Stretch = d.Stretch.Sensitive(mouth),
                Smile = d.Smile.Sensitive(mouth),
                IhFromSmile = d.IhFromSmile,
                Happy = d.Happy.Sensitive(smile),
                CheekSquint = d.CheekSquint.Sensitive(smile),
                BrowInnerUp = d.BrowInnerUp.Sensitive(brows),
                EyeWide = d.EyeWide.Sensitive(brows),
                MouthFrown = d.MouthFrown.Sensitive(brows),
                BrowDown = d.BrowDown.Sensitive(brows),
                BrowOuterUp = d.BrowOuterUp.Sensitive(brows),
                SmileMouth = d.SmileMouth.Sensitive(smile),
                EyeAngryFromBrows = d.EyeAngryFromBrows,
                FeelingGain = d.FeelingGain,
                GazeFullScale = d.GazeFullScale / tuning.Get(FaceTuningPart.Gaze),
                MaxLook = d.MaxLook,
            };
        }

        /// <summary>
        /// Writes the expressions, the face parts, and the gaze for <paramref name="face"/> into
        /// <paramref name="into"/>; the head is left as it is. Both the whole-face expressions and the
        /// parts are written; the model in use takes one of them (<see cref="ExpressionController"/>). With <paramref name="mirror"/>, the person's left drives the avatar's
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
            Array.Clear(into.Parts, 0, into.Parts.Length);

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

            // Mouth: the five shapes share one opening, and a closed mouth shows none of them.
            float jaw = JawOpen.Evaluate(face[FaceShape.JawOpen]);
            float apart = LipsApart.Evaluate(face[FaceShape.JawOpen]);
            float funnel = Funnel.Evaluate(face[FaceShape.MouthFunnel]);
            float pucker = Pucker.Evaluate(face[FaceShape.MouthPucker]);
            float stretch = Stretch.Evaluate(Both(FaceShape.MouthStretchLeft, FaceShape.MouthStretchRight));
            float smile = Smile.Evaluate(Both(FaceShape.MouthSmileLeft, FaceShape.MouthSmileRight));
            float aa = jaw * (1f - funnel);
            float oh = jaw * funnel;
            float ou = Mathf.Max(pucker, funnel) * (1f - jaw) * apart;
            float ee = stretch * apart;
            float ih = smile * IhFromSmile * (1f - jaw) * apart;
            float scale = MouthScale(aa + oh + ou + ee + ih);
            into[AvatarExpression.Aa] = aa * scale;
            into[AvatarExpression.Oh] = oh * scale;
            into[AvatarExpression.Ou] = ou * scale;
            into[AvatarExpression.Ee] = ee * scale;
            into[AvatarExpression.Ih] = ih * scale;

            // Feelings. VRM has no brow expressions, so the brows show through these: raised brows alone
            // give half a surprise and wide eyes the rest, while sadness also needs the frown.
            float browUp = BrowInnerUp.Evaluate(face[FaceShape.BrowInnerUp]);
            into[AvatarExpression.Happy] = Mathf.Max(
                Happy.Evaluate(SmileBeyondStretch(Both(FaceShape.MouthSmileLeft, FaceShape.MouthSmileRight), Both(FaceShape.MouthStretchLeft, FaceShape.MouthStretchRight))),
                CheekSquint.Evaluate(Both(FaceShape.CheekSquintLeft, FaceShape.CheekSquintRight)));
            into[AvatarExpression.Surprised] = FeelingGain * browUp * (0.5f + (0.5f * EyeWide.Evaluate(Both(FaceShape.EyeWideLeft, FaceShape.EyeWideRight))));
            into[AvatarExpression.Sad] = FeelingGain * Mathf.Min(browUp, MouthFrown.Evaluate(Both(FaceShape.MouthFrownLeft, FaceShape.MouthFrownRight)));
            into[AvatarExpression.Angry] = FeelingGain * BrowDown.Evaluate(Both(FaceShape.BrowDownLeft, FaceShape.BrowDownRight));

            WriteParts(face, left, right, jaw, apart, funnel, pucker, stretch, browUp, into);

            // Gaze: both eyes together. Looking in is toward the nose, the person's right for the left eye.
            float yaw = ((face[FaceShape.EyeLookInLeft] - face[FaceShape.EyeLookOutLeft]) + (face[FaceShape.EyeLookOutRight] - face[FaceShape.EyeLookInRight])) * 0.5f;
            float pitch = ((face[FaceShape.EyeLookUpLeft] - face[FaceShape.EyeLookDownLeft]) + (face[FaceShape.EyeLookUpRight] - face[FaceShape.EyeLookDownRight])) * 0.5f;
            into.LookYaw = (mirror ? -1f : 1f) * Mathf.Clamp(yaw / GazeFullScale, -1f, 1f) * MaxLook;

            // A closing lid reads as looking down, so the eyes look up and down only as far as they are open.
            into.LookPitch = Mathf.Clamp(pitch / GazeFullScale, -1f, 1f) * MaxLook * (1f - Mathf.Max(blinkLeft, blinkRight));
        }

        /// <summary>
        /// The parts, each on its own (avatar detailed design 7.4), as the Voom prototype moved VRoid's
        /// face: the eyes open and close on their own, the mouth smiles closed or open, the brows rise
        /// or fall alone. <paramref name="closeLeft"/> and <paramref name="closeRight"/> are the
        /// avatar's own eyes, already mirrored and kept together or apart.
        /// </summary>
        private void WriteParts(
            FacePose face,
            float closeLeft,
            float closeRight,
            float jaw,
            float apart,
            float funnel,
            float pucker,
            float stretch,
            float browUp,
            FaceTarget into)
        {
            // The eyes follow only their own closing; a smile does not close them.
            float rawSmile = Mean(face, FaceShape.MouthSmileLeft, FaceShape.MouthSmileRight);
            into[FacePart.EyeCloseLeft] = closeLeft;
            into[FacePart.EyeCloseRight] = closeRight;
            into[FacePart.EyeWide] = EyeWide.Evaluate(Mean(face, FaceShape.EyeWideLeft, FaceShape.EyeWideRight));
            float browDown = BrowDown.Evaluate(Mean(face, FaceShape.BrowDownLeft, FaceShape.BrowDownRight));
            into[FacePart.EyeAngry] = browDown * EyeAngryFromBrows;

            // Brows: raised inner brows with a frown are sad; raised brows otherwise are raised.
            float frown = MouthFrown.Evaluate(Mean(face, FaceShape.MouthFrownLeft, FaceShape.MouthFrownRight));
            float sad = Mathf.Min(browUp, frown);
            float raised = Mathf.Max(browUp, BrowOuterUp.Evaluate(Mean(face, FaceShape.BrowOuterUpLeft, FaceShape.BrowOuterUpRight)));
            into[FacePart.BrowSad] = sad;
            into[FacePart.BrowUp] = Mathf.Max(0f, raised - sad);
            into[FacePart.BrowDown] = browDown;

            // Mouth: raised corners are a smile, closed or open with the jaw; the vowels share the rest
            // of the opening. "i" is lips pulled sideways a little apart, "e" the same more open.
            float corners = SmileMouth.Evaluate(rawSmile);
            float a = jaw * (1f - funnel) * (1f - corners);
            float o = jaw * funnel;
            float u = Mathf.Max(pucker, funnel) * (1f - jaw) * apart;
            float e = stretch * jaw;
            float i = stretch * apart * (1f - jaw);
            float openSmile = corners * jaw;
            float scale = MouthScale(a + o + u + e + i + openSmile);
            into[FacePart.MouthA] = a * scale;
            into[FacePart.MouthO] = o * scale;
            into[FacePart.MouthU] = u * scale;
            into[FacePart.MouthE] = e * scale;
            into[FacePart.MouthI] = i * scale;
            into[FacePart.MouthOpenSmile] = openSmile * scale;
            into[FacePart.MouthSmile] = corners * (1f - jaw);
            into[FacePart.MouthFrown] = frown;
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

        /// <summary>
        /// How much of a smile is a smile and not the "i" mouth: the corners raised beyond the lips
        /// pulled sideways. Saying "i" pulls the lips sideways and raises the corners a little, which
        /// showed a smile, and many models close the eyes in their smile (2026-10-21). Raised cheeks
        /// still show a smile on their own. Pure.
        /// </summary>
        public static float SmileBeyondStretch(float smile, float stretch)
        {
            return Mathf.Max(0f, smile - stretch);
        }

        /// <summary>The mean of a value's left and right. Pure.</summary>
        private static float Mean(FacePose face, FaceShape left, FaceShape right)
        {
            return (face[left] + face[right]) * 0.5f;
        }

        /// <summary>The factor that keeps the mouth shapes at most fully open together: 1, or less above it. Pure.</summary>
        public static float MouthScale(float total)
        {
            return total > 1f ? 1f / total : 1f;
        }
    }
}
