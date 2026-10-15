using NUnit.Framework;
using UnityEngine;
using VirtualVessel.UI.Preview;

namespace VirtualVessel.UI.Tests
{
    /// <summary>Layout and text of the stream preview; behavior on a panel is in the PlayMode tests.</summary>
    public sealed class PreviewMathTests
    {
        [Test]
        public void Fit_KeepsTheAspectRatioInAnyRoom()
        {
            Vector2 wide = PreviewMath.Fit(1600, 600, 16f / 9f);
            Vector2 tall = PreviewMath.Fit(800, 900, 16f / 9f);

            Assert.That(wide.x, Is.EqualTo(1066.667f).Within(0.01f), "Wide room: full height.");
            Assert.That(wide.y, Is.EqualTo(600).Within(0.01f));
            Assert.That(tall.x, Is.EqualTo(800).Within(0.01f), "Tall room: full width.");
            Assert.That(tall.y, Is.EqualTo(450).Within(0.01f));
        }

        [Test]
        public void Texts_AreNumbersAndUnits()
        {
            Assert.That(PreviewMath.FrameRateText(59.94), Is.EqualTo("59.9 fps"));
            Assert.That(PreviewMath.FormatText(new PreviewStats(60, 60, 1920, 1080, 6000)), Is.EqualTo("1920×1080 · 6,000 kbps"));
            Assert.That(PreviewMath.FormatText(new PreviewStats(60, 60, 1920, 1080, null)), Is.EqualTo("1920×1080"), "Nothing encoded: no bitrate.");
        }

        [Test]
        public void FrameRateStatus_WarnsBelowTheTarget()
        {
            Assert.That(PreviewMath.FrameRateStatus(59.7, 60), Is.EqualTo(Controls.StatusKind.Success), "Half a frame of slack.");
            Assert.That(PreviewMath.FrameRateStatus(52, 60), Is.EqualTo(Controls.StatusKind.Warning));
            Assert.That(PreviewMath.FrameRateStatus(30, 30), Is.EqualTo(Controls.StatusKind.Success));
        }
    }
}
