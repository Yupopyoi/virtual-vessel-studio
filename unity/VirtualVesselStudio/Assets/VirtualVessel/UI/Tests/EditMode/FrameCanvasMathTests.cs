using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Core.Geometry;
using VirtualVessel.UI.Frames;

namespace VirtualVessel.UI.Tests
{
    /// <summary>Hit testing and display decisions of the placement editor; input on a panel is in the PlayMode tests.</summary>
    public sealed class FrameCanvasMathTests
    {
        // A frame at 25%–75% × 25%–75% of a 400 × 200 px stream area: corners at (100, 50) and (300, 150).
        private static readonly NormalizedRect s_frame = new NormalizedRect(0.25, 0.25, 0.5, 0.5);

        [Test]
        public void HandleAt_FindsCornersAndEdges_WithinTheRadius()
        {
            Assert.That(FrameCanvasMath.HandleAt(s_frame, 100, 50, 400, 200, 12), Is.EqualTo(FrameHandle.TopLeft));
            Assert.That(FrameCanvasMath.HandleAt(s_frame, 310, 160, 400, 200, 12), Is.EqualTo(FrameHandle.BottomRight));
            Assert.That(FrameCanvasMath.HandleAt(s_frame, 200, 52, 400, 200, 12), Is.EqualTo(FrameHandle.Top));
            Assert.That(FrameCanvasMath.HandleAt(s_frame, 300, 100, 400, 200, 12), Is.EqualTo(FrameHandle.Right));
            Assert.That(FrameCanvasMath.HandleAt(s_frame, 200, 100, 400, 200, 12), Is.Null, "The middle moves the frame.");
            Assert.That(FrameCanvasMath.HandleAt(s_frame, 113, 50, 400, 200, 12), Is.Null, "Just outside the radius.");
        }

        [Test]
        public void HandleAt_PrefersCorners_OnSmallFrames()
        {
            // 20 px wide: the top edge handle is 10 px from both top corners.
            var small = new NormalizedRect(0.5, 0.5, 0.05, 0.1);

            Assert.That(FrameCanvasMath.HandleAt(small, 202, 100, 400, 200, 12), Is.EqualTo(FrameHandle.TopLeft));
        }

        [Test]
        public void FrameAt_ReturnsTheFrontmost()
        {
            var layouts = new[]
            {
                new FrameLayout(new NormalizedRect(0, 0, 0.6, 0.6), NormalizedRect.Full, order: 2),
                new FrameLayout(new NormalizedRect(0.4, 0.4, 0.6, 0.6), NormalizedRect.Full, order: 1),
            };

            Assert.That(FrameCanvasMath.FrameAt(layouts, 0.5, 0.5), Is.EqualTo(0), "Higher order is in front.");
            Assert.That(FrameCanvasMath.FrameAt(layouts, 0.9, 0.9), Is.EqualTo(1));
            Assert.That(FrameCanvasMath.FrameAt(layouts, 0.9, 0.1), Is.EqualTo(-1));
        }

        [Test]
        public void FrontToBack_SortsByOrder_LaterFirstOnTies()
        {
            var layouts = new[]
            {
                new FrameLayout(s_frame, NormalizedRect.Full, order: 0),
                new FrameLayout(s_frame, NormalizedRect.Full, order: 5),
                new FrameLayout(s_frame, NormalizedRect.Full, order: 0),
            };

            Assert.That(FrameCanvasMath.FrontToBack(layouts), Is.EqualTo(new[] { 1, 2, 0 }));
        }

        [Test]
        public void CursorFor_MatchesWhatIsUnderThePointer()
        {
            Assert.That(FrameCanvasMath.CursorFor(FrameHandle.TopLeft, true, false), Is.EqualTo(CursorKind.ResizeDiagonalDown));
            Assert.That(FrameCanvasMath.CursorFor(FrameHandle.BottomLeft, true, false), Is.EqualTo(CursorKind.ResizeDiagonalUp));
            Assert.That(FrameCanvasMath.CursorFor(FrameHandle.Left, true, false), Is.EqualTo(CursorKind.ResizeHorizontal));
            Assert.That(FrameCanvasMath.CursorFor(FrameHandle.Bottom, true, false), Is.EqualTo(CursorKind.ResizeVertical));
            Assert.That(FrameCanvasMath.CursorFor(null, true, false), Is.EqualTo(CursorKind.Move));
            Assert.That(FrameCanvasMath.CursorFor(null, true, true), Is.EqualTo(CursorKind.NotAllowed));
            Assert.That(FrameCanvasMath.CursorFor(null, false, false), Is.EqualTo(CursorKind.Default));
        }

        [Test]
        public void Readout_IsInOutputPixels()
        {
            var placement = new NormalizedRect(1.0 / 6, 1.0 / 6, 2.0 / 3, 2.0 / 3);

            Assert.That(FrameCanvasMath.Readout(placement, new CanvasSize(1920, 1080)), Is.EqualTo("1280 × 720 · 320, 180"));
            Assert.That(FrameCanvasMath.ReadoutBelow(new NormalizedRect(0.1, 0.02, 0.3, 0.3)), Is.True, "No room above.");
            Assert.That(FrameCanvasMath.ReadoutBelow(placement), Is.False);
        }

        [TestCase(-1, 3, false, 0)]
        [TestCase(0, 3, false, 1)]
        [TestCase(2, 3, false, -1)]
        [TestCase(-1, 3, true, 2)]
        [TestCase(0, 3, true, -1)]
        [TestCase(-1, 0, false, -1)]
        public void NextSelection_CyclesThenLetsFocusLeave(int current, int count, bool backwards, int expected)
        {
            Assert.That(FrameCanvasMath.NextSelection(current, count, backwards), Is.EqualTo(expected));
        }

        [Test]
        public void NudgeFor_MovesOnePixel_TenWithShift()
        {
            Assert.That(FrameCanvas.NudgeFor(KeyCode.LeftArrow, false), Is.EqualTo((-1, 0)));
            Assert.That(FrameCanvas.NudgeFor(KeyCode.DownArrow, true), Is.EqualTo((0, 10)));
            Assert.That(FrameCanvas.NudgeFor(KeyCode.A, false), Is.EqualTo((0, 0)));
        }

        [Test]
        public void CursorShapes_AreWhiteWithADarkOutline()
        {
            Color32[] move = CursorShapes.Draw(CursorKind.Move);
            int center = (CursorShapes.Size / 2 * CursorShapes.Size) + (CursorShapes.Size / 2);

            Assert.That(move.Length, Is.EqualTo(CursorShapes.Size * CursorShapes.Size));
            Assert.That(move[center].a, Is.EqualTo(255));
            Assert.That(move[center].r, Is.GreaterThan(200), "The shape is white.");
            Assert.That(move[0].a, Is.EqualTo(0), "Corners are transparent.");
            Assert.That(CursorShapes.Inside(CursorKind.ResizeHorizontal, new Vector2(10, 0)), Is.True);
            Assert.That(CursorShapes.Inside(CursorKind.ResizeHorizontal, new Vector2(0, 10)), Is.False);
            Assert.That(CursorShapes.Inside(CursorKind.ResizeVertical, new Vector2(0, 10)), Is.True);
        }
    }
}
