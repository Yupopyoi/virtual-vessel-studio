using NUnit.Framework;

namespace VirtualVessel.UI.Tests
{
    /// <summary>How large the UI is drawn in a window (UI foundation detailed design 7.9).</summary>
    public sealed class UiPanelScaleTests
    {
        [Test]
        public void ALargeWindow_FollowsTheDisplayScale_AndTheUiSize()
        {
            Assert.That(UiPanelScale.Drawn(1f, 216f, 3840, 2160), Is.EqualTo(2.25f).Within(1e-4f), "4K at 225%.");
            Assert.That(UiPanelScale.Drawn(1.5f, 144f, 3840, 2160), Is.EqualTo(2.25f).Within(1e-4f), "150% UI size on 150% Windows.");
            Assert.That(UiPanelScale.Drawn(1f, 96f, 1920, 1080), Is.EqualTo(1f), "Full HD at 100%.");
        }

        [Test]
        public void ASmallWindow_OnALargeDisplayScale_KeepsTheLeastLayout()
        {
            // The user's case: a Full HD window on a 4K monitor at 225% held only 853 x 480.
            float drawn = UiPanelScale.Drawn(1f, 216f, 1920, 1080);
            Assert.That(drawn, Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(1920 / drawn, Is.EqualTo(UiPanelScale.MinWidth).Within(0.5f), "1280 x 720 of layout.");

            Assert.That(UiPanelScale.Drawn(1f, 216f, 1920, 800), Is.EqualTo(800f / UiPanelScale.MinHeight).Within(1e-4f), "The height limits a wide, low window.");
        }

        [Test]
        public void TheDisplayScale_IsNotTakenBelow100Percent_AndTheUiSizeAlwaysApplies()
        {
            Assert.That(UiPanelScale.Drawn(1f, 216f, 1024, 768), Is.EqualTo(1f), "A window smaller than the least layout is drawn at 100%.");
            Assert.That(UiPanelScale.Drawn(1f, 96f, 1024, 768), Is.EqualTo(1f), "At 100% nothing changes.");
            Assert.That(UiPanelScale.Drawn(2f, 216f, 1920, 1080), Is.EqualTo(3f).Within(1e-4f), "The user's 200% on the fitted 150%.");
            Assert.That(UiPanelScale.Drawn(2f, 96f, 1920, 1080), Is.EqualTo(2f), "The user's choice is kept as it is.");
        }

        [Test]
        public void ThePanelScale_IsTheDrawnSize_OverTheDisplayScaleThePanelAlreadyApplies()
        {
            Assert.That(UiPanelScale.PanelScale(1f, 216f, 1920, 1080), Is.EqualTo(1.5f / 2.25f).Within(1e-4f));
            Assert.That(UiPanelScale.PanelScale(1.25f, 96f, 1600, 900), Is.EqualTo(1.25f), "At 96 DPI, the UI size itself.");
            Assert.That(UiPanelScale.PanelScale(1f, 0f, 1920, 1080), Is.EqualTo(1f), "An unknown DPI counts as 96.");
            Assert.That(UiPanelScale.PanelScale(1f, 216f, 0, 0), Is.EqualTo(1f), "No window size yet: the display scale as it is.");
        }
    }
}
