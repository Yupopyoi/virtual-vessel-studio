using System;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.UI.Tooltips;

namespace VirtualVessel.UI.Tests
{
    /// <summary>Where tooltips go and when; behavior on a panel is in the PlayMode tests.</summary>
    public sealed class TooltipPlacementTests
    {
        private static readonly Rect s_screen = new Rect(0, 0, 800, 600);

        [Test]
        public void Place_IsCenteredAboveThePart()
        {
            Vector2 position = TooltipPlacement.Place(new Rect(300, 200, 100, 40), new Vector2(120, 30), s_screen, 6, 8);

            Assert.That(position, Is.EqualTo(new Vector2(290, 164)));
        }

        [Test]
        public void Place_GoesBelowWhenThereIsNoRoomAbove()
        {
            Vector2 position = TooltipPlacement.Place(new Rect(300, 10, 100, 40), new Vector2(120, 30), s_screen, 6, 8);

            Assert.That(position.y, Is.EqualTo(56), "Below the part, with the gap.");
        }

        [Test]
        public void Place_StaysInsideTheScreen()
        {
            Vector2 left = TooltipPlacement.Place(new Rect(0, 300, 20, 20), new Vector2(200, 30), s_screen, 6, 8);
            Vector2 right = TooltipPlacement.Place(new Rect(780, 300, 20, 20), new Vector2(200, 30), s_screen, 6, 8);
            Vector2 tooWide = TooltipPlacement.Place(new Rect(300, 300, 20, 20), new Vector2(900, 30), s_screen, 6, 8);

            Assert.That(left.x, Is.EqualTo(8));
            Assert.That(right.x, Is.EqualTo(800 - 8 - 200));
            Assert.That(tooWide.x, Is.EqualTo(8), "Wider than the screen: start at the left margin.");
        }

        [Test]
        public void Options_DefaultDelayIsHalfASecond()
        {
            Assert.That(new TooltipOptions().ShowDelay, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
        }
    }
}
