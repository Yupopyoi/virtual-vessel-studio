using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.UI.Icons;

namespace VirtualVessel.UI.Tests
{
    public sealed class IconGeometryTests
    {
        [Test]
        public void ParsePath_LinesAbsoluteAndRelative()
        {
            IReadOnlyList<IconPolyline> lines = IconGeometry.ParsePath("M4 7h10M18 7l2 1V12h-1v1");

            Assert.That(lines, Has.Count.EqualTo(2));
            Assert.That(lines[0].Points, Is.EqualTo(new[] { new Vector2(4, 7), new Vector2(14, 7) }));
            Assert.That(lines[1].Points, Is.EqualTo(new[] { new Vector2(18, 7), new Vector2(20, 8), new Vector2(20, 12), new Vector2(19, 12), new Vector2(19, 13) }));
            Assert.That(lines[1].Closed, Is.False);
        }

        [Test]
        public void ParsePath_CloseAndImplicitLines()
        {
            IReadOnlyList<IconPolyline> lines = IconGeometry.ParsePath("M12 3.5 21.5 20H2.5Z");

            Assert.That(lines, Has.Count.EqualTo(1));
            Assert.That(lines[0].Closed, Is.True);
            Assert.That(lines[0].Points[1], Is.EqualTo(new Vector2(21.5f, 20)), "Pairs after M are lines.");
        }

        [Test]
        public void ParsePath_ArcStaysOnTheCircle()
        {
            // Half circle of radius 3 around (12, 12), from the left to the right through the top.
            IconPolyline line = IconGeometry.ParsePath("M9 12a3 3 0 0 1 6 0").Single();

            Assert.That(line.Points.Last(), Is.EqualTo(new Vector2(15, 12)));
            foreach (Vector2 point in line.Points)
            {
                Assert.That(Vector2.Distance(point, new Vector2(12, 12)), Is.EqualTo(3f).Within(0.01f));
                Assert.That(point.y, Is.LessThanOrEqualTo(12.001f), "Sweep 1 goes through the top in screen coordinates.");
            }
        }

        [Test]
        public void ParsePath_CubicEndsAtItsEndPoint()
        {
            IconPolyline line = IconGeometry.ParsePath("M0 0C0 10 10 10 10 0").Single();

            Assert.That(line.Points.Last(), Is.EqualTo(new Vector2(10, 0)));
            Assert.That(line.Points.Max(p => p.y), Is.EqualTo(7.5f).Within(0.1f), "The curve peaks at 3/4 of the control height.");
        }

        [TestCase("4 7h10")]
        [TestCase("M4 7X3")]
        [TestCase("M4")]
        public void ParsePath_RejectsBadData(string path)
        {
            Assert.Throws<FormatException>(() => IconGeometry.ParsePath(path));
        }

        [Test]
        public void Library_EveryIconParsesInsideTheGrid()
        {
            foreach (IconName name in (IconName[])Enum.GetValues(typeof(IconName)))
            {
                if (name == IconName.None)
                {
                    continue;
                }

                IReadOnlyList<IconPart> parts = IconLibrary.Get(name);
                Assert.That(parts, Is.Not.Empty, $"{name} has no shape.");

                // Regression: dots written as near-zero strokes ("v0.01") are not drawn by Painter2D.
                // Dots must be small filled circles instead.
                foreach (IconPolyline line in parts.Where(part => !part.Filled).SelectMany(part => part.Lines))
                {
                    float length = 0f;
                    for (int i = 1; i < line.Points.Count; i++)
                    {
                        length += Vector2.Distance(line.Points[i - 1], line.Points[i]);
                    }

                    Assert.That(length, Is.GreaterThan(1f), $"{name} has a stroke too short to be drawn.");
                }

                foreach (Vector2 point in parts.SelectMany(part => part.Lines).SelectMany(line => line.Points))
                {
                    Assert.That(point.x, Is.InRange(1f, 23f), name.ToString());
                    Assert.That(point.y, Is.InRange(1f, 23f), name.ToString());
                }
            }
        }
    }
}
