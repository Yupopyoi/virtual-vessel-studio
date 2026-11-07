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

        /// <summary>
        /// The picked words read back as one sentence in the screen's language (Voice Lab detailed design 8.3.1):
        /// "lab.ask.sentence" with {who}, {voice}, and {how}, then the delivery and the user's own words. Each
        /// word gives its "phrase"; <paramref name="text"/> looks a key up. Pure.
        /// </summary>
        public static string AskSentence(VoiceLabShelf shelf, IReadOnlyList<string> picked, string freeWords, Func<string, string> text)
        {
            string Phrase(string slot)
            {
                foreach (string id in picked)
                {
                    VoiceLabShelfWord word = shelf.Word(id);
                    if (word != null && word.Slot == slot)
                    {
                        return text(word.PhraseKey);
                    }
                }

                return null;
            }

            string Join(string separatorKey, params string[] parts)
            {
                return string.Join(text(separatorKey), Array.FindAll(parts, p => !string.IsNullOrEmpty(p)));
            }

            string who = text("lab.ask.who").Replace("{person}", Phrase("person") ?? string.Empty).Replace("{voice}", Phrase("voice") ?? string.Empty).Trim();
            string sentence = text("lab.ask.sentence")
                .Replace("{who}", who)
                .Replace("{voice}", Join("lab.ask.list", Phrase("pitch"), Phrase("resonance"), Phrase("breath")))
                .Replace("{how}", Join("lab.ask.and", Phrase("character"), Phrase("energy")));
            string manner = Join("lab.ask.manner-join", Phrase("intonation"), Phrase("speed"));
            if (manner.Length > 0)
            {
                sentence += text("lab.ask.manner").Replace("{manner}", manner);
            }

            string own = freeWords?.Trim();
            return string.IsNullOrEmpty(own) ? sentence : sentence + " " + own;
        }

        /// <summary>
        /// The base's name: "Candidate n", or, for a base put back from a run, which has no number in this
        /// session, that it is the run's base. Pure.
        /// </summary>
        public static string BaseName(ILocalizer localizer, VoiceLabCandidate candidate, int number)
        {
            if (candidate == null)
            {
                return string.Empty;
            }

            return number > 0 ? Controls.CandidateTile.NameText(localizer, number) : Controls.KeyedText.Resolve(localizer, VoiceLabStrings.RemadeBase);
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
