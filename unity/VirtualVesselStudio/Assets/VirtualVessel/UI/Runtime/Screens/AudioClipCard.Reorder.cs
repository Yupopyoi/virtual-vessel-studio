using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Frames;

namespace VirtualVessel.UI.Screens
{
    /// <summary>Reordering the rows: dragging the grip, or Ctrl+Up and Ctrl+Down (setup.md 14).</summary>
    public sealed partial class AudioClipCard
    {
        internal void StartDrag(ClipRow row, PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            _drag = new Drag(row, evt.position.y);
            row.Grip.CapturePointer(evt.pointerId);
        }

        internal void DragTo(PointerMoveEvent evt)
        {
            if (_drag == null)
            {
                return;
            }

            if (!_drag.Moving)
            {
                if (Mathf.Abs(evt.position.y - _drag.StartY) < DragThreshold)
                {
                    return;
                }

                _drag.Moving = true;
                _drag.Row.Root.AddToClassList(DraggingClass);

                // The rows do not move during a drag, so their places are measured once.
                foreach (ClipRow row in _shown)
                {
                    _drag.Centers.Add(row.Root.worldBound.center.y);
                    _drag.Edges.Add(row.Root.layout.yMin);
                }

                _drag.Edges.Add(_shown[_shown.Count - 1].Root.layout.yMax);
            }

            _drag.DropIndex = LayerListMath.DropIndex(_drag.Centers, evt.position.y);
            _drop.style.top = _drag.Edges[_drag.DropIndex] - 1;
            _drop.style.display = DisplayStyle.Flex;
        }

        internal void Drop(PointerUpEvent evt)
        {
            Drag drag = _drag;
            if (drag == null)
            {
                return;
            }

            if (drag.Row.Grip.HasPointerCapture(evt.pointerId))
            {
                drag.Row.Grip.ReleasePointer(evt.pointerId);
            }

            EndDrag();
            int from = _shown.IndexOf(drag.Row);
            int to = LayerListMath.TargetPosition(from, drag.DropIndex);
            if (drag.Moving && to != from)
            {
                Move(drag.Row.Record, to);
            }
        }

        /// <summary>
        /// Ctrl+Up and Ctrl+Down with the focus in a row move it by one, as in the layer list. The grip is only for the
        /// pointer, so the keys work from the row's buttons; the rename field keeps them for its text.
        /// </summary>
        internal void Step(ClipRow row, KeyDownEvent evt)
        {
            int step = evt.keyCode == KeyCode.UpArrow ? -1 : evt.keyCode == KeyCode.DownArrow ? 1 : 0;
            if (step == 0 || !(evt.ctrlKey || evt.commandKey) || (evt.target is VisualElement target && row.NameInput.Contains(target)))
            {
                return;
            }

            // Kept from the focus navigation too, which would otherwise move on from the old row after it is rebuilt.
            evt.StopPropagation();
            focusController?.IgnoreEvent(evt);
            int from = _shown.IndexOf(row);
            int to = from + step;
            if (from >= 0 && to >= 0 && to < _shown.Count)
            {
                // Contains does not count the element itself.
                var focused = evt.target as VisualElement;
                Move(row.Record, to, row.Controls.FindIndex(control => control == focused || control.Contains(focused)));
            }
        }

        private void EndDrag()
        {
            _drag?.Row.Root.RemoveFromClassList(DraggingClass);
            _drag = null;
            _drop.style.display = DisplayStyle.None;
        }

        private sealed class Drag
        {
            public Drag(ClipRow row, float startY)
            {
                Row = row;
                StartY = startY;
            }

            public ClipRow Row { get; }

            public float StartY { get; }

            public bool Moving { get; set; }

            public int DropIndex { get; set; }

            /// <summary>Row centers in panel space, for choosing the landing row.</summary>
            public List<float> Centers { get; } = new List<float>();

            /// <summary>Row tops in the list, then the bottom of the last row, for drawing the line.</summary>
            public List<float> Edges { get; } = new List<float>();
        }
    }
}
