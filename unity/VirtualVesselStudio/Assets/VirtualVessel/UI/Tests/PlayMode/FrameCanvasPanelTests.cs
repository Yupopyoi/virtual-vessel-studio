using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Core.Geometry;
using VirtualVessel.Core.State;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Frames;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The placement editor on a real runtime panel, driven by pointer and key events.</summary>
    public sealed class FrameCanvasPanelTests
    {
        // The stream area is 640 × 360 px, so one pixel is 1/640 of the width.
        private const float Width = 640;
        private const float Height = 360;

        private GameObject _host;
        private PanelSettings _panelSettings;
        private VisualElement _streamArea;
        private FrameCanvas _canvas;
        private List<string> _failures;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _failures = new List<string>();
            var localizer = new Localizer(
                new[]
                {
                    new StringTable(new LocaleInfo("ja", "ja", null), new Dictionary<string, string> { { "frame.locked", "ロック中" }, { "frame.hidden", "非表示" } }),
                },
                "ja",
                null);
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _host = new GameObject(nameof(FrameCanvasPanelTests));
            UIDocument document = _host.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            var scope = new UiServicesScope(new UiServices(localizer, null, null, key => _failures.Add(key)));
            document.rootVisualElement.Add(scope);
            _streamArea = new VisualElement();
            _streamArea.style.position = Position.Absolute;
            _streamArea.style.left = 40;
            _streamArea.style.top = 40;
            _streamArea.style.width = Width;
            _streamArea.style.height = Height;
            scope.Add(_streamArea);
            _canvas = new FrameCanvas();
            _streamArea.Add(_canvas);

            // Somewhere for focus to go after the last frame.
            scope.Add(new Button { focusable = true });
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_host);
            Object.Destroy(_panelSettings);
        }

        [UnityTest]
        public IEnumerator Drag_MovesWithPreviews_AndCommitsOnRelease()
        {
            var game = new FakeSource("game", new NormalizedRect(0.1, 0.1, 0.25, 0.25));
            _canvas.SetSources(new[] { game });
            yield return null;

            Pointer(EventType.MouseDown, At(0.2, 0.2));
            Pointer(EventType.MouseDrag, At(0.2, 0.2) + new Vector2(64, 36));
            Assert.That(_canvas.Selected, Is.SameAs(game));
            Assert.That(game.Previews.Count, Is.EqualTo(1), "Each drag step is previewed.");
            Assert.That(game.Commits, Is.Empty, "Nothing is committed while dragging.");
            AssertRect(_canvas.Frames[0].Shown.Placement, 0.2, 0.2, 0.25, 0.25);
            Assert.That(_canvas.ReadoutText, Is.EqualTo("480 × 270 · 384, 216"));

            Pointer(EventType.MouseUp, At(0.2, 0.2) + new Vector2(64, 36));
            yield return null;
            Assert.That(game.Commits.Count, Is.EqualTo(1));
            AssertRect(game.Layout.Value.Placement, 0.2, 0.2, 0.25, 0.25);
            Assert.That(_canvas.Frames[0].IsPending, Is.False);
        }

        [UnityTest]
        public IEnumerator Corner_ResizesKeepingTheAspect_AndShiftFreesIt()
        {
            var game = new FakeSource("game", new NormalizedRect(0.1, 0.1, 0.4, 0.4));
            _canvas.SetSources(new[] { game });
            _canvas.Select(game);
            yield return null;

            Vector2 corner = At(0.5, 0.5);
            Pointer(EventType.MouseDown, corner);
            Pointer(EventType.MouseDrag, corner + new Vector2(64, 0));
            NormalizedRect kept = _canvas.Frames[0].Shown.Placement;
            Assert.That(kept.Width, Is.EqualTo(0.5).Within(1e-6));
            Assert.That(kept.Height, Is.EqualTo(0.5).Within(1e-6), "The aspect ratio is kept.");
            AssertRect(kept, 0.1, 0.1, 0.5, 0.5);

            Pointer(EventType.MouseDrag, corner + new Vector2(64, 0), EventModifiers.Shift);
            Assert.That(_canvas.Frames[0].Shown.Placement.Height, Is.EqualTo(0.4).Within(1e-6), "Shift frees the aspect ratio.");
            Pointer(EventType.MouseUp, corner + new Vector2(64, 0), EventModifiers.Shift);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Move_SnapsToTheCenter_UnlessCtrlIsHeld()
        {
            var game = new FakeSource("game", new NormalizedRect(0.1, 0.1, 0.2, 0.2));
            _canvas.SetSources(new[] { game });
            yield return null;

            // The frame's center starts at 0.2; 0.3 would put it 5 px left of the screen center.
            Vector2 grab = At(0.2, 0.2);
            Pointer(EventType.MouseDown, grab);
            Pointer(EventType.MouseDrag, grab + new Vector2((0.3f * Width) - 5, 0));
            Assert.That(_canvas.Frames[0].Shown.Placement.CenterX, Is.EqualTo(0.5).Within(1e-6), "Snapped to the center line.");
            Assert.That(_canvas.Q(className: FrameCanvas.GuideClass + "--x").style.display.value, Is.EqualTo(DisplayStyle.Flex), "The guide shows the line.");

            Pointer(EventType.MouseDrag, grab + new Vector2((0.3f * Width) - 5, 0), EventModifiers.Control);
            Assert.That(_canvas.Frames[0].Shown.Placement.CenterX, Is.LessThan(0.5), "Ctrl turns snapping off.");
            Pointer(EventType.MouseUp, grab, EventModifiers.Control);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Rejected_Commit_ReturnsToTheConfirmedLayout()
        {
            var game = new FakeSource("game", new NormalizedRect(0.1, 0.1, 0.25, 0.25)) { Reject = true };

            // Without a log in the scope, CommandRunner writes the exception to the Unity log.
            LogAssert.ignoreFailingMessages = true;
            _canvas.SetSources(new[] { game });
            yield return null;

            Pointer(EventType.MouseDown, At(0.2, 0.2));
            Pointer(EventType.MouseDrag, At(0.4, 0.2));
            Pointer(EventType.MouseUp, At(0.4, 0.2));
            yield return null;

            Assert.That(game.Commits.Count, Is.EqualTo(1));
            AssertRect(_canvas.Frames[0].Shown.Placement, 0.1, 0.1, 0.25, 0.25);
            Assert.That(_failures, Is.EqualTo(new[] { "stage.rejected" }), "The reason is shown to the user.");
        }

        [UnityTest]
        public IEnumerator Locked_Frame_IsSelectedButNotMoved()
        {
            var game = new FakeSource("game", new FrameLayout(new NormalizedRect(0.1, 0.1, 0.25, 0.25), NormalizedRect.Full, locked: true));
            _canvas.SetSources(new[] { game });
            yield return null;

            Pointer(EventType.MouseDown, At(0.2, 0.2));
            Pointer(EventType.MouseDrag, At(0.4, 0.4));
            Pointer(EventType.MouseUp, At(0.4, 0.4));
            yield return null;

            Assert.That(_canvas.Selected, Is.SameAs(game));
            Assert.That(game.Previews, Is.Empty);
            Assert.That(game.Commits, Is.Empty);
            Assert.That(_canvas.Frames[0].ClassListContains(MediaFrame.LockedClass), Is.True);
            Assert.That(_canvas.Frames[0].Q<Label>(className: MediaFrame.LockBadgeClass).text, Is.EqualTo("ロック中"));
        }

        [UnityTest]
        public IEnumerator Arrows_Nudge_AndCommitOnceOnRelease()
        {
            var game = new FakeSource("game", new NormalizedRect(0.5, 0.5, 0.25, 0.25));
            _canvas.SetSources(new[] { game });
            _canvas.Select(game);
            _canvas.Focus();
            yield return null;

            Key(true, KeyCode.RightArrow);
            Key(true, KeyCode.RightArrow);
            Key(true, KeyCode.DownArrow, EventModifiers.Shift);
            Assert.That(game.Previews.Count, Is.EqualTo(3));
            Assert.That(game.Commits, Is.Empty, "Key repeats are not committed one by one.");
            Key(false, KeyCode.DownArrow);
            yield return null;

            Assert.That(game.Commits.Count, Is.EqualTo(1));
            Assert.That(game.Layout.Value.Placement.X, Is.EqualTo(0.5 + (2 / 1920.0)).Within(1e-9));
            Assert.That(game.Layout.Value.Placement.Y, Is.EqualTo(0.5 + (10 / 1080.0)).Within(1e-9));
        }

        [UnityTest]
        public IEnumerator Escape_CancelsADrag()
        {
            var game = new FakeSource("game", new NormalizedRect(0.1, 0.1, 0.25, 0.25));
            _canvas.SetSources(new[] { game });
            yield return null;

            Pointer(EventType.MouseDown, At(0.2, 0.2));
            Pointer(EventType.MouseDrag, At(0.4, 0.4));
            Key(true, KeyCode.Escape);
            Pointer(EventType.MouseUp, At(0.4, 0.4));
            yield return null;

            Assert.That(game.Commits, Is.Empty);
            AssertRect(game.Previews[game.Previews.Count - 1].Placement, 0.1, 0.1, 0.25, 0.25);
            AssertRect(_canvas.Frames[0].Shown.Placement, 0.1, 0.1, 0.25, 0.25);
        }

        [UnityTest]
        public IEnumerator Tab_SelectsFrontToBack_ThenReleasesFocus_AndEmptyClickDeselects()
        {
            var back = new FakeSource("back", new FrameLayout(new NormalizedRect(0, 0, 0.3, 0.3), NormalizedRect.Full, order: 0));
            var front = new FakeSource("front", new FrameLayout(new NormalizedRect(0.5, 0.5, 0.3, 0.3), NormalizedRect.Full, order: 1));
            _canvas.SetSources(new[] { back, front });
            var selections = new List<IFrameSource>();
            _canvas.SelectionChanged += selections.Add;
            yield return null;

            // Tab reaches the focused element, as it does when the user has tabbed into the canvas.
            _canvas.Focus();
            Navigate(NavigationMoveEvent.Direction.Next);
            Navigate(NavigationMoveEvent.Direction.Next);
            Navigate(NavigationMoveEvent.Direction.Next);
            Assert.That(selections, Is.EqualTo(new IFrameSource[] { front, back, null }));

            _canvas.Select(front);
            Pointer(EventType.MouseDown, At(0.95, 0.1));
            Pointer(EventType.MouseUp, At(0.95, 0.1));
            Assert.That(_canvas.Selected, Is.Null);
        }

        [UnityTest]
        public IEnumerator CropMode_TogglesByKeyDoubleClickAndSwitch_AndEndsWithEnterOrOutsideClick()
        {
            var game = new FakeSource("game", new NormalizedRect(0.1, 0.1, 0.4, 0.4));
            _canvas.SetSources(new[] { game });
            _canvas.Select(game);
            _canvas.Focus();
            yield return null;

            VisualElement modeSwitch = _canvas.Q(className: FrameCanvas.ModeSwitchClass);
            Assert.That(modeSwitch.style.display.value, Is.EqualTo(DisplayStyle.Flex), "The mode is always shown for an editable frame.");
            Key(true, KeyCode.C);
            Assert.That(_canvas.CropMode, Is.True);
            Assert.That(_canvas.Frames[0].ClassListContains(MediaFrame.CropClass), Is.True);
            Assert.That(_canvas.Q(className: FrameCanvas.GhostClass).style.display.value, Is.EqualTo(DisplayStyle.Flex), "The whole video shows faintly.");
            Key(true, KeyCode.Return);
            Assert.That(_canvas.CropMode, Is.False);
            Assert.That(_canvas.Selected, Is.SameAs(game), "Enter ends crop mode but keeps the selection.");

            DoubleClick(At(0.3, 0.3));
            Assert.That(_canvas.CropMode, Is.True, "Double-click switches to crop mode...");
            DoubleClick(At(0.3, 0.3));
            Assert.That(_canvas.CropMode, Is.False, "...and back, the same way.");
            DoubleClick(At(0.3, 0.3));
            Assert.That(_canvas.CropMode, Is.True);
            Pointer(EventType.MouseDown, At(0.9, 0.9));
            Pointer(EventType.MouseUp, At(0.9, 0.9));
            Assert.That(_canvas.CropMode, Is.False, "A click outside leaves crop mode...");
            Assert.That(_canvas.Selected, Is.Null, "...and clears the selection in one go.");

            _canvas.Select(game);
            modeSwitch.Q<Segmented>().value = 1;
            Assert.That(_canvas.CropMode, Is.True, "The switch changes the mode.");
        }

        [UnityTest]
        public IEnumerator CropMode_EdgeHandleCutsTheVideo_KeepingItsScale()
        {
            var game = new FakeSource("game", new NormalizedRect(0.1, 0.1, 0.4, 0.4));
            _canvas.SetSources(new[] { game });
            _canvas.Select(game);
            _canvas.SetCropMode(true);
            yield return null;

            // The right edge handle moved left by a quarter of the frame.
            Vector2 right = At(0.5, 0.3);
            Pointer(EventType.MouseDown, right);
            Pointer(EventType.MouseDrag, right - new Vector2(0.1f * Width, 0));
            Pointer(EventType.MouseUp, right - new Vector2(0.1f * Width, 0));
            yield return null;

            FrameLayout cut = game.Layout.Value;
            Assert.That(cut.Crop.Right, Is.EqualTo(0.75).Within(1e-4));
            AssertRect(cut.Placement, 0.1, 0.1, 0.3, 0.4);
        }

        [UnityTest]
        public IEnumerator Wheel_ZoomsAsOneEdit_PanAndUndoRedo()
        {
            var game = new FakeSource("game", new NormalizedRect(0.1, 0.1, 0.4, 0.4));
            _canvas.SetSources(new[] { game });
            _canvas.Focus();
            yield return null;

            Wheel(At(0.3, 0.3), -5);
            Wheel(At(0.3, 0.3), -5);
            Wheel(At(0.3, 0.3), -5);
            Assert.That(game.Previews.Count, Is.EqualTo(3));
            Assert.That(game.Commits, Is.Empty, "Wheel steps are not committed one by one.");
            Assert.That(_canvas.ReadoutText, Does.Contain("%"));
            yield return new WaitForSecondsRealtime(0.7f);
            Assert.That(game.Commits.Count, Is.EqualTo(1), "A burst is one edit.");
            Assert.That(_canvas.History.UndoCount, Is.EqualTo(1));
            NormalizedRect zoomed = game.Layout.Value.Crop;
            Assert.That(zoomed.Width, Is.LessThan(1));

            // The middle button pans the zoomed video without moving the frame.
            Pointer(EventType.MouseDown, At(0.3, 0.3), button: 2);
            Pointer(EventType.MouseDrag, At(0.35, 0.3), button: 2);
            Pointer(EventType.MouseUp, At(0.35, 0.3), button: 2);
            yield return null;
            Assert.That(game.Layout.Value.Crop.X, Is.LessThan(zoomed.X), "Dragging right reveals the left.");
            AssertRect(game.Layout.Value.Placement, 0.1, 0.1, 0.4, 0.4);

            Key(true, KeyCode.Z, EventModifiers.Control);
            Key(true, KeyCode.Z, EventModifiers.Control);
            yield return null;
            Assert.That(game.Layout.Value.Crop, Is.EqualTo(NormalizedRect.Full), "Two undos: the pan, then the zoom.");
            Key(true, KeyCode.Y, EventModifiers.Control);
            yield return null;
            Assert.That(game.Layout.Value.Crop, Is.EqualTo(zoomed));
        }

        [UnityTest]
        public IEnumerator RightClick_OpensTheMenu_AndAnEntryRunsAndCloses()
        {
            var game = new FakeSource("game", new NormalizedRect(0.1, 0.1, 0.4, 0.4));
            _canvas.SetSources(new[] { game });
            yield return null;

            Pointer(EventType.MouseDown, At(0.3, 0.3), button: 1);
            yield return null;
            PopupMenu menu = _canvas.Menu;
            Assert.That(menu, Is.Not.Null);
            Assert.That(_canvas.Selected, Is.SameAs(game), "Right-click selects.");
            Assert.That(menu.Items.Count, Is.EqualTo(12));
            Assert.That(menu.HighlightIndex, Is.GreaterThanOrEqualTo(0), "An entry is ready for the keyboard.");

            // On this small test screen the menu may open over the pointer, which highlights the entry
            // under it; the up arrow walks back to the first entry.
            while (menu.HighlightIndex > 0)
            {
                using (NavigationMoveEvent evt = NavigationMoveEvent.GetPooled(NavigationMoveEvent.Direction.Up))
                {
                    evt.target = menu;
                    menu.SendEvent(evt);
                }
            }

            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = menu;
                menu.SendEvent(evt);
            }

            yield return null;
            Assert.That(_canvas.Menu, Is.Null);
            Assert.That(game.Layout.Value.FlipHorizontal, Is.True, "The first entry flips the video.");
        }

        [UnityTest]
        public IEnumerator Menu_OnALockedFrame_OffersOnlyUnlockAndFlip()
        {
            var game = new FakeSource("game", new FrameLayout(new NormalizedRect(0.1, 0.1, 0.4, 0.4), NormalizedRect.Full, locked: true));
            _canvas.SetSources(new[] { game });
            _canvas.Select(game);
            yield return null;

            _canvas.ShowMenu(At(0.3, 0.3));
            yield return null;
            var enabled = new List<bool>();
            foreach (VisualElement item in _canvas.Menu.Items)
            {
                enabled.Add(!item.ClassListContains(PopupMenu.ItemClass + "--disabled"));
            }

            Assert.That(enabled, Is.EqualTo(new[] { true, true, false, false, false, false, false, false, false, false, false, false }));
            _canvas.Menu.Close();
            Assert.That(_canvas.Menu, Is.Null);
        }

        [UnityTest]
        public IEnumerator Shortcuts_ChangeOrderFitAndFlip_AndAreUndoable()
        {
            var back = new FakeSource("back", new FrameLayout(new NormalizedRect(0.1, 0.1, 0.3, 0.3), NormalizedRect.Full, order: 0));
            var front = new FakeSource("front", new FrameLayout(new NormalizedRect(0.5, 0.5, 0.3, 0.3), NormalizedRect.Full, order: 1));
            _canvas.SetSources(new[] { back, front });
            _canvas.Select(back);
            _canvas.Focus();
            yield return null;

            Key(true, KeyCode.None, character: ']');
            yield return null;
            Assert.That(back.Layout.Value.Order, Is.GreaterThan(front.Layout.Value.Order), "] brings it one step forward, past the other.");

            Key(true, KeyCode.Alpha1, EventModifiers.Shift);
            yield return null;
            AssertRect(back.Layout.Value.Placement, 0, 0, 1, 1);

            Key(true, KeyCode.H);
            yield return null;
            Assert.That(back.Layout.Value.FlipHorizontal, Is.True);

            Key(true, KeyCode.Z, EventModifiers.Control);
            Key(true, KeyCode.Z, EventModifiers.Control);
            Key(true, KeyCode.Z, EventModifiers.Control);
            yield return null;
            Assert.That(back.Layout.Value, Is.EqualTo(new FrameLayout(new NormalizedRect(0.1, 0.1, 0.3, 0.3), NormalizedRect.Full, order: 0)));
        }

        [UnityTest]
        public IEnumerator CropMode_DragInsideMovesTheWindow_NotTheVideo()
        {
            // The middle half of the video, zoomed: the whole video sits at 0 to 0.8 across.
            var game = new FakeSource("game", new FrameLayout(new NormalizedRect(0.2, 0.2, 0.4, 0.4), new NormalizedRect(0.25, 0.25, 0.5, 0.5)));
            _canvas.SetSources(new[] { game });
            _canvas.Select(game);
            _canvas.SetCropMode(true);
            yield return null;

            Pointer(EventType.MouseDown, At(0.4, 0.4));
            Pointer(EventType.MouseDrag, At(0.5, 0.4));
            Pointer(EventType.MouseUp, At(0.5, 0.4));
            yield return null;

            FrameLayout moved = game.Layout.Value;
            AssertRect(moved.Placement, 0.3, 0.2, 0.4, 0.4);
            Assert.That(moved.Crop.X, Is.EqualTo(0.375).Within(1e-4), "The window moved over the video.");
            AssertRect(FrameEditMath.VideoPlacement(moved), 0, 0, 0.8, 0.8);
        }

        [UnityTest]
        public IEnumerator SelectingAnotherFrame_StartsInPlacementMode()
        {
            var game = new FakeSource("game", new NormalizedRect(0.05, 0.05, 0.3, 0.3));
            var desk = new FakeSource("desk", new NormalizedRect(0.6, 0.6, 0.3, 0.3));
            _canvas.SetSources(new[] { game, desk });
            _canvas.Select(game);
            _canvas.SetCropMode(true);
            yield return null;

            Pointer(EventType.MouseDown, At(0.75, 0.75));
            Pointer(EventType.MouseUp, At(0.75, 0.75));

            Assert.That(_canvas.Selected, Is.SameAs(desk));
            Assert.That(_canvas.CropMode, Is.False, "Crop mode does not carry over to another frame.");
        }

        [UnityTest]
        public IEnumerator CropMode_StaysOnClicksOverTheCroppedFrameAndItsVideo()
        {
            // The game is zoomed (the whole video spans 0 to 0.8), and the camera sits in front of it.
            var game = new FakeSource("game", new FrameLayout(new NormalizedRect(0.2, 0.2, 0.4, 0.4), new NormalizedRect(0.25, 0.25, 0.5, 0.5), order: 0));
            var camera = new FakeSource("camera", new FrameLayout(new NormalizedRect(0.45, 0.45, 0.2, 0.2), NormalizedRect.Full, order: 1));
            _canvas.SetSources(new[] { game, camera });
            _canvas.Select(game);
            yield return null;

            // Switched with the mode switch, as a user would.
            _canvas.Q(className: FrameCanvas.ModeSwitchClass).Q<Segmented>().value = 1;
            Assert.That(_canvas.CropMode, Is.True);

            Pointer(EventType.MouseDown, At(0.1, 0.1));
            Pointer(EventType.MouseUp, At(0.1, 0.1));
            Assert.That(_canvas.CropMode, Is.True, "A click on the faint whole video stays in crop mode.");

            Pointer(EventType.MouseDown, At(0.5, 0.5));
            Pointer(EventType.MouseUp, At(0.5, 0.5));
            Assert.That(_canvas.Selected, Is.SameAs(game), "The frame being cropped wins over the camera in front.");
            Assert.That(_canvas.CropMode, Is.True);

            Pointer(EventType.MouseDown, At(0.95, 0.95));
            Pointer(EventType.MouseUp, At(0.95, 0.95));
            Assert.That(_canvas.CropMode, Is.False, "A click clearly outside does.");
            Assert.That(_canvas.Selected, Is.Null);
        }

        [UnityTest]
        public IEnumerator AClickAfterTheModeSwitch_IsNotADoubleClick()
        {
            var game = new FakeSource("game", new NormalizedRect(0.1, 0.1, 0.4, 0.4));
            var desk = new FakeSource("desk", new NormalizedRect(0.6, 0.6, 0.3, 0.3));
            _canvas.SetSources(new[] { game, desk });
            _canvas.Select(game);
            yield return null;

            Segmented modes = _canvas.Q(className: FrameCanvas.ModeSwitchClass).Q<Segmented>();
            modes.value = 1;
            modes.value = 0;

            // The platform counts this press as the second click after the one on the switch.
            Pointer(EventType.MouseDown, At(0.3, 0.3), clickCount: 2);
            Pointer(EventType.MouseUp, At(0.3, 0.3), clickCount: 2);
            Assert.That(_canvas.CropMode, Is.False, "One press on the frame is not a double-click.");

            // Quick presses on two different frames are not a double-click either.
            Pointer(EventType.MouseDown, At(0.75, 0.75));
            Pointer(EventType.MouseUp, At(0.75, 0.75));
            Assert.That(_canvas.Selected, Is.SameAs(desk));
            Assert.That(_canvas.CropMode, Is.False);
        }

        [UnityTest]
        public IEnumerator Drag_IsUndoable()
        {
            var game = new FakeSource("game", new NormalizedRect(0.1, 0.1, 0.25, 0.25));
            _canvas.SetSources(new[] { game });
            _canvas.Focus();
            yield return null;

            Pointer(EventType.MouseDown, At(0.2, 0.2));
            Pointer(EventType.MouseDrag, At(0.4, 0.2));
            Pointer(EventType.MouseUp, At(0.4, 0.2));
            yield return null;
            Key(true, KeyCode.Z, EventModifiers.Control);
            yield return null;
            AssertRect(game.Layout.Value.Placement, 0.1, 0.1, 0.25, 0.25);
            Key(true, KeyCode.Z, EventModifiers.Control | EventModifiers.Shift);
            yield return null;
            AssertRect(game.Layout.Value.Placement, 0.3, 0.1, 0.25, 0.25);
        }

        [UnityTest]
        public IEnumerator LayerList_ShowsFrontToBack_WithNamesAndStates_AndSharesSelection()
        {
            var back = new FakeSource("back", new FrameLayout(new NormalizedRect(0, 0, 0.3, 0.3), NormalizedRect.Full, order: 0));
            var middle = new FakeSource("middle", new FrameLayout(new NormalizedRect(0.3, 0.3, 0.3, 0.3), NormalizedRect.Full, locked: true, order: 1));
            var front = new FakeSource("front", new FrameLayout(new NormalizedRect(0.6, 0.6, 0.3, 0.3), NormalizedRect.Full, order: 2));
            back.Shown.Value = false;
            _canvas.SetSources(new[] { back, middle, front });
            var list = new LayerList(_canvas);
            _streamArea.parent.Add(list);
            yield return null;

            Assert.That(RowNames(list), Is.EqualTo(new[] { "front", "middle", "back" }), "Top is the front.");
            Assert.That(RowState(list, 1), Is.EqualTo("ロック中"));
            Assert.That(RowState(list, 2), Is.EqualTo("非表示"));

            ClickRow(list.Rows[0]);
            Assert.That(_canvas.Selected, Is.SameAs(front), "Clicking a row selects its video.");
            ClickRow(list.Rows[2]);
            Assert.That(_canvas.Selected, Is.SameAs(front), "A hidden video cannot be selected.");
            _canvas.Select(middle);
            Assert.That(list.Rows[1].ClassListContains(LayerList.SelectedClass), Is.True, "The preview's selection shows in the list.");

            front.Name.Value = "Game";
            Assert.That(RowNames(list)[0], Is.EqualTo("Game"), "Names follow Stage.");
        }

        [UnityTest]
        public IEnumerator LayerList_DragReorders_AsOneUndoableStep_AndKeysMatch()
        {
            var a = new FakeSource("a", new FrameLayout(new NormalizedRect(0, 0, 0.3, 0.3), NormalizedRect.Full, order: 0));
            var b = new FakeSource("b", new FrameLayout(new NormalizedRect(0.3, 0.3, 0.3, 0.3), NormalizedRect.Full, order: 1));
            var c = new FakeSource("c", new FrameLayout(new NormalizedRect(0.6, 0.6, 0.3, 0.3), NormalizedRect.Full, order: 2));
            _canvas.SetSources(new[] { a, b, c });
            var list = new LayerList(_canvas);
            list.style.position = Position.Absolute;
            list.style.left = 700;
            list.style.top = 40;
            list.style.width = 200;
            _streamArea.parent.Add(list);
            yield return null;
            Assert.That(RowNames(list), Is.EqualTo(new[] { "c", "b", "a" }));

            // Drag the back row (a) above the front row.
            VisualElement row = list.Rows[2];
            Vector2 from = row.worldBound.center;
            Vector2 to = new Vector2(from.x, list.Rows[0].worldBound.yMin + 2);
            RowPointer(row, EventType.MouseDown, from);
            RowPointer(row, EventType.MouseDrag, to);
            VisualElement drop = list.Q(className: LayerList.DropClass);
            Assert.That(drop.style.display.value, Is.EqualTo(DisplayStyle.Flex), "The landing line shows.");
            float firstRowTop = list.Rows[0].layout.yMin;
            yield return null;
            Assert.That(list.Rows[0].layout.yMin, Is.EqualTo(firstRowTop), "The line does not push the rows.");
            RowPointer(row, EventType.MouseDrag, to + new Vector2(0, 1));
            Assert.That(drop.style.top.value.value, Is.EqualTo(firstRowTop - 1), "The landing row stays put on a small move.");
            RowPointer(row, EventType.MouseUp, to);
            yield return null;

            Assert.That(RowNames(list), Is.EqualTo(new[] { "a", "c", "b" }));
            Assert.That(drop.style.display.value, Is.EqualTo(DisplayStyle.None));
            _canvas.Focus();
            Key(true, KeyCode.Z, EventModifiers.Control);
            yield return null;
            Assert.That(RowNames(list), Is.EqualTo(new[] { "c", "b", "a" }), "One undo puts every video back.");

            // Keyboard: down moves the selection, Ctrl+Up moves the selected video toward the front.
            _canvas.Select(null);
            list.Focus();
            ListKey(list, KeyCode.DownArrow);
            Assert.That(_canvas.Selected, Is.SameAs(c));
            ListKey(list, KeyCode.DownArrow);
            Assert.That(_canvas.Selected, Is.SameAs(b));
            ListKey(list, KeyCode.UpArrow, EventModifiers.Control);
            yield return null;
            Assert.That(RowNames(list), Is.EqualTo(new[] { "b", "c", "a" }));
        }

        [UnityTest]
        public IEnumerator HiddenVideo_CannotBeGrabbed_AndLosesTheSelection()
        {
            var game = new FakeSource("game", new NormalizedRect(0.1, 0.1, 0.4, 0.4));
            _canvas.SetSources(new[] { game });
            _canvas.Select(game);
            yield return null;

            game.Shown.Value = false;
            Assert.That(_canvas.Selected, Is.Null, "A video that leaves the stream loses the selection.");
            Pointer(EventType.MouseDown, At(0.3, 0.3));
            Pointer(EventType.MouseUp, At(0.3, 0.3));
            Assert.That(_canvas.Selected, Is.Null, "It cannot be clicked either.");
            Assert.That(_canvas.Frames[0].style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator ShowVideos_DrawsTheVideosInStackingOrder_WithCropAndFlip()
        {
            var back = new FakeSource("back", new FrameLayout(new NormalizedRect(0, 0, 0.5, 0.5), new NormalizedRect(0.25, 0, 0.5, 1), flipHorizontal: true, order: 5));
            var front = new FakeSource("front", new FrameLayout(new NormalizedRect(0.2, 0.2, 0.5, 0.5), NormalizedRect.Full, order: 9));
            _canvas.SetSources(new[] { front, back });
            _canvas.ShowVideos = true;
            yield return null;

            List<Image> videos = _canvas.Query<Image>().Where(i => !i.ClassListContains(FrameCanvas.GhostClass)).ToList();
            Assert.That(videos.Count, Is.EqualTo(2));
            Image backVideo = videos[0];
            Assert.That(backVideo.uv, Is.EqualTo(new Rect(0.25f, 0, 0.5f, 1)), "The crop is a UV rectangle, not a copy.");
            Assert.That(backVideo.style.scale.value.value.x, Is.EqualTo(-1), "The flip is a scale.");
            Assert.That(videos[1].style.left.value.value, Is.EqualTo(20).Within(1e-3), "The front video is drawn last.");
        }

        private static List<string> RowNames(LayerList list)
        {
            var names = new List<string>();
            foreach (VisualElement row in list.Rows)
            {
                names.Add(row.Q<Label>(className: LayerList.UssClass + "__name").text);
            }

            return names;
        }

        private static string RowState(LayerList list, int index)
        {
            return list.Rows[index].Q<Label>(className: LayerList.UssClass + "__state").text;
        }

        private static void ClickRow(VisualElement row)
        {
            RowPointer(row, EventType.MouseDown, row.worldBound.center);
            RowPointer(row, EventType.MouseUp, row.worldBound.center);
        }

        private static void RowPointer(VisualElement row, EventType type, Vector2 position)
        {
            var system = new Event { type = type, mousePosition = position, button = 0, clickCount = 1 };
            EventBase evt = type switch
            {
                EventType.MouseDown => PointerDownEvent.GetPooled(system),
                EventType.MouseUp => PointerUpEvent.GetPooled(system),
                _ => PointerMoveEvent.GetPooled(system),
            };
            using (evt)
            {
                evt.target = row;
                row.SendEvent(evt);
            }
        }

        private static void ListKey(LayerList list, KeyCode key, EventModifiers modifiers = EventModifiers.None)
        {
            using (KeyDownEvent evt = KeyDownEvent.GetPooled('\0', key, modifiers))
            {
                evt.target = list;
                list.SendEvent(evt);
            }
        }

        private Vector2 At(double x, double y)
        {
            return _streamArea.LocalToWorld(new Vector2((float)x * Width, (float)y * Height));
        }

        private void Pointer(EventType type, Vector2 position, EventModifiers modifiers = EventModifiers.None, int button = 0, int clickCount = 1)
        {
            var system = new Event { type = type, mousePosition = position, button = button, modifiers = modifiers, clickCount = clickCount };
            EventBase evt = type switch
            {
                EventType.MouseDown => PointerDownEvent.GetPooled(system),
                EventType.MouseUp => PointerUpEvent.GetPooled(system),
                _ => PointerMoveEvent.GetPooled(system),
            };
            using (evt)
            {
                evt.target = _canvas;
                _canvas.SendEvent(evt);
            }
        }

        private void DoubleClick(Vector2 position)
        {
            Pointer(EventType.MouseDown, position);
            Pointer(EventType.MouseUp, position);
            Pointer(EventType.MouseDown, position, clickCount: 2);
            Pointer(EventType.MouseUp, position, clickCount: 2);
        }

        private void Wheel(Vector2 position, float deltaY)
        {
            var system = new Event { type = EventType.ScrollWheel, mousePosition = position, delta = new Vector2(0, deltaY) };
            using (WheelEvent evt = WheelEvent.GetPooled(system))
            {
                evt.target = _canvas;
                _canvas.SendEvent(evt);
            }
        }

        private void Key(bool down, KeyCode key, EventModifiers modifiers = EventModifiers.None, char character = '\0')
        {
            EventBase evt = down ? KeyDownEvent.GetPooled(character, key, modifiers) : KeyUpEvent.GetPooled(character, key, modifiers);
            using (evt)
            {
                evt.target = _canvas;
                _canvas.SendEvent(evt);
            }
        }

        private void Navigate(NavigationMoveEvent.Direction direction)
        {
            using (NavigationMoveEvent evt = NavigationMoveEvent.GetPooled(direction))
            {
                evt.target = _canvas;
                _canvas.SendEvent(evt);
            }
        }

        private static void AssertRect(NormalizedRect actual, double x, double y, double width, double height)
        {
            Assert.That(actual.X, Is.EqualTo(x).Within(1e-4), "x");
            Assert.That(actual.Y, Is.EqualTo(y).Within(1e-4), "y");
            Assert.That(actual.Width, Is.EqualTo(width).Within(1e-4), "width");
            Assert.That(actual.Height, Is.EqualTo(height).Within(1e-4), "height");
        }

        /// <summary>Stands in for Stage: records previews and commits, and can reject them.</summary>
        private sealed class FakeSource : IFrameSource
        {
            private readonly ObservableValue<FrameLayout> _layout;

            public FakeSource(string id, NormalizedRect placement)
                : this(id, FrameLayout.Create(placement))
            {
            }

            public FakeSource(string id, FrameLayout layout)
            {
                Id = id;
                Name.Value = id;
                _layout = new ObservableValue<FrameLayout>(layout);
            }

            public string Id { get; }

            public ObservableValue<string> Name { get; } = new ObservableValue<string>(string.Empty);

            public ObservableValue<bool> Shown { get; } = new ObservableValue<bool>(true);

            public IReadOnlyObservable<string> DisplayName => Name;

            public IReadOnlyObservable<bool> Visible => Shown;

            public double SourceAspect => 16.0 / 9.0;

            public Texture Video => null;

            public IReadOnlyObservable<FrameLayout> Layout => _layout;

            public List<FrameLayout> Previews { get; } = new List<FrameLayout>();

            public List<FrameLayout> Commits { get; } = new List<FrameLayout>();

            public bool Reject { get; set; }

            public void PreviewLayout(FrameLayout layout)
            {
                Previews.Add(layout);
            }

            public Task CommitLayoutAsync(FrameLayout layout, CancellationToken cancellationToken)
            {
                Commits.Add(layout);
                if (Reject)
                {
                    throw new Commands.UserFacingException("stage.rejected");
                }

                _layout.Value = layout;
                return Task.CompletedTask;
            }
        }
    }
}
