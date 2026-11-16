using NUnit.Framework;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The step indicator and the candidate tile (components.md 3.26, 3.27).</summary>
    public sealed class StepAndCandidateTests
    {
        [TestCase(0, 1, StepStatus.Done)]
        [TestCase(1, 1, StepStatus.Current)]
        [TestCase(2, 1, StepStatus.Ahead)]
        public void StepStatus_FollowsTheCurrentStep(int step, int current, StepStatus expected)
        {
            Assert.That(StepIndicator.StatusOf(step, current), Is.EqualTo(expected));
        }

        [Test]
        public void StepNumbers_CountFromOne_WithoutAStringTable()
        {
            Assert.That(StepIndicator.NumberText(null, 0), Is.EqualTo("STEP 1"));
        }

        [Test]
        public void OnlyStepsReached_CanBeChosen()
        {
            var steps = new StepIndicator();
            steps.SetSteps("a", "b", "c");
            steps.Show(0, 1);
            Button[] buttons = steps.Query<Button>().ToList().ToArray();

            Assert.That(buttons.Length, Is.EqualTo(3));
            Assert.That(buttons[0].ClassListContains(StepIndicator.CurrentClass), Is.True);
            Assert.That(buttons[1].enabledSelf, Is.True, "Reached before, so the user may go back to it.");
            Assert.That(buttons[2].enabledSelf, Is.False);

            steps.Show(2, 1);
            Assert.That(steps.Reached, Is.EqualTo(2), "The current step counts as reached.");
            Assert.That(buttons[0].ClassListContains(StepIndicator.DoneClass), Is.True);
        }

        [TestCase(150f, 0f)]
        [TestCase(425f, 50f)]
        [TestCase(700f, 100f)]
        [TestCase(80f, 0f)]
        [TestCase(900f, 100f)]
        public void Pitch_SitsOnTheSharedScale(float hz, float percent)
        {
            Assert.That(CandidateTile.ScalePercent(hz), Is.EqualTo(percent).Within(0.001f));
        }

        [Test]
        public void PitchNumbers_ShowADash_WhenThereIsNoPitch()
        {
            Assert.That(CandidateTile.HzText(268), Is.EqualTo("268 Hz"));
            Assert.That(CandidateTile.HzText(0), Is.EqualTo("—"));
            Assert.That(CandidateTile.RangeText(203, 338), Is.EqualTo("203–338 Hz"));
            Assert.That(CandidateTile.RangeText(0, 338), Is.EqualTo("—"));
        }

        [Test]
        public void Tile_ShowsWhetherItIsPicked()
        {
            var tile = new CandidateTile(1, 268, 203, 338);
            Assert.That(tile.ClassListContains(CandidateTile.PickedClass), Is.False);

            tile.Picked = true;
            Assert.That(tile.ClassListContains(CandidateTile.PickedClass), Is.True);
        }
    }
}
