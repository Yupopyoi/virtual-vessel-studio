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
                    new StringTable(new LocaleInfo("ja", "ja", null), new Dictionary<string, string> { { "frame.locked", "ロック中" } }),
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

        private Vector2 At(double x, double y)
        {
            return _streamArea.LocalToWorld(new Vector2((float)x * Width, (float)y * Height));
        }

        private void Pointer(EventType type, Vector2 position, EventModifiers modifiers = EventModifiers.None)
        {
            var system = new Event { type = type, mousePosition = position, button = 0, modifiers = modifiers, clickCount = 1 };
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

        private void Key(bool down, KeyCode key, EventModifiers modifiers = EventModifiers.None)
        {
            EventBase evt = down ? KeyDownEvent.GetPooled('\0', key, modifiers) : KeyUpEvent.GetPooled('\0', key, modifiers);
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
                _layout = new ObservableValue<FrameLayout>(layout);
            }

            public string Id { get; }

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
