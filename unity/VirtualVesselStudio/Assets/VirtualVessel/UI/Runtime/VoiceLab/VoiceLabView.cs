using System;
using System.Collections.Generic;
using System.Globalization;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Streaming;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>How the candidates are ordered (system design 14.14).</summary>
    public enum CandidateOrder
    {
        AsMade,
        LowestFirst,
        HighestFirst,
    }

    /// <summary>A mood slider: the key the scripts know, its starting value, and its texts.</summary>
    public readonly struct MoodSlider
    {
        public MoodSlider(string key, float start)
        {
            Key = key;
            Start = start;
        }

        /// <summary>The slider's key in the request's "mood" object (Voice Lab detailed design 8.3.1).</summary>
        public string Key { get; }

        /// <summary>0 to 1; the values VCBM's instructions were tuned with.</summary>
        public float Start { get; }

        public string NameKey => "lab.mood." + Key;

        public string LowKey => NameKey + ".low";

        public string HighKey => NameKey + ".high";
    }

    /// <summary>The Voice Lab screen's decisions, apart from the elements so they can be tested. All pure.</summary>
    public static class VoiceLabView
    {
        /// <summary>
        /// The timbre's age step that still sounded like the same person (+4, ×1.10; Voice Lab detailed design
        /// 8.3.2). Beyond it the screen warns.
        /// </summary>
        public const float SamePersonAge = 4f;

        public const int CandidatesPerRound = 6;

        /// <summary>The main four, shown first (system design 14.14).</summary>
        public static readonly IReadOnlyList<MoodSlider> MainMood = new[]
        {
            new MoodSlider("youthful", 0.55f),
            new MoodSlider("pitch", 0.55f),
            new MoodSlider("brightness", 0.65f),
            new MoodSlider("energy", 0.40f),
        };

        /// <summary>The rest, under More detail.</summary>
        public static readonly IReadOnlyList<MoodSlider> MoreMood = new[]
        {
            new MoodSlider("softness", 0.70f),
            new MoodSlider("breathiness", 0.25f),
            new MoodSlider("cuteness", 0.60f),
            new MoodSlider("expressiveness", 0.55f),
            new MoodSlider("speed", 0.50f),
            new MoodSlider("naturalness", 0.90f),
        };

        /// <summary>
        /// A slider's percent as the scripts take it, 0.01 to 1. Never 0: the scripts leave a sentence out at
        /// exactly 0, which the left end of a slider does not mean.
        /// </summary>
        public static float MoodValue(float percent)
        {
            return Math.Max(0.01f, Math.Min(1f, percent / 100f));
        }

        /// <summary>The candidates' indexes in <paramref name="order"/>; equal pitches keep the order made.</summary>
        public static int[] Order(IReadOnlyList<int> medianHz, CandidateOrder order)
        {
            var indexes = new int[medianHz.Count];
            for (int i = 0; i < indexes.Length; i++)
            {
                indexes[i] = i;
            }

            if (order == CandidateOrder.AsMade)
            {
                return indexes;
            }

            int sign = order == CandidateOrder.LowestFirst ? 1 : -1;

            // A stable sort; Array.Sort is not.
            var sorted = new List<int>(indexes);
            sorted.Sort((a, b) =>
            {
                int byPitch = sign * medianHz[a].CompareTo(medianHz[b]);
                return byPitch != 0 ? byPitch : a.CompareTo(b);
            });
            return sorted.ToArray();
        }

        /// <summary>The pitch after moving it <paramref name="semitones"/>, in Hz; 0 when the base has none.</summary>
        public static int MovedHz(int baseHz, float semitones)
        {
            return baseHz > 0 ? (int)Math.Round(baseHz * Math.Pow(2.0, semitones / 12.0)) : 0;
        }

        /// <summary>"268 Hz → 338 Hz", or just "268 Hz" when unchanged, or a dash.</summary>
        public static string PitchChangeText(int baseHz, float semitones)
        {
            if (baseHz <= 0)
            {
                return "—";
            }

            int moved = MovedHz(baseHz, semitones);
            return moved == baseHz
                ? string.Format(CultureInfo.InvariantCulture, "{0} Hz", baseHz)
                : string.Format(CultureInfo.InvariantCulture, "{0} Hz → {1} Hz", baseHz, moved);
        }

        /// <summary>Whether the timbre moved far enough that it may sound like someone else (system design 14.15).</summary>
        public static bool IsFar(VoiceLabTimbre timbre)
        {
            return Math.Abs(timbre.Age) > SamePersonAge;
        }

        public static bool IsUnchanged(VoiceLabTimbre timbre)
        {
            return timbre.Pitch == 0f && timbre.Age == 0f && timbre.Brightness == 0f && timbre.Breathiness == 0f;
        }

        /// <summary>"+4", "0", "-2": a step as the summary shows it.</summary>
        public static string StepText(float step)
        {
            int rounded = (int)Math.Round(step);
            return rounded > 0 ? "+" + rounded.ToString(CultureInfo.InvariantCulture) : rounded.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>"Pitch +4 · Age +4 · Brightness 0 · Breathiness 0" in the language.</summary>
        public static string TimbreSummary(ILocalizer localizer, VoiceLabTimbre timbre)
        {
            string Part(string key, float step) => (localizer?.Get(key) ?? key) + " " + StepText(step);
            return string.Join(" · ", Part(VoiceLabStrings.TimbrePitch, timbre.Pitch), Part(VoiceLabStrings.TimbreAge, timbre.Age), Part(VoiceLabStrings.TimbreBrightness, timbre.Brightness), Part(VoiceLabStrings.TimbreBreathiness, timbre.Breathiness));
        }

        /// <summary>The text of a build's stage, or null when no build runs.</summary>
        public static string BuildStageKey(VoiceLabBuildState state)
        {
            switch (state)
            {
                case VoiceLabBuildState.Preparing:
                    return VoiceLabStrings.BuildPreparing;
                case VoiceLabBuildState.Preprocessing:
                case VoiceLabBuildState.Training:
                    return VoiceLabStrings.BuildLearning;
                case VoiceLabBuildState.PostProcessing:
                    return VoiceLabStrings.BuildFinishing;
                default:
                    return null;
            }
        }

        /// <summary>"45%", for the progress bar's detail.</summary>
        public static string PercentText(float fraction)
        {
            return ((int)Math.Round(100f * Math.Max(0f, Math.Min(1f, fraction)))).ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>What a finished build tells the user; null when it completed.</summary>
        public static string BuildResultKey(VoiceLabBuild build)
        {
            if (build == null)
            {
                return VoiceLabStrings.BuildFailed;
            }

            switch (build.State)
            {
                case VoiceLabBuildState.Completed:
                    return null;
                case VoiceLabBuildState.Cancelled:
                    return VoiceLabStrings.BuildCancelled;
                default:
                    return build.OutOfMemory ? VoiceLabStrings.BuildOutOfMemory : VoiceLabStrings.BuildFailed;
            }
        }

        /// <summary>Building a voice uses the GPU heavily, so it does not start while a stream runs (system design 14.26).</summary>
        public static bool IsStreaming(StreamingState state)
        {
            return state == StreamingState.Connecting || state == StreamingState.Live || state == StreamingState.Reconnecting;
        }

        /// <summary>The name as it will be registered, or null when there is none.</summary>
        public static string CleanName(string name)
        {
            string trimmed = name?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }
    }
}
