using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.Core.Geometry;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.Frames
{
    /// <summary>Keyboard input: Escape, Space for panning, arrow-key nudges, and Tab between frames (UI foundation detailed design 9.3).</summary>
    public partial class FrameCanvas
    {
        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                // Esc steps back one layer at a time: the drag, then crop mode, then the selection. With
                // nothing left it passes on, so the preview around the canvas can leave full screen.
                if (_gesture != null)
                {
                    EndGesture(-1, commit: false);
                }
                else if (_cropMode)
                {
                    SetCropMode(false);
                }
                else if (_selected != null)
                {
                    Select(null);
                }
                else
                {
                    return;
                }

                evt.StopPropagation();
                return;
            }

            if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && _cropMode)
            {
                SetCropMode(false);
                evt.StopPropagation();
                return;
            }

            if (evt.keyCode == KeyCode.Space)
            {
                _spaceDown = true;
                return;
            }

            bool ctrl = evt.ctrlKey || evt.commandKey;
            if (ctrl && (evt.keyCode == KeyCode.Z || evt.keyCode == KeyCode.Y))
            {
                if (evt.keyCode == KeyCode.Y || evt.shiftKey)
                {
                    Redo();
                }
                else
                {
                    Undo();
                }

                evt.StopPropagation();
                return;
            }

            FrameAction action = FrameCanvasMath.ActionFor(evt.keyCode, evt.character, evt.shiftKey, ctrl);
            if (action != FrameAction.None)
            {
                if (_selected != null)
                {
                    Run(action);
                    evt.StopPropagation();
                }

                return;
            }

            // Arrow keys on the mode switch change the mode instead.
            (int x, int y) = NudgeFor(evt.keyCode, evt.shiftKey);
            if ((x == 0 && y == 0) || _selected == null || _gesture != null || evt.target != this)
            {
                return;
            }

            evt.StopPropagation();
            if (_selected.Shown.Locked)
            {
                return;
            }

            // Key repeats preview each step; the edit is committed once, when the key is released.
            if (_nudge != null && _nudge.Frame != _selected)
            {
                CommitNudge();
            }

            _nudge ??= new Burst(_selected, _selected.Shown);
            FrameLayout shown = _selected.Shown;
            FrameLayout next = shown.WithPlacement(FrameEditMath.Nudge(shown.Placement, x, y, OutputSize, shown.AllowOutside));
            _selected.ShowPending(next);
            _selected.Source.PreviewLayout(next);
            ShowReadout(next, crop: false);
            HideReadoutLater();
        }

        private void OnKeyUp(KeyUpEvent evt)
        {
            if (evt.keyCode == KeyCode.Space)
            {
                _spaceDown = false;
            }
            else if (NudgeFor(evt.keyCode, false) != (0, 0))
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

        private void OnNavigationMove(NavigationMoveEvent evt)
        {
            bool tab = evt.direction == NavigationMoveEvent.Direction.Next || evt.direction == NavigationMoveEvent.Direction.Previous;
            if (!tab)
            {
                // Arrow keys nudge the selected frame (OnKeyDown) instead of moving focus.
                if (_selected != null && evt.target == this)
                {
                    evt.StopPropagation();
                    focusController?.IgnoreEvent(evt);
                }

                return;
            }

            List<MediaFrame> visible = VisibleFrames();
            int[] order = FrameCanvasMath.FrontToBack(visible.ConvertAll(f => f.Shown));
            int current = _selected == null ? -1 : Array.IndexOf(order, visible.IndexOf(_selected));
            int next = FrameCanvasMath.NextSelection(current, order.Length, evt.direction == NavigationMoveEvent.Direction.Previous);
            if (next < 0)
            {
                // Past the last frame: let focus leave the canvas.
                Select(null);
                return;
            }

            Select(visible[order[next]].Source);
            evt.StopPropagation();
            focusController?.IgnoreEvent(evt);
        }

        private void OnFocusIn(FocusInEvent evt)
        {
            // Arriving from the keyboard selects the front frame, so the next keys act on something.
            List<MediaFrame> visible = VisibleFrames();
            if (evt.target == this && _selected == null && visible.Count > 0 && FocusRing.IsFromKeyboard(evt.direction))
            {
                Select(visible[FrameCanvasMath.FrontToBack(visible.ConvertAll(f => f.Shown))[0]].Source);
            }
        }
    }
}
