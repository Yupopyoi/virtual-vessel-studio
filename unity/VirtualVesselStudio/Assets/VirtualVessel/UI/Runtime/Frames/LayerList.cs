using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.Core.Geometry;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Tooltips;

namespace VirtualVessel.UI.Frames
{
    /// <summary>
    /// The videos on the stream screen from front (top) to back (components.md 3.20, UI foundation
    /// detailed design 9.7). It shares the selection and the undo history with its
    /// <see cref="FrameCanvas"/>: clicking a row selects that video, and dragging a row, or Ctrl+Up and
    /// Ctrl+Down, changes the stacking order as one undoable step.
    /// </summary>
    public sealed class LayerList : VisualElement
    {
        public const string UssClass = "vv-layer-list";

        public const string RowClass = UssClass + "__row";

        public const string SelectedClass = RowClass + "--selected";

        public const string DraggingClass = RowClass + "--dragging";

        public const string DropClass = UssClass + "__drop";

        /// <summary>A press turns into a drag after moving this many pixels, so a click stays a click.</summary>
        private const float DragThreshold = 4f;

        private readonly FrameCanvas _canvas;
        private readonly List<Row> _rows = new List<Row>();
        private readonly VisualElement _drop;
        private readonly List<MediaFrame> _watched = new List<MediaFrame>();
        private Drag _drag;

        public LayerList(FrameCanvas canvas)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            AddToClassList(UssClass);
            focusable = true;
            tabIndex = 0;
            FocusRing.Attach(this);
            // The landing line is drawn over the rows, not inserted between them: inserting it would push
            // the rows below it down, move the row centers, and make the landing row flicker back and forth.
            _drop = new VisualElement { pickingMode = PickingMode.Ignore };
            _drop.AddToClassList(DropClass);
            _drop.style.position = Position.Absolute;
            _drop.style.display = DisplayStyle.None;

            Label hint = QuietText.Of(new Label { pickingMode = PickingMode.Ignore });
            hint.AddToClassList(UssClass + "__hint");
            hint.AddToClassList("vv-text-caption");
            new KeyedText(hint, text => hint.text = text) { Key = FrameStrings.LayersHint };
            Add(hint);

            canvas.FramesChanged += Rebuild;
            canvas.SelectionChanged += _ => Refresh();
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<NavigationMoveEvent>(OnNavigationMove);
            Rebuild();
        }

        /// <summary>The rows from front (top) to back.</summary>
        public IReadOnlyList<VisualElement> Rows => _rows;

        private void Rebuild()
        {
            foreach (MediaFrame frame in _watched)
            {
                frame.ShownChanged -= Refresh;
                frame.VisibleChanged -= Refresh;
            }

            _watched.Clear();
            _watched.AddRange(_canvas.Frames);
            foreach (MediaFrame frame in _watched)
            {
                frame.ShownChanged += Refresh;
                frame.VisibleChanged += Refresh;
            }

            foreach (Row row in _rows)
            {
                row.RemoveFromHierarchy();
            }

            _rows.Clear();
            foreach (MediaFrame frame in _watched)
            {
                var row = new Row(frame);
                row.RegisterCallback<PointerDownEvent>(evt => OnPointerDown(row, evt));
                row.RegisterCallback<PointerMoveEvent>(OnPointerMove);
                row.RegisterCallback<PointerUpEvent>(evt => OnPointerUp(row, evt));
                _rows.Add(row);
            }

            Refresh();
        }

        /// <summary>Puts the rows in stacking order and updates their selection and state.</summary>
        private void Refresh()
        {
            var layouts = new List<FrameLayout>(_rows.Count);
            foreach (Row row in _rows)
            {
                layouts.Add(row.Frame.Shown);
            }

            int[] frontToBack = FrameCanvasMath.FrontToBack(layouts);
            var ordered = new List<Row>(_rows.Count);
            foreach (int index in frontToBack)
            {
                ordered.Add(_rows[index]);
            }

            _rows.Clear();
            _rows.AddRange(ordered);
            foreach (Row row in _rows)
            {
                Add(row);
                row.Refresh(row.Frame.Source == _canvas.Selected);
            }

            Add(_drop);
        }

        private void OnPointerDown(Row row, PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            Focus();
            _drag = new Drag(row, evt.position.y);
            row.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_drag == null)
            {
                return;
            }

            if (!_drag.Moving)
            {
                // A locked video keeps its place, as the menu's order commands do.
                if (Mathf.Abs(evt.position.y - _drag.StartY) < DragThreshold || _drag.Row.Frame.Shown.Locked)
                {
                    return;
                }

                _drag.Moving = true;
                _drag.Row.AddToClassList(DraggingClass);

                // The rows do not move during a drag, so their places are measured once.
                foreach (Row row in _rows)
                {
                    _drag.Centers.Add(row.worldBound.center.y);
                    _drag.Edges.Add(row.layout.yMin);
                }

                _drag.Edges.Add(_rows[_rows.Count - 1].layout.yMax);
            }

            _drag.DropIndex = LayerListMath.DropIndex(_drag.Centers, evt.position.y);
            _drop.style.top = _drag.Edges[_drag.DropIndex] - 1;
            _drop.style.display = DisplayStyle.Flex;
        }

        private void OnPointerUp(Row row, PointerUpEvent evt)
        {
            Drag drag = _drag;
            _drag = null;
            if (drag == null)
            {
                return;
            }

            if (row.HasPointerCapture(evt.pointerId))
            {
                row.ReleasePointer(evt.pointerId);
            }

            _drop.style.display = DisplayStyle.None;
            row.RemoveFromClassList(DraggingClass);
            if (drag.Moving)
            {
                int from = _rows.IndexOf(drag.Row);
                _canvas.MoveInStack(drag.Row.Frame.Source, LayerListMath.TargetPosition(from, drag.DropIndex));
            }
            else
            {
                _canvas.Select(drag.Row.Frame.Source);
            }
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            int step = evt.keyCode == KeyCode.UpArrow ? -1 : evt.keyCode == KeyCode.DownArrow ? 1 : 0;
            if (step == 0)
            {
                return;
            }

            evt.StopPropagation();
            if (evt.ctrlKey || evt.commandKey)
            {
                // The same as ] and [ in the preview: up is toward the front.
                _canvas.Run(step < 0 ? FrameAction.BringForward : FrameAction.SendBackward);
                return;
            }

            int current = _rows.FindIndex(r => r.Frame.Source == _canvas.Selected);
            int next = LayerListMath.NextVisible(_rows.ConvertAll(r => r.Frame.IsVisible), current, step);
            if (next >= 0)
            {
                _canvas.Select(_rows[next].Frame.Source);
            }
        }

        private void OnNavigationMove(NavigationMoveEvent evt)
        {
            // Up and down move the selection in the list (OnKeyDown) instead of moving focus.
            if (evt.direction == NavigationMoveEvent.Direction.Up || evt.direction == NavigationMoveEvent.Direction.Down)
            {
                evt.StopPropagation();
                focusController?.IgnoreEvent(evt);
            }
        }

        private sealed class Drag
        {
            public Drag(Row row, float startY)
            {
                Row = row;
                StartY = startY;
            }

            public Row Row { get; }

            public float StartY { get; }

            public bool Moving { get; set; }

            public int DropIndex { get; set; }

            /// <summary>Row centers in panel space, for choosing the landing row.</summary>
            public List<float> Centers { get; } = new List<float>();

            /// <summary>Row tops in the list, then the bottom of the last row, for drawing the line.</summary>
            public List<float> Edges { get; } = new List<float>();
        }

        /// <summary>One video: a grip, a small picture of it, its name, and its state.</summary>
        private sealed class Row : VisualElement, ITooltipSource
        {
            private readonly Image _thumbnail;
            private readonly KeyedText _state;
            private readonly Label _stateLabel;

            public Row(MediaFrame frame)
            {
                Frame = frame;
                AddToClassList(RowClass);
                var grip = new Icon(IconName.Grip) { pickingMode = PickingMode.Ignore };
                grip.AddToClassList(UssClass + "__grip");
                Add(grip);

                _thumbnail = new Image { scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
                _thumbnail.AddToClassList(UssClass + "__thumbnail");
                Add(_thumbnail);

                Label name = QuietText.Of(new Label { pickingMode = PickingMode.Ignore });
                name.AddToClassList(UssClass + "__name");
                Add(name);
                if (frame.Source.NameKey != null)
                {
                    new KeyedText(name, text => name.text = text) { Key = frame.Source.NameKey };
                }
                else
                {
                    this.Bind(frame.Source.DisplayName, text => name.text = text);
                }

                _stateLabel = QuietText.Of(new Label { pickingMode = PickingMode.Ignore });
                _stateLabel.AddToClassList(UssClass + "__state");
                _stateLabel.AddToClassList("vv-text-caption");
                Add(_stateLabel);
                _state = new KeyedText(_stateLabel, text =>
                {
                    _stateLabel.text = text;
                    _stateLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
                });
            }

            public MediaFrame Frame { get; }

            string ITooltipSource.CurrentTooltipKey => FrameStrings.LayerTip;

            string ITooltipSource.Shortcut => null;

            public void Refresh(bool selected)
            {
                EnableInClassList(SelectedClass, selected);
                Texture video = Frame.Source.Video;
                _thumbnail.image = video;
                _thumbnail.EnableInClassList(UssClass + "__thumbnail--empty", video == null);
                _state.Key = LayerListMath.StateKey(Frame.IsVisible, Frame.Shown.Locked);
                EnableInClassList(RowClass + "--hidden", !Frame.IsVisible);
            }
        }
    }

    /// <summary>Decisions of the layer list. Pure.</summary>
    public static class LayerListMath
    {
        /// <summary>Where a dragged row would land: before the first row whose center is below the pointer.</summary>
        public static int DropIndex(IReadOnlyList<float> rowCenters, float y)
        {
            for (int i = 0; i < rowCenters.Count; i++)
            {
                if (y < rowCenters[i])
                {
                    return i;
                }
            }

            return rowCenters.Count;
        }

        /// <summary>
        /// The front-to-back position for a row moved from <paramref name="from"/> to a drop index: taking
        /// the row out first shifts the rows below it up by one.
        /// </summary>
        public static int TargetPosition(int from, int dropIndex)
        {
            return dropIndex > from ? dropIndex - 1 : dropIndex;
        }

        /// <summary>The next visible row from <paramref name="current"/> by <paramref name="step"/>, or -1; from none, the first or last visible row.</summary>
        public static int NextVisible(IReadOnlyList<bool> visible, int current, int step)
        {
            int i = current < 0 ? (step > 0 ? -1 : visible.Count) : current;
            for (i += step; i >= 0 && i < visible.Count; i += step)
            {
                if (visible[i])
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>The state text of a row: hidden first, since a hidden video is not on the stream at all.</summary>
        public static string StateKey(bool visible, bool locked)
        {
            return !visible ? FrameStrings.Hidden : locked ? FrameStrings.Locked : null;
        }
    }
}
