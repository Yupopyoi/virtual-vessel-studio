using System;
using NUnit.Framework;
using VirtualVessel.Core.Geometry;

namespace VirtualVessel.Core.Tests
{
    /// <summary>
    /// Placement editing math on a 1920 x 1080 stream screen. Expected values are worked out in
    /// pixels in the comments.
    /// </summary>
    public sealed class FrameEditMathTests
    {
        private const double Tolerance = 1e-9;

        private static readonly CanvasSize s_canvas = new CanvasSize(1920, 1080);

        // 960 x 540 px at (480, 270): a quarter-area 16:9 frame in the middle of the screen.
        private static readonly NormalizedRect s_middle = new NormalizedRect(0.25, 0.25, 0.5, 0.5);

        [Test]
        public void Move_StaysOnScreen_UnlessAllowed()
        {
            NormalizedRect inside = FrameEditMath.Move(s_middle, 0.5, -0.5, allowOutside: false);
            NormalizedRect outside = FrameEditMath.Move(s_middle, 0.5, -0.5, allowOutside: true);

            AssertRect(inside, 0.5, 0, 0.5, 0.5);
            AssertRect(outside, 0.75, -0.25, 0.5, 0.5);
        }

        [Test]
        public void Move_Outside_KeepsEnoughOnScreenToGrab()
        {
            // A 960 px frame can go off the left edge until 192 px (10% of the screen) remains.
            NormalizedRect left = FrameEditMath.Move(s_middle, -2, 0, allowOutside: true);
            NormalizedRect down = FrameEditMath.Move(s_middle, 0, 2, allowOutside: true);
            NormalizedRect nudged = FrameEditMath.Nudge(left, -50, 0, s_canvas, allowOutside: true);

            AssertRect(left, 0.1 - 0.5, 0.25, 0.5, 0.5);
            AssertRect(down, 0.25, 0.9, 0.5, 0.5);
            AssertRect(nudged, 0.1 - 0.5, 0.25, 0.5, 0.5);
        }

        [Test]
        public void Move_Outside_SmallFrame_KeepsHalfOnScreen()
        {
            // 96 px wide (5%): half of it, 48 px, is less than 10% of the screen.
            var small = new NormalizedRect(0.5, 0.5, 0.05, 0.05);

            NormalizedRect moved = FrameEditMath.Move(small, 2, 0, allowOutside: true);

            AssertRect(moved, 1 - 0.025, 0.5, 0.05, 0.05);
        }

        [Test]
        public void MoveWithSnap_Outside_StaysReachable()
        {
            SnapLines snap = FrameEditMath.CreateSnapLines(Array.Empty<NormalizedRect>(), 0.01, 0.01);

            FrameEditResult moved = FrameEditMath.MoveWithSnap(s_middle, 2, 0, allowOutside: true, snap);

            AssertRect(moved.Rect, 0.9, 0.25, 0.5, 0.5);
        }

        [Test]
        public void Move_FrameLargerThanScreen_KeepsCoveringIt()
        {
            var large = new NormalizedRect(-0.1, -0.1, 1.2, 1.2);

            NormalizedRect moved = FrameEditMath.Move(large, 0.5, 0.5, allowOutside: false);

            AssertRect(moved, 0, 0, 1.2, 1.2);
        }

        [Test]
        public void Nudge_MovesByOutputPixels()
        {
            NormalizedRect moved = FrameEditMath.Nudge(s_middle, 10, -1, s_canvas, allowOutside: false);

            AssertRect(moved, 0.25 + 10 / 1920.0, 0.25 - 1 / 1080.0, 0.5, 0.5);
        }

        [Test]
        public void MoveWithSnap_SnapsCenterToScreenCenter()
        {
            SnapLines snap = FrameEditMath.CreateSnapLines(null, 0.01, 0.01);

            FrameEditResult result = FrameEditMath.MoveWithSnap(s_middle, 0.006, 0, allowOutside: false, snap);

            AssertRect(result.Rect, 0.25, 0.25, 0.5, 0.5);
            Assert.That(result.GuideX, Is.EqualTo(0.5));
            Assert.That(result.GuideY, Is.EqualTo(0.5));
        }

        [Test]
        public void MoveWithSnap_BeyondThreshold_DoesNotSnap()
        {
            SnapLines snap = FrameEditMath.CreateSnapLines(null, 0.01, 0.01);

            FrameEditResult result = FrameEditMath.MoveWithSnap(s_middle, 0.02, 0.02, allowOutside: false, snap);

            AssertRect(result.Rect, 0.27, 0.27, 0.5, 0.5);
            Assert.That(result.GuideX, Is.Null);
            Assert.That(result.GuideY, Is.Null);
        }

        [Test]
        public void MoveWithSnap_SnapsToAnotherFramesEdge()
        {
            var other = new NormalizedRect(0.7, 0.05, 0.2, 0.2);
            var small = new NormalizedRect(0.1, 0.6, 0.25, 0.2);
            SnapLines snap = FrameEditMath.CreateSnapLines(new[] { other }, 0.01, 0.01);

            // The small frame's right edge ends 0.004 short of the other frame's left edge (0.7);
            // its left edge (0.446) and center (0.571) are far from any line.
            FrameEditResult result = FrameEditMath.MoveWithSnap(small, 0.346, 0, allowOutside: false, snap);

            Assert.That(result.Rect.Right, Is.EqualTo(0.7).Within(Tolerance));
            Assert.That(result.GuideX, Is.EqualTo(0.7));
        }

        [Test]
        public void Resize_CornerKeepsAspectAndFixesOppositeCorner()
        {
            // Pointer +192 px right: width 960 -> 1152 (x1.2); height follows to 648.
            FrameEditResult result = FrameEditMath.Resize(s_middle, FrameHandle.BottomRight, 0.1, 0, ResizeOptions.Default, s_canvas, allowOutside: false);

            AssertRect(result.Rect, 0.25, 0.25, 0.6, 0.6);
        }

        [Test]
        public void Resize_DiagonalDrag_UsesTheAxisThatChangedMore()
        {
            // Top-left handle: x changes by 20% of the width, y by 10% of the height; x wins (x1.2).
            FrameEditResult result = FrameEditMath.Resize(s_middle, FrameHandle.TopLeft, -0.1, -0.05, ResizeOptions.Default, s_canvas, allowOutside: false);

            AssertRect(result.Rect, 0.15, 0.15, 0.6, 0.6);
        }

        [Test]
        public void Resize_EdgeKeepsAspectAroundOppositeEdgeCenter()
        {
            FrameEditResult result = FrameEditMath.Resize(s_middle, FrameHandle.Right, 0.1, 0, ResizeOptions.Default, s_canvas, allowOutside: false);

            AssertRect(result.Rect, 0.25, 0.2, 0.6, 0.6);
        }

        [Test]
        public void Resize_WithoutKeepAspect_ChangesEachAxisFreely()
        {
            var options = new ResizeOptions(keepAspect: false, fromCenter: false);

            FrameEditResult corner = FrameEditMath.Resize(s_middle, FrameHandle.BottomRight, 0.1, -0.1, options, s_canvas, allowOutside: false);
            FrameEditResult edge = FrameEditMath.Resize(s_middle, FrameHandle.Bottom, 0.3, 0.1, options, s_canvas, allowOutside: false);

            AssertRect(corner.Rect, 0.25, 0.25, 0.6, 0.4);
            AssertRect(edge.Rect, 0.25, 0.25, 0.5, 0.6);
        }

        [Test]
        public void Resize_FromCenter_GrowsBothSides()
        {
            var options = new ResizeOptions(keepAspect: true, fromCenter: true);

            FrameEditResult result = FrameEditMath.Resize(s_middle, FrameHandle.Right, 0.1, 0, options, s_canvas, allowOutside: false);

            AssertRect(result.Rect, 0.15, 0.15, 0.7, 0.7);
        }

        [Test]
        public void Resize_NeverGoesBelowMinimumOrFlips()
        {
            FrameEditResult collapsed = FrameEditMath.Resize(s_middle, FrameHandle.BottomRight, -1, -1, ResizeOptions.Default, s_canvas, allowOutside: false);
            FrameEditResult crossed = FrameEditMath.Resize(s_middle, FrameHandle.TopLeft, 0.9, 0.9, ResizeOptions.Default, s_canvas, allowOutside: false);

            AssertRect(collapsed.Rect, 0.25, 0.25, 0.05, 0.05);
            AssertRect(crossed.Rect, 0.70, 0.70, 0.05, 0.05);
        }

        [Test]
        public void Resize_StaysOnScreen_UnlessAllowed()
        {
            // Room to the right is 1440 px of 960 and below 810 px of 540: both allow x1.5.
            FrameEditResult inside = FrameEditMath.Resize(s_middle, FrameHandle.BottomRight, 1, 0, ResizeOptions.Default, s_canvas, allowOutside: false);
            FrameEditResult outside = FrameEditMath.Resize(s_middle, FrameHandle.BottomRight, 1, 0, ResizeOptions.Default, s_canvas, allowOutside: true);

            AssertRect(inside.Rect, 0.25, 0.25, 0.75, 0.75);
            AssertRect(outside.Rect, 0.25, 0.25, 1.5, 1.5);
        }

        [Test]
        public void Resize_SnapsTheDraggedEdge()
        {
            var options = new ResizeOptions(keepAspect: false, fromCenter: false);
            var small = new NormalizedRect(0.1, 0.1, 0.3, 0.3);
            SnapLines snap = FrameEditMath.CreateSnapLines(null, 0.005, 0.005);

            FrameEditResult result = FrameEditMath.Resize(small, FrameHandle.Right, 0.098, 0, options, s_canvas, allowOutside: false, snap);

            Assert.That(result.Rect.Right, Is.EqualTo(0.5).Within(Tolerance));
            Assert.That(result.GuideX, Is.EqualTo(0.5));
            Assert.That(result.GuideY, Is.Null);
        }

        [Test]
        public void ZoomCrop_KeepsThePointUnderThePointer()
        {
            NormalizedRect centered = FrameEditMath.ZoomCrop(NormalizedRect.Full, 2, 0.5, 0.5);
            NormalizedRect corner = FrameEditMath.ZoomCrop(NormalizedRect.Full, 2, 0, 0);
            NormalizedRect rightEdge = FrameEditMath.ZoomCrop(NormalizedRect.Full, 2, 1, 0.5);

            AssertRect(centered, 0.25, 0.25, 0.5, 0.5);
            AssertRect(corner, 0, 0, 0.5, 0.5);
            AssertRect(rightEdge, 0.5, 0.25, 0.5, 0.5);
        }

        [Test]
        public void ZoomCrop_IsLimited()
        {
            NormalizedRect tooClose = FrameEditMath.ZoomCrop(NormalizedRect.Full, 100, 0.5, 0.5);
            NormalizedRect tooFar = FrameEditMath.ZoomCrop(new NormalizedRect(0.25, 0.25, 0.5, 0.5), 0.1, 0.5, 0.5);

            Assert.That(tooClose.Width, Is.EqualTo(FrameEditMath.MinimumCropFraction).Within(Tolerance));
            AssertRect(tooFar, 0, 0, 1, 1);
        }

        [Test]
        public void PanCrop_MovesOppositeToDragAndStaysInsideVideo()
        {
            var zoomed = new NormalizedRect(0.25, 0.25, 0.5, 0.5);

            NormalizedRect half = FrameEditMath.PanCrop(zoomed, 0.5, 0);
            NormalizedRect far = FrameEditMath.PanCrop(zoomed, 2, -2);

            AssertRect(half, 0, 0.25, 0.5, 0.5);
            AssertRect(far, 0, 0.5, 0.5, 0.5);
        }

        [Test]
        public void ZoomPercent_IsRelativeToWholeVideo()
        {
            Assert.That(FrameEditMath.ZoomPercent(NormalizedRect.Full), Is.EqualTo(100));
            Assert.That(FrameEditMath.ZoomPercent(new NormalizedRect(0, 0, 0.5, 0.5)), Is.EqualTo(200));
        }

        [Test]
        public void CropEdge_MovesFrameAndVideoEdgeTogether()
        {
            // Placement 0.5 wide shows the whole video, so 0.05 on screen is 0.1 of the video.
            FrameLayout layout = FrameLayout.Create(new NormalizedRect(0, 0, 0.5, 0.5));

            FrameLayout left = FrameEditMath.CropEdge(layout, FrameHandle.Left, 0.05, 0);
            FrameLayout bottom = FrameEditMath.CropEdge(layout, FrameHandle.Bottom, 0, -0.05);

            AssertRect(left.Placement, 0.05, 0, 0.45, 0.5);
            AssertRect(left.Crop, 0.1, 0, 0.9, 1);
            AssertRect(bottom.Placement, 0, 0, 0.5, 0.45);
            AssertRect(bottom.Crop, 0, 0, 1, 0.9);
        }

        [Test]
        public void CropEdge_CannotUncropBeyondTheVideo()
        {
            FrameLayout layout = FrameLayout.Create(new NormalizedRect(0.1, 0, 0.5, 0.5));

            FrameLayout result = FrameEditMath.CropEdge(layout, FrameHandle.Left, -0.1, 0);

            AssertRect(result.Placement, 0.1, 0, 0.5, 0.5);
            AssertRect(result.Crop, 0, 0, 1, 1);
        }

        [Test]
        public void CropEdge_FlippedFrame_CropsTheMirroredVideoEdge()
        {
            FrameLayout layout = FrameLayout.Create(new NormalizedRect(0, 0, 0.5, 0.5)).WithFlipHorizontal(true);

            FrameLayout result = FrameEditMath.CropEdge(layout, FrameHandle.Left, 0.05, 0);

            AssertRect(result.Placement, 0.05, 0, 0.45, 0.5);
            AssertRect(result.Crop, 0, 0, 0.9, 1);
        }

        [Test]
        public void Fit_AndFill_FollowTheShownAspect()
        {
            // A 4:3 video on 16:9: fit is 1440 px wide, fill is 1440 px tall.
            NormalizedRect fit = FrameEditMath.Fit(NormalizedRect.Full, 4.0 / 3.0, s_canvas);
            NormalizedRect fill = FrameEditMath.Fill(NormalizedRect.Full, 4.0 / 3.0, s_canvas);

            // Cropping a 16:9 video to its left half shows 8:9.
            NormalizedRect croppedFit = FrameEditMath.Fit(new NormalizedRect(0, 0, 0.5, 1), 16.0 / 9.0, s_canvas);

            AssertRect(fit, 0.125, 0, 0.75, 1);
            AssertRect(fill, 0, -1.0 / 6.0, 1, 4.0 / 3.0);
            AssertRect(croppedFit, 0.25, 0, 0.5, 1);
        }

        [Test]
        public void Center_KeepsSize()
        {
            NormalizedRect centered = FrameEditMath.Center(new NormalizedRect(0.7, 0.1, 0.2, 0.4));

            AssertRect(centered, 0.4, 0.3, 0.2, 0.4);
        }

        [Test]
        public void CreateSnapLines_IncludesScreenAndOtherFrames()
        {
            SnapLines lines = FrameEditMath.CreateSnapLines(new[] { new NormalizedRect(0.1, 0.2, 0.3, 0.4) }, 0.01, 0.02);

            Assert.That(lines.Vertical, Is.EquivalentTo(new[] { 0, 0.5, 1, 0.1, 0.25, 0.4 }).Using<double>((a, b) => Math.Abs(a - b) < Tolerance ? 0 : a.CompareTo(b)));
            Assert.That(lines.Horizontal, Is.EquivalentTo(new[] { 0, 0.5, 1, 0.2, 0.4, 0.6 }).Using<double>((a, b) => Math.Abs(a - b) < Tolerance ? 0 : a.CompareTo(b)));
            Assert.That(lines.ThresholdY, Is.EqualTo(0.02));
        }

        [Test]
        public void FrameLayout_WithMethodsChangeOnlyOneField()
        {
            FrameLayout layout = FrameLayout.Create(s_middle).WithOrder(3).WithLocked(true);

            FrameLayout flipped = layout.WithFlipHorizontal(true);

            Assert.That(flipped.Placement, Is.EqualTo(s_middle));
            Assert.That(flipped.Crop, Is.EqualTo(NormalizedRect.Full));
            Assert.That(flipped.Order, Is.EqualTo(3));
            Assert.That(flipped.Locked, Is.True);
            Assert.That(flipped.FlipHorizontal, Is.True);
            Assert.That(flipped, Is.Not.EqualTo(layout));
        }

        [Test]
        public void NormalizedRect_EdgesAndCenters()
        {
            NormalizedRect rect = NormalizedRect.FromEdges(0.1, 0.2, 0.5, 0.8);

            Assert.That(rect.Width, Is.EqualTo(0.4).Within(Tolerance));
            Assert.That(rect.Height, Is.EqualTo(0.6).Within(Tolerance));
            Assert.That(rect.CenterX, Is.EqualTo(0.3).Within(Tolerance));
            Assert.That(rect.CenterY, Is.EqualTo(0.5).Within(Tolerance));
        }

        private static void AssertRect(NormalizedRect actual, double x, double y, double width, double height)
        {
            string message = $"Actual {actual}";
            Assert.That(actual.X, Is.EqualTo(x).Within(Tolerance), message);
            Assert.That(actual.Y, Is.EqualTo(y).Within(Tolerance), message);
            Assert.That(actual.Width, Is.EqualTo(width).Within(Tolerance), message);
            Assert.That(actual.Height, Is.EqualTo(height).Within(Tolerance), message);
        }
    }
}
