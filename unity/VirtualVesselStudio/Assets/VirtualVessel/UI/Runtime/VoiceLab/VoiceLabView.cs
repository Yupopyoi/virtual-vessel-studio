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

    /// <summary>
    /// A mood item: the key the scripts know and its levels. The scripts' instruction changes only at
    /// thresholds (services/voice-lab/voicelab/prompt.py), so the screen offers the levels, each sent as a
    /// value inside its band, and names the one chosen. Moving the knob always changes the instruction.
    /// </summary>
    public sealed class MoodSlider
    {
        public MoodSlider(string key, int start, params float[] levels)
        {
            Key = key;
            Levels = levels;
            Start = start;
        }

        /// <summary>The item's key in the request's "mood" object (Voice Lab detailed design 8.3.1).</summary>
        public string Key { get; }

        /// <summary>The value sent for each level, low to high, each inside one band of the instruction.</summary>
        public IReadOnlyList<float> Levels { get; }

        /// <summary>The level that holds the value VCBM's instructions were tuned with.</summary>
        public int Start { get; }

        public string NameKey => "lab.mood." + Key;

        /// <summary>The name of each level: "lab.mood.&lt;key&gt;.&lt;level&gt;", counted from 0.</summary>
        public string[] LevelKeys()
        {
            var keys = new string[Levels.Count];
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = NameKey + "." + i.ToString(CultureInfo.InvariantCulture);
            }

            return keys;
        }

        /// <summary>The value sent for <paramref name="level"/>, clamped to the levels. Pure.</summary>
        public float ValueAt(int level)
        {
            return Levels[Math.Max(0, Math.Min(Levels.Count - 1, level))];
        }
    }

    /// <summary>The Voice Lab screen's decisions, apart from the elements so they can be tested. All pure.</summary>
    public static class VoiceLabView
    {
        /// <summary>
        /// The timbre's age step that still sounded like the same person (+4, ×1.10; Voice Lab detailed design
        /// 8.3.2). Beyond it the screen warns.
        /// </summary>
        public const float SamePersonAge = 4f;

        /// <summary>How many candidates a round can make; six by default, fewer to try things quickly.</summary>
        public static readonly IReadOnlyList<int> CandidateCounts = new[] { 1, 3, 6, 12 };

        public const int DefaultCountIndex = 2;

        public static int CandidatesPerRound => CandidateCounts[DefaultCountIndex];

        /// <summary>The main four, shown first (system design 14.14). The bands are prompt.py's.</summary>
        public static readonly IReadOnlyList<MoodSlider> MainMood = new[]
        {
            new MoodSlider("youthful", 1, 0.25f, 0.55f, 0.85f, 0.95f),      // <0.40, to 0.80, to 0.90, from 0.90
            new MoodSlider("pitch", 1, 0.20f, 0.50f, 0.70f, 0.85f, 0.95f), // to 0.28, to 0.64, to 0.82, to 0.92, from 0.92
            new MoodSlider("brightness", 1, 0.20f, 0.50f, 0.80f),          // to 0.32, to 0.68, from 0.68
            new MoodSlider("energy", 1, 0.20f, 0.50f, 0.85f),              // to 0.30, to 0.72, from 0.72
        };

        /// <summary>The rest, under More detail.</summary>
        public static readonly IReadOnlyList<MoodSlider> MoreMood = new[]
        {
            new MoodSlider("softness", 2, 0.20f, 0.50f, 0.80f),            // to 0.32, to 0.68, from 0.68
            new MoodSlider("breathiness", 0, 0.20f, 0.50f, 0.85f),         // to 0.30, to 0.72, from 0.72
            new MoodSlider("cuteness", 1, 0.30f, 0.60f, 0.90f),            // <0.45, to 0.78, from 0.78
            new MoodSlider("expressiveness", 1, 0.20f, 0.50f, 0.85f),      // to 0.30, to 0.72, from 0.72
            new MoodSlider("speed", 1, 0.20f, 0.50f, 0.85f),               // to 0.30, to 0.72, from 0.72
            new MoodSlider("naturalness", 2, 0.30f, 0.70f, 0.90f),         // <0.52, to 0.82, from 0.82
        };

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

        /// <summary>Pitch, age, and brightness each moved this far together sounded mechanical (decision record, 2026-11-06).</summary>
        public const float MechanicalStep = 4f;

        /// <summary>Whether the timbre moved far enough that it may sound like someone else (system design 14.15).</summary>
        public static bool IsFar(VoiceLabTimbre timbre)
        {
            return Math.Abs(timbre.Age) > SamePersonAge;
        }

        /// <summary>Whether pitch, age, and brightness all moved far, which sounded mechanical.</summary>
        public static bool IsMechanical(VoiceLabTimbre timbre)
        {
            return Math.Abs(timbre.Pitch) >= MechanicalStep && Math.Abs(timbre.Age) >= MechanicalStep && Math.Abs(timbre.Brightness) >= MechanicalStep;
        }

        public static bool IsUnchanged(VoiceLabTimbre timbre)
        {
            return timbre.Pitch == 0f && timbre.Age == 0f && timbre.Brightness == 0f && timbre.Breathiness == 0f && timbre.Softness == 0f;
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
            return string.Join(" · ", Part(VoiceLabStrings.TimbrePitch, timbre.Pitch), Part(VoiceLabStrings.TimbreAge, timbre.Age), Part(VoiceLabStrings.TimbreBrightness, timbre.Brightness), Part(VoiceLabStrings.TimbreSoftness, timbre.Softness), Part(VoiceLabStrings.TimbreBreathiness, timbre.Breathiness));
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

        /// <summary>The candidates' bar: its text, how full it is, and "2 / 6".</summary>
        public static (string LabelKey, float Fraction, string Detail) MakingBar(VoiceLabMaking making)
        {
            if (making.Loading)
            {
                return (VoiceLabStrings.Loading, 0f, string.Empty);
            }

            float fraction = Math.Max(0f, Math.Min(1f, making.Done / (float)making.Total));
            return (VoiceLabStrings.Making, fraction, string.Format(CultureInfo.InvariantCulture, "{0} / {1}", making.Done, making.Total));
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
