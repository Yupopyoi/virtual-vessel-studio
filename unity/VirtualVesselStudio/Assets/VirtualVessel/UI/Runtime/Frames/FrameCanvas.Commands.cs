using System;
using System.Collections.Generic;
using UnityEngine;
using VirtualVessel.Core.Geometry;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.Frames
{
    /// <summary>The frame menu, its shortcuts, and undo/redo (UI foundation detailed design 9.3 and 9.4).</summary>
    public partial class FrameCanvas
    {
        /// <summary>Runs a menu command on the selected frame, as its shortcut does.</summary>
        public void Run(FrameAction action)
        {
            MediaFrame frame = _selected;
            if (frame == null || action == FrameAction.None)
            {
                return;
            }

            if (frame.Shown.Locked && !FrameCanvasMath.AllowedWhenLocked(action))
            {
                return;
            }

            if (action == FrameAction.ToggleCropMode)
            {
                SetCropMode(!_cropMode);
                return;
            }

            CommitBursts();
            StackMove? move = StackMoveFor(action);
            if (move.HasValue)
            {
                Restack(frame, move.Value);
                return;
            }

            FrameLayout before = frame.Shown;
            FrameLayout after = Apply(action, before, frame.Source.SourceAspect, OutputSize);
            if (after == before)
            {
                return;
            }

            Commit(frame, before, after, record: true);
            if (after.Locked)
            {
                SetCropMode(false);
            }

            RefreshSelectionParts();
        }

        public void Undo()
        {
            CommitBursts();
            IReadOnlyList<FrameEdit> step = _history.Undo();
            if (step != null)
            {
                Restore(step, after: false);
            }
        }

        public void Redo()
        {
            CommitBursts();
            IReadOnlyList<FrameEdit> step = _history.Redo();
            if (step != null)
            {
                Restore(step, after: true);
            }
        }

        /// <summary>Opens the frame menu for the selected frame at a panel position.</summary>
        public void ShowMenu(Vector2 panelPosition)
        {
            if (_selected == null)
            {
                return;
            }

            _menu?.Close();
            int[] frontToBack = FrameCanvasMath.FrontToBack(ShownLayouts());
            int position = Array.IndexOf(frontToBack, _frames.IndexOf(_selected));
            _menu = PopupMenu.Show(this, panelPosition, MenuEntries(_selected.Shown, _cropMode, position, frontToBack.Length), () => Focus());
        }

        /// <summary>The layout a command that changes one frame produces. Pure.</summary>
        internal static FrameLayout Apply(FrameAction action, FrameLayout layout, double sourceAspect, CanvasSize canvas)
        {
            return action switch
            {
                FrameAction.ToggleFlip => FrameCommands.ToggleFlip(layout),
                FrameAction.ToggleLock => FrameCommands.ToggleLock(layout),
                FrameAction.ToggleAllowOutside => FrameCommands.ToggleAllowOutside(layout),
                FrameAction.ResetCrop => FrameCommands.ResetCrop(layout),
                FrameAction.Fit => FrameCommands.Fit(layout, sourceAspect, canvas),
                FrameAction.Fill => FrameCommands.Fill(layout, sourceAspect, canvas),
                FrameAction.Center => FrameCommands.Center(layout),
                _ => layout,
            };
        }

        /// <summary>The stacking move of an order command, or null for other commands. Pure.</summary>
        internal static StackMove? StackMoveFor(FrameAction action)
        {
            return action switch
            {
                FrameAction.BringForward => StackMove.Forward,
                FrameAction.SendBackward => StackMove.Backward,
                FrameAction.BringToFront => StackMove.ToFront,
                FrameAction.SendToBack => StackMove.ToBack,
                _ => null,
            };
        }

        /// <summary>
        /// Moves a video to <paramref name="position"/> in the front-to-back order (0 is the front), as
        /// one undoable step, as dragging a row of the layer list does (UI foundation detailed design 9.7).
        /// </summary>
        public void MoveInStack(IFrameSource source, int position)
        {
            MediaFrame frame = _frames.Find(f => f.Source == source);
            if (frame == null || frame.Shown.Locked)
            {
                return;
            }

            CommitBursts();
            List<FrameLayout> layouts = ShownLayouts();
            ApplyOrders(frame, layouts, FrameCommands.MoveTo(layouts, _frames.IndexOf(frame), position));
        }

        /// <summary>Changes the stacking order as one undoable step, which may renumber several frames.</summary>
        private void Restack(MediaFrame frame, StackMove move)
        {
            List<FrameLayout> layouts = ShownLayouts();
            ApplyOrders(frame, layouts, FrameCommands.Restack(layouts, _frames.IndexOf(frame), move));
        }

        private void ApplyOrders(MediaFrame frame, List<FrameLayout> layouts, int[] orders)
        {
            // The moved frame first, so that undo selects it again.
            var edits = new List<FrameEdit>();
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < _frames.Count; i++)
                {
                    bool moved = _frames[i] == frame;
                    if (moved == (pass == 0) && orders[i] != layouts[i].Order)
                    {
                        edits.Add(new FrameEdit(_frames[i].Source, layouts[i], layouts[i].WithOrder(orders[i])));
                    }
                }
            }

            _history.Record(edits);
            foreach (FrameEdit edit in edits)
            {
                Commit(_frames.Find(f => f.Source == edit.Source), edit.Before, edit.After, record: false);
            }
        }

        private void Restore(IReadOnlyList<FrameEdit> step, bool after)
        {
            bool selected = false;
            foreach (FrameEdit edit in step)
            {
                MediaFrame frame = _frames.Find(f => f.Source == edit.Source);
                if (frame == null)
                {
                    // The video was removed meanwhile; nothing to restore.
                    continue;
                }

                if (!selected)
                {
                    Select(edit.Source);
                    selected = true;
                }

                Commit(frame, frame.Shown, after ? edit.After : edit.Before, record: false);
            }

            RefreshSelectionParts();
        }

        private IReadOnlyList<MenuEntry> MenuEntries(FrameLayout layout, bool cropMode, int position, int count)
        {
            bool editable = !layout.Locked;
            MenuEntry Entry(FrameAction action, string textKey, string tooltipKey, string shortcut, bool? isChecked = null, bool possible = true)
            {
                return new MenuEntry(textKey, () => Run(action), shortcut, tooltipKey, isChecked, possible && (editable || FrameCanvasMath.AllowedWhenLocked(action)));
            }

            MenuEntry Stack(FrameAction action, StackMove move, string textKey, string tooltipKey, string shortcut)
            {
                return Entry(action, textKey, tooltipKey, shortcut, possible: FrameCommands.CanRestack(position, count, move));
            }

            return new[]
            {
                Entry(FrameAction.ToggleFlip, FrameStrings.Flip, FrameStrings.FlipTip, "H", layout.FlipHorizontal),
                Entry(FrameAction.ToggleLock, FrameStrings.Lock, FrameStrings.LockTip, "L", layout.Locked),
                Entry(FrameAction.ToggleAllowOutside, FrameStrings.AllowOutside, FrameStrings.AllowOutsideTip, "O", layout.AllowOutside),
                MenuEntry.Separator,
                Entry(FrameAction.ToggleCropMode, FrameStrings.CropMode, FrameStrings.CropModeTip, "C", cropMode),
                Entry(FrameAction.ResetCrop, FrameStrings.ResetCrop, FrameStrings.ResetCropTip, "R"),
                MenuEntry.Separator,
                Entry(FrameAction.Fit, FrameStrings.Fit, FrameStrings.FitTip, "Shift+1"),
                Entry(FrameAction.Fill, FrameStrings.Fill, FrameStrings.FillTip, "Shift+2"),
                Entry(FrameAction.Center, FrameStrings.Center, FrameStrings.CenterTip, "Shift+C"),
                MenuEntry.Separator,
                Stack(FrameAction.BringForward, StackMove.Forward, FrameStrings.BringForward, FrameStrings.BringForwardTip, "]"),
                Stack(FrameAction.SendBackward, StackMove.Backward, FrameStrings.SendBackward, FrameStrings.SendBackwardTip, "["),
                Stack(FrameAction.BringToFront, StackMove.ToFront, FrameStrings.BringToFront, FrameStrings.BringToFrontTip, "Shift+]"),
                Stack(FrameAction.SendToBack, StackMove.ToBack, FrameStrings.SendToBack, FrameStrings.SendToBackTip, "Shift+["),
            };
        }
    }
}
