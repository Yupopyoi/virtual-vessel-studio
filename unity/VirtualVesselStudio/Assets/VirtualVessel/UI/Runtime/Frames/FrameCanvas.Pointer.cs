using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.Core.Geometry;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.Frames
{
    /// <summary>Hit tests and pointer gestures: select, move, resize, crop, pan, and the wheel (UI foundation detailed design 9.3).</summary>
    public partial class FrameCanvas
    {
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
            List<MediaFrame> visible = VisibleFrames();
            int index = FrameCanvasMath.FrameAt(visible.ConvertAll(f => f.Shown), p.x / size.x, p.y / size.y);
            return index < 0 ? null : visible[index];
        }

        /// <summary>
        /// The frame a press acts on. In crop mode the frame being cropped wins over frames in front of
        /// it; <paramref name="onVideo"/> tells a press on its faint whole video, outside the window.
        /// </summary>
        private MediaFrame PressTarget(Vector2 panelPosition, out bool onVideo)
        {
            onVideo = false;
            if (_cropMode && _selected != null)
            {
                Vector2 p = StreamPixels(panelPosition);
                Vector2 size = StreamSize();
                CropPress press = FrameCanvasMath.CropPressAt(_selected.Shown, p.x / size.x, p.y / size.y);
                if (press != CropPress.Outside)
                {
                    onVideo = press == CropPress.Video;
                    return _selected;
                }
            }

            return FrameUnder(panelPosition);
        }

        // ------------------------------------------------------------------ Pointer

        private void OnPointerDown(PointerDownEvent evt)
        {
            // The mode switch handles its own clicks; a press there also ends any double-click.
            if (evt.target != this)
            {
                _lastPressFrame = null;
                return;
            }

            Focus();
            CommitBursts();
            MediaFrame hit = PressTarget(evt.position, out bool onVideo);
            if (evt.button == 1)
            {
                if (hit != null)
                {
                    Select(hit.Source);
                    ShowMenu(evt.position);
                }

                evt.StopPropagation();
                return;
            }

            if (evt.button == 2)
            {
                // The middle button pans a zoomed video in either mode.
                if (hit != null)
                {
                    Select(hit.Source);
                    if (!hit.Shown.Locked)
                    {
                        Begin(evt, GestureKind.Pan, hit, FrameHandle.TopLeft);
                    }
                }

                return;
            }

            if (evt.button != 0)
            {
                _lastPressFrame = null;
                return;
            }

            // A double-click is two presses on the same frame, close in time and place. The platform's
            // click count is not used: it also counts a press elsewhere, such as on the mode switch,
            // and a click right after it would switch modes by surprise.
            bool doubleClick = hit != null && hit == _lastPressFrame
                && FrameCanvasMath.IsDoubleClick(_lastPressTime, _lastPressPosition, evt.timestamp, evt.position);
            _lastPressFrame = doubleClick ? null : hit;
            _lastPressTime = evt.timestamp;
            _lastPressPosition = evt.position;

            FrameHandle? handle = !doubleClick && !_spaceDown ? HandleUnder(evt.position) : null;
            if (handle.HasValue)
            {
                Begin(evt, _cropMode ? GestureKind.Crop : GestureKind.Resize, _selected, handle.Value);
                return;
            }

            if (onVideo)
            {
                // The faint whole video around the crop window: stay in crop mode, change nothing.
                return;
            }

            if (hit == null)
            {
                // Clicking outside clears the selection, which also ends crop mode: one click, the same
                // result in either mode. Enter and Esc end crop mode but keep the selection.
                Select(null);
                return;
            }

            Select(hit.Source);
            if (doubleClick)
            {
                // Double-click enters crop mode, and leaves it the same way.
                SetCropMode(!_cropMode);
                return;
            }

            if (!hit.Shown.Locked)
            {
                GestureKind kind = _spaceDown ? GestureKind.Pan : _cropMode ? GestureKind.CropWindow : GestureKind.Move;
                Begin(evt, kind, hit, FrameHandle.TopLeft);
            }
        }

        private void Begin(PointerDownEvent evt, GestureKind kind, MediaFrame frame, FrameHandle handle)
        {
            _gesture = new Gesture(kind, frame, handle, evt.position, frame.Shown);
            this.CapturePointer(evt.pointerId);
            ShowReadout(frame.Shown, IsCropGesture(kind));
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_gesture == null)
            {
                if (evt.target != this)
                {
                    CursorService.Reset();
                    return;
                }

                FrameHandle? handle = HandleUnder(evt.position);
                if (_selected != null)
                {
                    _selected.HoverHandle = handle;
                }

                MediaFrame hit = _selected;
                if (!handle.HasValue)
                {
                    // Over the faint whole video in crop mode there is nothing to grab.
                    hit = PressTarget(evt.position, out bool onVideo);
                    hit = onVideo ? null : hit;
                }

                CursorService.Set(FrameCanvasMath.CursorFor(handle, hit != null, hit != null && hit.Shown.Locked));
                return;
            }

            Vector2 size = StreamSize();
            Vector2 start = StreamPixels(_gesture.StartPointer);
            Vector2 now = StreamPixels(evt.position);
            float dxPixels = now.x - start.x;
            float dyPixels = now.y - start.y;
            double dx = dxPixels / size.x;
            double dy = dyPixels / size.y;
            FrameLayout s = _gesture.Start;
            FrameLayout next;
            double? guideX = null;
            double? guideY = null;
            switch (_gesture.Kind)
            {
                case GestureKind.Move:
                case GestureKind.Resize:
                    bool snapOff = evt.ctrlKey || evt.commandKey;
                    SnapLines snap = snapOff ? SnapLines.None : FrameEditMath.CreateSnapLines(OtherPlacements(_gesture.Frame), SnapDistance / size.x, SnapDistance / size.y);
                    FrameEditResult result = _gesture.Kind == GestureKind.Move
                        ? FrameEditMath.MoveWithSnap(s.Placement, dx, dy, s.AllowOutside, snap)
                        : FrameEditMath.Resize(s.Placement, _gesture.Handle, dx, dy, new ResizeOptions(keepAspect: !evt.shiftKey, fromCenter: evt.altKey), OutputSize, s.AllowOutside, snap);
                    next = s.WithPlacement(result.Rect);
                    guideX = result.GuideX;
                    guideY = result.GuideY;
                    break;
                case GestureKind.Crop:
                    next = FrameEditMath.CropEdge(s, _gesture.Handle, dx, dy);
                    break;
                case GestureKind.CropWindow:
                    next = FrameEditMath.MoveCropWindow(s, dx, dy);
                    break;
                default:
                    // Pan: the drag as a fraction of the frame, mirrored for a flipped frame.
                    double fx = dxPixels / Math.Max(1, s.Placement.Width * size.x);
                    double fy = dyPixels / Math.Max(1, s.Placement.Height * size.y);
                    next = s.WithCrop(FrameEditMath.PanCrop(s.Crop, s.FlipHorizontal ? -fx : fx, fy));
                    break;
            }

            _gesture.Last = next;
            _gesture.Version = _gesture.Frame.ShowPending(next);
            _gesture.Frame.Source.PreviewLayout(next);
            SetGuides(guideX, guideY);
            ShowReadout(next, IsCropGesture(_gesture.Kind));
        }

        private static bool IsCropGesture(GestureKind kind)
        {
            return kind == GestureKind.Crop || kind == GestureKind.CropWindow || kind == GestureKind.Pan;
        }

        private IEnumerable<NormalizedRect> OtherPlacements(MediaFrame except)
        {
            foreach (FrameLayout layout in OtherLayouts(except))
            {
                yield return layout.Placement;
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
                Commit(gesture.Frame, gesture.Start, gesture.Last.Value, record: true);
            }
            else
            {
                gesture.Frame.Source.PreviewLayout(gesture.Start);
                gesture.Frame.EndPending(gesture.Version);
            }
        }

        private void OnWheel(WheelEvent evt)
        {
            if (evt.target != this)
            {
                return;
            }

            MediaFrame hit = PressTarget(evt.mousePosition, out _);
            if (hit == null)
            {
                return;
            }

            evt.StopPropagation();
            Select(hit.Source);
            if (hit.Shown.Locked)
            {
                return;
            }

            // Wheel steps arrive one by one; a burst of them is previewed step by step and is one edit.
            if (_wheel != null && _wheel.Frame != hit)
            {
                CommitWheel();
            }

            _wheel ??= new Burst(hit, hit.Shown);
            FrameLayout shown = hit.Shown;
            Vector2 p = StreamPixels(evt.mousePosition);
            Vector2 size = StreamSize();
            (double px, double py) = FrameCanvasMath.CropPivot(shown.Placement, p.x / size.x, p.y / size.y, shown.FlipHorizontal);
            FrameLayout next = shown.WithCrop(FrameEditMath.ZoomCrop(shown.Crop, FrameCanvasMath.ZoomFactor(evt.delta.y), px, py));
            hit.ShowPending(next);
            hit.Source.PreviewLayout(next);
            ShowReadout(next, crop: true);
            HideReadoutLater();
            _wheel.Timer?.Pause();
            _wheel.Timer = schedule.Execute(CommitWheel).StartingIn(WheelBurst);
        }

        private enum GestureKind
        {
            Move,
            Resize,

            /// <summary>Crop mode: an edge or corner handle cuts the video.</summary>
            Crop,

            /// <summary>Crop mode: the window moves over the video, which stays still.</summary>
            CropWindow,

            /// <summary>Middle button or Space: the video moves under the frame, which stays still.</summary>
            Pan,
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
