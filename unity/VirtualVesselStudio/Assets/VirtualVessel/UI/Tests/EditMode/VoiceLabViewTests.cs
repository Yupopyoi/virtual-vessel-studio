using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Streaming;
using VirtualVessel.UI.VoiceLab;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The Voice Lab screen's decisions (system design 14.13 to 14.18); the screen itself is in the PlayMode tests.</summary>
    public sealed class VoiceLabViewTests
    {
        [Test]
        public void MoodSliders_AreTheTenTheScriptsKnow_FourShownFirst()
        {
            Assert.That(VoiceLabView.MainMood.Select(m => m.Key), Is.EqualTo(new[] { "youthful", "pitch", "brightness", "energy" }));
            Assert.That(VoiceLabView.MainMood.Concat(VoiceLabView.MoreMood).Select(m => m.Key), Is.EquivalentTo(new[]
            {
                "youthful", "pitch", "brightness", "softness", "breathiness", "energy", "cuteness", "expressiveness", "speed", "naturalness",
            }));
        }

        [Test]
        public void MoodSliderTexts_AreInEveryTable()
        {
            // Built from the slider's key rather than declared, so the key test of the string tables does not see them.
            string folder = Path.Combine(Application.dataPath, "VirtualVessel", "UI", "Strings");
            foreach (string file in Directory.GetFiles(folder, "*.json"))
            {
                StringTable table = StringTable.Parse(File.ReadAllText(file, Encoding.UTF8));
                foreach (MoodSlider slider in VoiceLabView.MainMood.Concat(VoiceLabView.MoreMood))
                {
                    Assert.That(table.Strings.Keys, Does.Contain(slider.NameKey).And.Contain(slider.LowKey).And.Contain(slider.HighKey), file);
                }
            }
        }

        [TestCase(0f, 0.01f)]
        [TestCase(55f, 0.55f)]
        [TestCase(100f, 1f)]
        public void MoodValue_NeverReachesZero_WhichWouldDropTheSentence(float percent, float value)
        {
            Assert.That(VoiceLabView.MoodValue(percent), Is.EqualTo(value).Within(0.0001f));
        }

        [Test]
        public void Candidates_SortByPitch_KeepingTheOrderMadeForEqualPitches()
        {
            int[] hz = { 268, 406, 214, 268 };
            Assert.That(VoiceLabView.Order(hz, CandidateOrder.AsMade), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(VoiceLabView.Order(hz, CandidateOrder.LowestFirst), Is.EqualTo(new[] { 2, 0, 3, 1 }));
            Assert.That(VoiceLabView.Order(hz, CandidateOrder.HighestFirst), Is.EqualTo(new[] { 1, 0, 3, 2 }));
        }

        [Test]
        public void PitchChange_ShowsWhereTheKnobMovesIt()
        {
            Assert.That(VoiceLabView.MovedHz(268, 12f), Is.EqualTo(536));
            Assert.That(VoiceLabView.PitchChangeText(268, 4f), Is.EqualTo("268 Hz → 338 Hz"));
            Assert.That(VoiceLabView.PitchChangeText(268, 0f), Is.EqualTo("268 Hz"));
            Assert.That(VoiceLabView.PitchChangeText(0, 4f), Is.EqualTo("—"));
        }

        [TestCase(4f, false)]
        [TestCase(5f, true)]
        [TestCase(-5f, true)]
        public void FarMoves_AreAgeBeyondWhatStillSoundedTheSame(float age, bool far)
        {
            Assert.That(VoiceLabView.IsFar(new VoiceLabTimbre(6f, age, 6f, 6f)), Is.EqualTo(far));
        }

        [Test]
        public void TimbreSummary_NamesEachKnob()
        {
            Assert.That(VoiceLabView.TimbreSummary(null, new VoiceLabTimbre(4f, 4f, 0f, -2f)), Is.EqualTo("lab.timbre.pitch +4 · lab.timbre.age +4 · lab.timbre.brightness 0 · lab.timbre.breathiness -2"));
        }

        [TestCase(VoiceLabBuildState.Preparing, VoiceLabStrings.BuildPreparing)]
        [TestCase(VoiceLabBuildState.Preprocessing, VoiceLabStrings.BuildLearning)]
        [TestCase(VoiceLabBuildState.Training, VoiceLabStrings.BuildLearning)]
        [TestCase(VoiceLabBuildState.PostProcessing, VoiceLabStrings.BuildFinishing)]
        [TestCase(VoiceLabBuildState.None, null)]
        public void BuildStages_HaveTheirTexts(VoiceLabBuildState state, string key)
        {
            Assert.That(VoiceLabView.BuildStageKey(state), Is.EqualTo(key));
        }

        [Test]
        public void BuildResults_TellOutOfMemoryApart()
        {
            Assert.That(VoiceLabView.BuildResultKey(new VoiceLabBuild("r", VoiceLabBuildState.Completed, false)), Is.Null);
            Assert.That(VoiceLabView.BuildResultKey(new VoiceLabBuild("r", VoiceLabBuildState.Failed, true)), Is.EqualTo(VoiceLabStrings.BuildOutOfMemory));
            Assert.That(VoiceLabView.BuildResultKey(new VoiceLabBuild("r", VoiceLabBuildState.Failed, false)), Is.EqualTo(VoiceLabStrings.BuildFailed));
            Assert.That(VoiceLabView.BuildResultKey(new VoiceLabBuild("r", VoiceLabBuildState.Cancelled, false)), Is.EqualTo(VoiceLabStrings.BuildCancelled));
            Assert.That(VoiceLabView.BuildResultKey(null), Is.EqualTo(VoiceLabStrings.BuildFailed));
        }

        [TestCase(StreamingState.Idle, false)]
        [TestCase(StreamingState.Connecting, true)]
        [TestCase(StreamingState.Live, true)]
        [TestCase(StreamingState.Reconnecting, true)]
        [TestCase(StreamingState.Failed, false)]
        public void Builds_WaitWhileAStreamRuns(StreamingState state, bool streaming)
        {
            Assert.That(VoiceLabView.IsStreaming(state), Is.EqualTo(streaming));
        }

        [Test]
        public void Names_AreTrimmed_AndBlankIsNone()
        {
            Assert.That(VoiceLabView.CleanName("  ふわり "), Is.EqualTo("ふわり"));
            Assert.That(VoiceLabView.CleanName("   "), Is.Null);
            Assert.That(VoiceLabView.PercentText(0.456f), Is.EqualTo("46%"));
        }

        [Test]
        public void Playback_EndsWhenNothingPlaysOrItReachedTheEnd()
        {
            Assert.That(VoiceLabPlayer.HasEnded(null), Is.True);
            Assert.That(VoiceLabPlayer.HasEnded(1f), Is.True);
            Assert.That(VoiceLabPlayer.HasEnded(0.5f), Is.False);
        }
    }
}
