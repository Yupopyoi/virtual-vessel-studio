using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.Core.Geometry;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.Frames
{
    /// <summary>
    /// A transparent layer over the stream preview for editing where videos are placed (UI foundation
    /// detailed design 9). It turns pointer and key input into <see cref="FrameEditMath"/> calls, shows
    /// each step through <see cref="IFrameSource.PreviewLayout"/>, and commits when a drag or key press
    /// ends.
    /// </summary>
    /// <remarks>
    /// Put it inside the element that shows the stream screen; the layer spans that element and reaches
    /// <see cref="HandleRadius"/> beyond it, so handles on the edge can still be grabbed. Only styles
    /// change while dragging; no video is copied (UI foundation detailed design 16).
    /// </remarks>
    [UxmlElement]
    public partial class FrameCanvas : VisualElement
    {
        public const string UssClass = "vv-frame-canvas";

        public const string StreamClass = UssClass + "__stream";

        public const string GuideClass = UssClass + "__guide";

        public const string ReadoutClass = UssClass + "__readout";

        /// <summary>A handle can be grabbed this many pixels from its center: a 24px target (components.md 2).</summary>
        public const float HandleRadius = 12f;

        /// <summary>Frames snap to lines this many screen pixels away (UI foundation detailed design 9.3).</summary>
        public const float SnapDistance = 8f;

        /// <summary>How long the readout stays after an edit ends, in milliseconds.</summary>
        private const long ReadoutLinger = 700;

        private readonly VisualElement _stream;
        private readonly VisualElement _guideX;
        private readonly VisualElement _guideY;
        private readonly Label _readout;
        private readonly List<MediaFrame> _frames = new List<MediaFrame>();
        private MediaFrame _selected;
        private Gesture _gesture;
        private MediaFrame _nudged;
        private int _nudgeVersion;
        private IVisualElementScheduledItem _hideReadout;

        public FrameCanvas()
        {
            AddToClassList(UssClass);
            focusable = true;
            tabIndex = 0;

            // The geometry is part of how the canvas works, not of its look, so it is set here rather
            // than in USS: the layer reaches HandleRadius beyond the stream screen on every side.
            Inset(this, -HandleRadius);
            FocusRing.Attach(this);

            _stream = new VisualElement { pickingMode = PickingMode.Ignore };
            _stream.AddToClassList(StreamClass);
            Inset(_stream, HandleRadius);
            Add(_stream);
            _guideX = Guide(vertical: true);
            _guideY = Guide(vertical: false);
            _readout = QuietText.Of(new Label { pickingMode = PickingMode.Ignore });
            _readout.AddToClassList(ReadoutClass);
            _readout.AddToClassList("vv-text-mono");
            _readout.style.display = DisplayStyle.None;
            _stream.Add(_readout);

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(evt => EndGesture(evt.pointerId, commit: true));
            RegisterCallback<PointerCaptureOutEvent>(_ => EndGesture(-1, commit: true));
            RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (_gesture == null)
                {
                    CursorService.Reset();
                }
            });
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<KeyUpEvent>(OnKeyUp);
            RegisterCallback<NavigationMoveEvent>(OnNavigationMove);
            RegisterCallback<FocusInEvent>(OnFocusIn);
            RegisterCallback<FocusOutEvent>(_ => CommitNudge());
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                EndGesture(-1, commit: true);
                CommitNudge();
                CursorService.Reset();
            });
        }

        /// <summary>Size of the stream screen in output pixels; aspect ratios, minimum sizes, and nudges use it.</summary>
        public CanvasSize OutputSize { get; set; } = new CanvasSize(1920, 1080);

        public IReadOnlyList<MediaFrame> Frames => _frames;

        public IFrameSource Selected => _selected?.Source;

        /// <summary>Whether the readout of position and size is on screen.</summary>
        public bool ReadoutVisible => _readout.style.display == DisplayStyle.Flex;

        public string ReadoutText => _readout.text;

        public event Action<IFrameSource> SelectionChanged;

        /// <summary>The videos to edit. Frames that are no longer listed are removed with their selection.</summary>
        public void SetSources(IEnumerable<IFrameSource> sources)
        {
            if (sources == null)
            {
                throw new ArgumentNullException(nameof(sources));
            }

            EndGesture(-1, commit: true);
            CommitNudge();
            IFrameSource selected = Selected;
            foreach (MediaFrame frame in _frames)
            {
                frame.RemoveFromHierarchy();
            }

            _frames.Clear();
            _selected = null;
            foreach (IFrameSource source in sources)
            {
                var frame = new MediaFrame(source);
                _frames.Add(frame);
                _stream.Insert(_stream.IndexOf(_readout), frame);
            }

            MediaFrame keep = _frames.Find(f => f.Source == selected);
            Select(keep?.Source);
        }

        public void Select(IFrameSource source)
        {
            MediaFrame frame = source == null ? null : _frames.Find(f => f.Source == source);
            if (frame == _selected)
            {
                return;
            }

            CommitNudge();
            if (_selected != null)
            {
                _selected.Selected = false;
                _selected.HoverHandle = null;
            }

            _selected = frame;
            if (frame != null)
            {
                frame.Selected = true;

                // Above the other outlines; the readout and guides stay on top.
                frame.PlaceBehind(_guideX);
            }

            SelectionChanged?.Invoke(Selected);
        }

        private static void Inset(VisualElement element, float inset)
        {
            element.style.position = Position.Absolute;
            element.style.left = inset;
            element.style.top = inset;
            element.style.right = inset;
            element.style.bottom = inset;
        }

        private VisualElement Guide(bool vertical)
        {
            var guide = new VisualElement { pickingMode = PickingMode.Ignore };
            guide.AddToClassList(GuideClass);
            guide.AddToClassList(GuideClass + (vertical ? "--x" : "--y"));
            guide.style.display = DisplayStyle.None;
            _stream.Add(guide);
            return guide;
        }

        private List<FrameLayout> ShownLayouts()
        {
            var layouts = new List<FrameLayout>(_frames.Count);
            foreach (MediaFrame frame in _frames)
            {
                layouts.Add(frame.Shown);
            }

            return layouts;
        }

        private Vector2 StreamPixels(Vector2 panelPosition)
        {
            return _stream.WorldToLocal(panelPosition);
        }

        private Vector2 StreamSize()
        {
            Rect r = _stream.contentRect;
            return new Vector2(Mathf.Max(1, r.width), Mathf.Max(1, r.height));
        }

        private FrameHandle? HandleUnder(Vector2 panelPosition)
        {
            if (_selected == null || _selected.Shown.Locked)
            {
                return null;
            }

            Vector2 p = StreamPixels(panelPosition);
            Vector2 size = StreamSize();
            return FrameCanvasMath.HandleAt(_selected.Shown.Placement, p.x, p.y, size.x, size.y, HandleRadius);
        }

        private MediaFrame FrameUnder(Vector2 panelPosition)
        {
            Vector2 p = StreamPixels(panelPosition);
            Vector2 size = StreamSize();
            int index = FrameCanvasMath.FrameAt(ShownLayouts(), p.x / size.x, p.y / size.y);
            return index < 0 ? null : _frames[index];
        }

        // ------------------------------------------------------------------ Pointer

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            Focus();
            CommitNudge();
            FrameHandle? handle = HandleUnder(evt.position);
            if (handle.HasValue)
            {
                Begin(evt, GestureKind.Resize, _selected, handle.Value);
                return;
            }

            MediaFrame hit = FrameUnder(evt.position);
            Select(hit?.Source);
            if (hit != null && !hit.Shown.Locked)
            {
                Begin(evt, GestureKind.Move, hit, FrameHandle.TopLeft);
            }
        }

        private void Begin(PointerDownEvent evt, GestureKind kind, MediaFrame frame, FrameHandle handle)
        {
            _gesture = new Gesture(kind, frame, handle, evt.position, frame.Shown);
            this.CapturePointer(evt.pointerId);
            ShowReadout(frame.Shown.Placement);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_gesture == null)
            {
                FrameHandle? handle = HandleUnder(evt.position);
                if (_selected != null)
                {
                    _selected.HoverHandle = handle;
                }

                MediaFrame hit = handle.HasValue ? _selected : FrameUnder(evt.position);
                CursorService.Set(FrameCanvasMath.CursorFor(handle, hit != null, hit != null && hit.Shown.Locked));
                return;
            }

            Vector2 size = StreamSize();
            Vector2 start = StreamPixels(_gesture.StartPointer);
            Vector2 now = StreamPixels(evt.position);
            double dx = (now.x - start.x) / size.x;
            double dy = (now.y - start.y) / size.y;
            bool snapOff = evt.ctrlKey || evt.commandKey;
            SnapLines snap = snapOff ? SnapLines.None : FrameEditMath.CreateSnapLines(OtherPlacements(_gesture.Frame), SnapDistance / size.x, SnapDistance / size.y);
            FrameLayout s = _gesture.Start;
            FrameEditResult result = _gesture.Kind == GestureKind.Move
                ? FrameEditMath.MoveWithSnap(s.Placement, dx, dy, s.AllowOutside, snap)
                : FrameEditMath.Resize(s.Placement, _gesture.Handle, dx, dy, new ResizeOptions(keepAspect: !evt.shiftKey, fromCenter: evt.altKey), OutputSize, s.AllowOutside, snap);

            FrameLayout next = s.WithPlacement(result.Rect);
            _gesture.Last = next;
            _gesture.Version = _gesture.Frame.ShowPending(next);
            _gesture.Frame.Source.PreviewLayout(next);
            SetGuides(result.GuideX, result.GuideY);
            ShowReadout(next.Placement);
        }

        private IEnumerable<NormalizedRect> OtherPlacements(MediaFrame except)
        {
            foreach (MediaFrame frame in _frames)
            {
                if (frame != except)
                {
                    yield return frame.Shown.Placement;
                }
            }
        }

        private void EndGesture(int pointerId, bool commit)
        {
            Gesture gesture = _gesture;
            if (gesture == null)
            {
                return;
            }

            _gesture = null;
            if (pointerId >= 0 && this.HasPointerCapture(pointerId))
            {
                this.ReleasePointer(pointerId);
            }

            SetGuides(null, null);
            HideReadoutLater();
            if (gesture.Last == null)
            {
                // A click without movement changes nothing.
                return;
            }

            if (commit)
            {
                Commit(gesture.Frame, gesture.Last.Value);
            }
            else
            {
                gesture.Frame.Source.PreviewLayout(gesture.Start);
                gesture.Frame.EndPending(gesture.Version);
            }
        }

        // ------------------------------------------------------------------ Keyboard

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                if (_gesture != null)
                {
                    EndGesture(-1, commit: false);
                }
                else
                {
                    Select(null);
                }

                evt.StopPropagation();
                return;
            }

            (int x, int y) = NudgeFor(evt.keyCode, evt.shiftKey);
            if ((x == 0 && y == 0) || _selected == null || _gesture != null)
            {
                return;
            }

            evt.StopPropagation();
            if (_selected.Shown.Locked)
            {
                return;
            }

            // Key repeats preview each step; the edit is committed once, when the key is released.
            FrameLayout shown = _selected.Shown;
            FrameLayout next = shown.WithPlacement(FrameEditMath.Nudge(shown.Placement, x, y, OutputSize, shown.AllowOutside));
            _nudged = _selected;
            _nudgeVersion = _selected.ShowPending(next);
            _selected.Source.PreviewLayout(next);
            ShowReadout(next.Placement);
            HideReadoutLater();
        }

        private void OnKeyUp(KeyUpEvent evt)
        {
            if (NudgeFor(evt.keyCode, false) != (0, 0))
            {
                CommitNudge();
            }
        }

        /// <summary>Arrow keys move one output pixel, ten with Shift (UI foundation detailed design 9.3).</summary>
        internal static (int X, int Y) NudgeFor(KeyCode key, bool shift)
        {
            int step = shift ? 10 : 1;
            return key switch
            {
                KeyCode.LeftArrow => (-step, 0),
                KeyCode.RightArrow => (step, 0),
                KeyCode.UpArrow => (0, -step),
                KeyCode.DownArrow => (0, step),
                _ => (0, 0),
            };
        }

        private void CommitNudge()
        {
            MediaFrame frame = _nudged;
            if (frame == null)
            {
                return;
            }

            _nudged = null;
            Commit(frame, frame.Shown);
        }

        private void OnNavigationMove(NavigationMoveEvent evt)
        {
            bool tab = evt.direction == NavigationMoveEvent.Direction.Next || evt.direction == NavigationMoveEvent.Direction.Previous;
            if (!tab)
            {
                // Arrow keys nudge the selected frame (OnKeyDown) instead of moving focus.
                if (_selected != null)
                {
                    evt.StopPropagation();
                    focusController?.IgnoreEvent(evt);
                }

                return;
            }

            int[] order = FrameCanvasMath.FrontToBack(ShownLayouts());
            int current = _selected == null ? -1 : Array.IndexOf(order, _frames.IndexOf(_selected));
            int next = FrameCanvasMath.NextSelection(current, order.Length, evt.direction == NavigationMoveEvent.Direction.Previous);
            if (next < 0)
            {
                // Past the last frame: let focus leave the canvas.
                Select(null);
                return;
            }

            Select(_frames[order[next]].Source);
            evt.StopPropagation();
            focusController?.IgnoreEvent(evt);
        }

        private void OnFocusIn(FocusInEvent evt)
        {
            // Arriving from the keyboard selects the front frame, so the next keys act on something.
            if (_selected == null && _frames.Count > 0 && FocusRing.IsFromKeyboard(evt.direction))
            {
                Select(_frames[FrameCanvasMath.FrontToBack(ShownLayouts())[0]].Source);
            }
        }

        // ------------------------------------------------------------------ Commit and display

        private void Commit(MediaFrame frame, FrameLayout layout)
        {
            int version = frame.ShowPending(layout);
            _ = CommitAsync(frame, layout, version);
        }

        private async Task CommitAsync(MediaFrame frame, FrameLayout layout, int version)
        {
            // Failures are reported by CommandRunner; either way the frame then shows what Stage holds.
            await CommandRunner.RunAsync(() => frame.Source.CommitLayoutAsync(layout, CancellationToken.None), UiServices.Find(this));
            frame.EndPending(version);
        }

        private void SetGuides(double? x, double? y)
        {
            _guideX.style.display = x.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            _guideY.style.display = y.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (x.HasValue)
            {
                _guideX.style.left = Length.Percent((float)(x.Value * 100));
            }

            if (y.HasValue)
            {
                _guideY.style.top = Length.Percent((float)(y.Value * 100));
            }
        }

        private void ShowReadout(NormalizedRect placement)
        {
            _hideReadout?.Pause();
            bool below = FrameCanvasMath.ReadoutBelow(placement);
            _readout.text = FrameCanvasMath.Readout(placement, OutputSize);
            _readout.style.left = Length.Percent((float)(placement.CenterX * 100));
            _readout.style.top = Length.Percent((float)((below ? placement.Bottom : placement.Top) * 100));
            _readout.style.translate = new Translate(Length.Percent(-50), Length.Percent(below ? 0 : -100));
            _readout.EnableInClassList(ReadoutClass + "--below", below);
            _readout.style.display = DisplayStyle.Flex;
            _readout.BringToFront();
        }

        private void HideReadoutLater()
        {
            _hideReadout?.Pause();
            _hideReadout = schedule.Execute(() => _readout.style.display = DisplayStyle.None).StartingIn(ReadoutLinger);
        }

        private enum GestureKind
        {
            Move,
            Resize,
        }

        private sealed class Gesture
        {
            public Gesture(GestureKind kind, MediaFrame frame, FrameHandle handle, Vector2 startPointer, FrameLayout start)
            {
                Kind = kind;
                Frame = frame;
                Handle = handle;
                StartPointer = startPointer;
                Start = start;
            }

            public GestureKind Kind { get; }

            public MediaFrame Frame { get; }

            public FrameHandle Handle { get; }

            public Vector2 StartPointer { get; }

            public FrameLayout Start { get; }

            /// <summary>The latest layout of the drag; null until the pointer moves.</summary>
            public FrameLayout? Last { get; set; }

            public int Version { get; set; }
        }
    }
}
