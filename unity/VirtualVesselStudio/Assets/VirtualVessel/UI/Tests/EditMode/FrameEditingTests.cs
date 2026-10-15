using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using VirtualVessel.Core.Geometry;
using VirtualVessel.Core.State;
using VirtualVessel.UI.Frames;

namespace VirtualVessel.UI.Tests
{
    /// <summary>Undo history, menu commands, and shortcuts; input on a panel is in the PlayMode tests.</summary>
    public sealed class FrameEditingTests
    {
        private static readonly FrameLayout s_a = FrameLayout.Create(new NormalizedRect(0.1, 0.1, 0.2, 0.2));
        private static readonly FrameLayout s_b = FrameLayout.Create(new NormalizedRect(0.3, 0.1, 0.2, 0.2));
        private static readonly FrameLayout s_c = FrameLayout.Create(new NormalizedRect(0.5, 0.1, 0.2, 0.2));

        [Test]
        public void History_UndoesAndRedoesInOrder_AndANewEditClearsRedo()
        {
            var history = new FrameHistory();
            var source = new NullSource();

            history.Record(source, s_a, s_b);
            history.Record(source, s_b, s_c);
            history.Record(source, s_c, s_c);

            Assert.That(history.UndoCount, Is.EqualTo(2), "An edit that changed nothing is not recorded.");
            Assert.That(history.Undo()[0].Before, Is.EqualTo(s_b));
            Assert.That(history.Undo()[0].Before, Is.EqualTo(s_a));
            Assert.That(history.Undo(), Is.Null);
            Assert.That(history.Redo()[0].After, Is.EqualTo(s_b));
            history.Record(source, s_b, s_a);
            Assert.That(history.CanRedo, Is.False);
        }

        [Test]
        public void History_ForgetsBeyondItsCapacity()
        {
            var history = new FrameHistory();
            var source = new NullSource();
            for (int i = 0; i < FrameHistory.Capacity + 5; i++)
            {
                history.Record(source, i % 2 == 0 ? s_a : s_b, i % 2 == 0 ? s_b : s_a);
            }

            Assert.That(history.UndoCount, Is.EqualTo(FrameHistory.Capacity));
        }

        [Test]
        public void History_AStepCanChangeSeveralFrames()
        {
            var history = new FrameHistory();
            var one = new NullSource();
            var two = new NullSource();

            history.Record(new[] { new FrameEdit(one, s_a, s_b), new FrameEdit(two, s_c, s_c) });
            history.Record(new[] { new FrameEdit(one, s_a, s_a) });

            Assert.That(history.UndoCount, Is.EqualTo(1), "Unchanged frames are left out; an empty step is ignored.");
            Assert.That(history.Undo().Count, Is.EqualTo(1));
        }

        [Test]
        public void Restack_MovesOneStepOrAllTheWay_AndRenumbers()
        {
            // Front to back: b (order 7), a (order 3), c (order 3, earlier in the list so behind a).
            FrameLayout[] layouts = { s_c.WithOrder(3), s_a.WithOrder(3), s_b.WithOrder(7) };

            Assert.That(FrameCommands.Restack(layouts, 0, StackMove.Forward), Is.EqualTo(new[] { 1, 0, 2 }), "c swaps with a.");
            Assert.That(FrameCommands.Restack(layouts, 0, StackMove.ToFront), Is.EqualTo(new[] { 2, 0, 1 }));
            Assert.That(FrameCommands.Restack(layouts, 2, StackMove.Backward), Is.EqualTo(new[] { 0, 2, 1 }), "b swaps with a.");
            Assert.That(FrameCommands.Restack(layouts, 2, StackMove.Forward), Is.EqualTo(new[] { 3, 3, 7 }), "Already in front: unchanged.");
            Assert.That(FrameCommands.CanRestack(0, 3, StackMove.Forward), Is.False);
            Assert.That(FrameCommands.CanRestack(0, 3, StackMove.ToBack), Is.True);
            Assert.That(FrameCommands.CanRestack(0, 1, StackMove.ToBack), Is.False, "Alone: nothing to do.");
        }

        [Test]
        public void Commands_TurningOffAllowOutside_BringsTheFrameBack()
        {
            FrameLayout outside = new FrameLayout(new NormalizedRect(-0.1, 0.5, 0.2, 0.2), NormalizedRect.Full, allowOutside: true);

            FrameLayout inside = FrameCommands.ToggleAllowOutside(outside);

            Assert.That(inside.AllowOutside, Is.False);
            Assert.That(inside.Placement.X, Is.EqualTo(0).Within(1e-9));
        }

        [Test]
        public void Apply_RunsEachMenuCommand()
        {
            var canvas = new CanvasSize(1920, 1080);
            FrameLayout cropped = s_a.WithCrop(new NormalizedRect(0.25, 0.25, 0.5, 0.5));

            Assert.That(FrameCanvas.Apply(FrameAction.ToggleFlip, s_a, 16.0 / 9, canvas).FlipHorizontal, Is.True);
            Assert.That(FrameCanvas.Apply(FrameAction.ToggleLock, s_a, 16.0 / 9, canvas).Locked, Is.True);
            Assert.That(FrameCanvas.Apply(FrameAction.Fit, s_a, 16.0 / 9, canvas).Placement, Is.EqualTo(NormalizedRect.Full));
            Assert.That(FrameCanvas.Apply(FrameAction.Center, s_a, 16.0 / 9, canvas).Placement.CenterX, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(FrameCanvas.Apply(FrameAction.ResetCrop, cropped, 16.0 / 9, canvas).Crop, Is.EqualTo(NormalizedRect.Full));
            Assert.That(FrameCanvas.StackMoveFor(FrameAction.BringForward), Is.EqualTo(StackMove.Forward));
            Assert.That(FrameCanvas.StackMoveFor(FrameAction.Fit), Is.Null);
        }

        [Test]
        public void ActionFor_MapsTheShortcuts()
        {
            Assert.That(FrameCanvasMath.ActionFor(KeyCode.H, '\0', false, false), Is.EqualTo(FrameAction.ToggleFlip));
            Assert.That(FrameCanvasMath.ActionFor(KeyCode.L, '\0', false, false), Is.EqualTo(FrameAction.ToggleLock));
            Assert.That(FrameCanvasMath.ActionFor(KeyCode.O, '\0', false, false), Is.EqualTo(FrameAction.ToggleAllowOutside));
            Assert.That(FrameCanvasMath.ActionFor(KeyCode.C, '\0', false, false), Is.EqualTo(FrameAction.ToggleCropMode));
            Assert.That(FrameCanvasMath.ActionFor(KeyCode.C, '\0', true, false), Is.EqualTo(FrameAction.Center));
            Assert.That(FrameCanvasMath.ActionFor(KeyCode.R, '\0', false, false), Is.EqualTo(FrameAction.ResetCrop));
            Assert.That(FrameCanvasMath.ActionFor(KeyCode.Alpha1, '\0', true, false), Is.EqualTo(FrameAction.Fit));
            Assert.That(FrameCanvasMath.ActionFor(KeyCode.Alpha2, '\0', true, false), Is.EqualTo(FrameAction.Fill));
            Assert.That(FrameCanvasMath.ActionFor(KeyCode.None, ']', false, false), Is.EqualTo(FrameAction.BringForward));
            Assert.That(FrameCanvasMath.ActionFor(KeyCode.None, '[', false, false), Is.EqualTo(FrameAction.SendBackward));
            Assert.That(FrameCanvasMath.ActionFor(KeyCode.None, '}', true, false), Is.EqualTo(FrameAction.BringToFront), "Shift+] types }.");
            Assert.That(FrameCanvasMath.ActionFor(KeyCode.None, '{', true, false), Is.EqualTo(FrameAction.SendToBack));
            Assert.That(FrameCanvasMath.ActionFor(KeyCode.C, '\0', false, true), Is.EqualTo(FrameAction.None), "Ctrl+C is not crop mode.");
            Assert.That(FrameCanvasMath.ActionFor(KeyCode.Alpha1, '\0', false, false), Is.EqualTo(FrameAction.None));
            Assert.That(FrameCanvasMath.AllowedWhenLocked(FrameAction.ToggleLock), Is.True);
            Assert.That(FrameCanvasMath.AllowedWhenLocked(FrameAction.Center), Is.False);
        }

        [Test]
        public void CropPressAt_KeepsTheCroppedFrameAndItsVideo()
        {
            // The middle half of the video: the window is 0.25 to 0.75, the whole video 0 to 1 across.
            var cropped = new FrameLayout(new NormalizedRect(0.25, 0.25, 0.5, 0.5), new NormalizedRect(0.25, 0.25, 0.5, 0.5));

            Assert.That(FrameCanvasMath.CropPressAt(cropped, 0.5, 0.5), Is.EqualTo(CropPress.Window));
            Assert.That(FrameCanvasMath.CropPressAt(cropped, 0.1, 0.1), Is.EqualTo(CropPress.Video));
            Assert.That(FrameCanvasMath.CropPressAt(cropped.WithCrop(NormalizedRect.Full), 0.1, 0.1), Is.EqualTo(CropPress.Outside));
        }

        [Test]
        public void IsDoubleClick_NeedsTwoPressesCloseInTimeAndPlace()
        {
            var at = new Vector2(100, 100);

            Assert.That(FrameCanvasMath.IsDoubleClick(1000, at, 1300, at + new Vector2(3, 3)), Is.True);
            Assert.That(FrameCanvasMath.IsDoubleClick(1000, at, 1600, at), Is.False, "Too slow.");
            Assert.That(FrameCanvasMath.IsDoubleClick(1000, at, 1100, at + new Vector2(20, 0)), Is.False, "Too far apart.");
        }

        [Test]
        public void MoveTo_PutsAFrameAtAnyPosition_AndRenumbers()
        {
            // Front to back: 2, 1, 0.
            FrameLayout[] layouts = { s_a.WithOrder(0), s_b.WithOrder(1), s_c.WithOrder(2) };

            Assert.That(FrameCommands.MoveTo(layouts, 0, 0), Is.EqualTo(new[] { 2, 0, 1 }), "The back one to the front.");
            Assert.That(FrameCommands.MoveTo(layouts, 2, 1), Is.EqualTo(new[] { 0, 2, 1 }));
            Assert.That(FrameCommands.MoveTo(layouts, 1, 1), Is.EqualTo(new[] { 0, 1, 2 }), "Already there: unchanged.");
            Assert.That(FrameCommands.MoveTo(layouts, 2, 9), Is.EqualTo(new[] { 1, 2, 0 }), "Past the end: clamped to the back.");
        }

        [Test]
        public void LayerList_DropAndTargetPositions()
        {
            float[] centers = { 10, 40, 70 };

            Assert.That(LayerListMath.DropIndex(centers, 5), Is.EqualTo(0));
            Assert.That(LayerListMath.DropIndex(centers, 50), Is.EqualTo(2));
            Assert.That(LayerListMath.DropIndex(centers, 90), Is.EqualTo(3));
            Assert.That(LayerListMath.TargetPosition(0, 3), Is.EqualTo(2), "Taking the row out shifts the rest up.");
            Assert.That(LayerListMath.TargetPosition(2, 0), Is.EqualTo(0));
        }

        [Test]
        public void LayerList_KeysSkipHiddenRows_AndStatesPutHiddenFirst()
        {
            bool[] visible = { true, false, true };

            Assert.That(LayerListMath.NextVisible(visible, 0, 1), Is.EqualTo(2));
            Assert.That(LayerListMath.NextVisible(visible, 2, 1), Is.EqualTo(-1));
            Assert.That(LayerListMath.NextVisible(visible, -1, 1), Is.EqualTo(0));
            Assert.That(LayerListMath.NextVisible(visible, -1, -1), Is.EqualTo(2));
            Assert.That(LayerListMath.StateKey(false, true), Is.EqualTo(FrameStrings.Hidden));
            Assert.That(LayerListMath.StateKey(true, true), Is.EqualTo(FrameStrings.Locked));
            Assert.That(LayerListMath.StateKey(true, false), Is.Null);
        }

        [Test]
        public void CropReadoutAndPivot()
        {
            var layout = new FrameLayout(new NormalizedRect(0.25, 0.25, 0.5, 0.5), new NormalizedRect(0.25, 0.25, 0.5, 0.5));

            Assert.That(FrameCanvasMath.CropReadout(layout, new CanvasSize(1920, 1080)), Is.EqualTo("200% · 960 × 540 · 480, 270"));
            Assert.That(FrameCanvasMath.CropPivot(layout.Placement, 0.25, 0.5, false), Is.EqualTo((0.0, 0.5)));
            Assert.That(FrameCanvasMath.CropPivot(layout.Placement, 0.25, 0.5, true), Is.EqualTo((1.0, 0.5)), "Mirrored for a flipped frame.");
            Assert.That(FrameCanvasMath.ZoomFactor(-1), Is.GreaterThan(1), "Wheel up zooms in.");
        }

        private sealed class NullSource : IFrameSource
        {
            public string Id => "null";

            public IReadOnlyObservable<string> DisplayName { get; } = new ObservableValue<string>("null");

            public IReadOnlyObservable<bool> Visible { get; } = new ObservableValue<bool>(true);

            public double SourceAspect => 1;

            public Texture Video => null;

            public IReadOnlyObservable<FrameLayout> Layout { get; } = new ObservableValue<FrameLayout>(default);

            public void PreviewLayout(FrameLayout layout)
            {
            }

            public Task CommitLayoutAsync(FrameLayout layout, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }
    }
}
