using NUnit.Framework;
using UnityEngine;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.Tests
{
    /// <summary>Where a popup menu opens (components.md 3.23); the menu on a panel is in the PlayMode tests.</summary>
    public sealed class PopupMenuTests
    {
        [Test]
        public void Place_FlipsAndStaysInside()
        {
            var bounds = new Rect(0, 0, 800, 600);
            var size = new Vector2(200, 300);

            Assert.That(PopupMenu.Place(new Vector2(100, 100), size, bounds, 8), Is.EqualTo(new Vector2(100, 100)));
            Assert.That(PopupMenu.Place(new Vector2(700, 100), size, bounds, 8), Is.EqualTo(new Vector2(500, 100)), "Opens to the left near the right edge.");
            Assert.That(PopupMenu.Place(new Vector2(100, 500), size, bounds, 8), Is.EqualTo(new Vector2(100, 200)), "Opens upward near the bottom.");
            Assert.That(PopupMenu.Place(new Vector2(100, 200), new Vector2(200, 700), bounds, 8).y, Is.EqualTo(8), "Taller than the screen: from the top margin.");
        }
    }
}
