using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.StageEditing
{
    /// <summary>
    /// Shows an <see cref="IStageView"/> (docs/&lt;lang&gt;/ui/screens/setup.md 15.2): its image, a numbered marker on
    /// each editable thing, and input turned into commands. Dragging a marker moves its thing; dragging elsewhere
    /// orbits the view, the stage turning with the pointer as in Unity's Scene View; a middle-button or Shift drag
    /// moves the pivot; the wheel zooms; a double click resets the view. The view is open only while this is shown
    /// and <see cref="Active"/>, so it costs nothing otherwise.
    /// </summary>
    public sealed class StageViewPanel : VisualElement
    {
        public const string UssClass = "vv-stage-view";
        public const string ImageClass = UssClass + "__image";
        public const string MarkerClass = UssClass + "__marker";
        public const string ChosenClass = MarkerClass + "--chosen";

        /// <summary>Degrees the view orbits per pixel of drag: across, and up and down.</summary>
        public const float OrbitPerPixelX = 0.4f;
        public const float OrbitPerPixelY = 0.3f;

        /// <summary>How near to a marker's center a press picks it, in pixels.</summary>
        public const float PickRadius = 16f;

        /// <summary>Degrees an arrow key moves the chosen thing or, with Shift, the view.</summary>
        public const float KeyStep = 5f;

        private const long CheckIntervalMs = 250;

        private readonly IStageView _view;
        private readonly Image _image;
        private readonly List<Label> _markerLabels = new List<Label>();
        private IReadOnlyList<StageViewMarker> _markers = Array.Empty<StageViewMarker>();
        private IDisposable _open;
        private Vector2 _openSize;
        private bool _active = true;
        private string _chosen;
        private Mode _mode;
        private int _pointerId = -1;
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        private IVisualElementScheduledItem _check;

        public StageViewPanel(IStageView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            AddToClassList(UssClass);
            focusable = true;
            tabIndex = 0;
            FocusRing.Attach(this);

            // Stretched: the render target is made in this element's shape, so a point here is the same point there.
            _image = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            _image.AddToClassList(ImageClass);
            Add(_image);

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => EndPointer());
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<KeyDownEvent>(OnKeyDown);

            // The arrow keys move the chosen thing here, so they do not also move the focus away; Tab still does.
            RegisterCallback<NavigationMoveEvent>(evt =>
            {
                if (evt.direction != NavigationMoveEvent.Direction.Next && evt.direction != NavigationMoveEvent.Direction.Previous)
                {
                    evt.StopPropagation();
                }
            });
            RegisterCallback<AttachToPanelEvent>(_ => Attach());
            RegisterCallback<DetachFromPanelEvent>(_ => Detach());
        }

        /// <summary>A marker was pressed: the owner chooses that thing.</summary>
        public event Action<string> ItemChosen;

        /// <summary>An arrow key with the view focused: move the chosen thing by these degrees (left-right, up-down).</summary>
        public event Action<Vector2> NudgeRequested;

        /// <summary>Whether the view should draw while shown, such as while its Tab is chosen.</summary>
        public bool Active
        {
            get => _active;
            set
            {
                _active = value;
                Check();
            }
        }

        /// <summary>Whether the view is open now; for tests.</summary>
        public bool IsOpen => _open != null;

        /// <summary>The markers' elements, in the order of the view's markers; for tests.</summary>
        public IReadOnlyList<Label> MarkerLabels => _markerLabels;

        /// <summary>The chosen thing, drawn bolder in the view and on its marker.</summary>
        public string Chosen
        {
            get => _chosen;
            set
            {
                _chosen = value;
                _view.Select(value);
                ShowMarkers();
            }
        }

        /// <summary>The marker nearest to <paramref name="point"/> within <see cref="PickRadius"/>, the nearest drawn on top winning; null for none. Pure.</summary>
        public static string Pick(IReadOnlyList<StageViewMarker> markers, Vector2 point, Vector2 size)
        {
            string picked = null;
            float best = PickRadius;
            for (int i = 0; i < markers.Count; i++)
            {
                StageViewMarker marker = markers[i];
                if (!marker.Visible)
                {
                    continue;
                }

                float distance = Vector2.Distance(new Vector2(marker.Uv.x * size.x, marker.Uv.y * size.y), point);
                if (distance <= best)
                {
                    best = distance;
                    picked = marker.ItemId;
                }
            }

            return picked;
        }

        /// <summary>
        /// The orbit for a drag of <paramref name="pixels"/> (right and down positive) (Stage detailed design 7.4.4). By
        /// default, left and right the stage turns with the pointer, so the viewpoint moves the other way; up and down the
        /// viewpoint follows the pointer: dragging down raises it (the user's choice, 2026-11-22). Each direction can be
        /// turned around, as people expect either. Pure.
        /// </summary>
        public static Vector2 OrbitFor(Vector2 pixels, bool invertX = false, bool invertY = false)
        {
            return new Vector2((invertX ? 1f : -1f) * pixels.x * OrbitPerPixelX, (invertY ? -1f : 1f) * pixels.y * OrbitPerPixelY);
        }

        /// <summary>Which way a drag turns the view; null keeps the default.</summary>
        public IStageViewPreferences Preferences { get; set; }

        private Vector2 Size => new Vector2(contentRect.width, contentRect.height);

        private Vector2 UvOf(Vector2 local) => new Vector2(Size.x > 0 ? local.x / Size.x : 0f, Size.y > 0 ? local.y / Size.y : 0f);

        private void Attach()
        {
            _subscriptions.Add(_view.Image.Subscribe(texture => _image.image = texture));
            _subscriptions.Add(_view.Markers.Subscribe(markers =>
            {
                _markers = markers ?? Array.Empty<StageViewMarker>();
                ShowMarkers();
            }));
            _image.image = _view.Image.Value;
            _markers = _view.Markers.Value ?? Array.Empty<StageViewMarker>();

            // A hidden page or screen leaves this attached with no size; checking now and then closes the view then.
            _check = schedule.Execute(Check).Every(CheckIntervalMs);
            Check();
        }

        private void Detach()
        {
            _check?.Pause();
            _check = null;
            foreach (IDisposable subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
            Close();
        }

        private void Check()
        {
            Vector2 size = Size;
            bool shown = panel != null && _active && size.x > 1f && size.y > 1f && resolvedStyle.display != DisplayStyle.None;
            if (!shown)
            {
                Close();
                return;
            }

            // A new shape needs a new render target, or the picture would stretch and points would not match.
            if (_open != null && Mathf.Abs(size.x / size.y - _openSize.x / _openSize.y) > 0.03f)
            {
                Close();
            }

            if (_open == null)
            {
                float scale = panel.scaledPixelsPerPoint;
                _open = _view.Open(Mathf.RoundToInt(size.x * scale), Mathf.RoundToInt(size.y * scale));
                _openSize = size;
                _view.Select(_chosen);
            }
        }

        private void Close()
        {
            EndPointer();
            _open?.Dispose();
            _open = null;
            _image.image = null;
        }

        private void ShowMarkers()
        {
            while (_markerLabels.Count < _markers.Count)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.AddToClassList(MarkerClass);
                label.style.position = Position.Absolute;
                _markerLabels.Add(label);
                Add(label);
            }

            Vector2 size = Size;
            for (int i = 0; i < _markerLabels.Count; i++)
            {
                Label label = _markerLabels[i];
                bool shown = i < _markers.Count && _markers[i].Visible && _open != null;
                label.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
                if (!shown)
                {
                    continue;
                }

                StageViewMarker marker = _markers[i];
                label.text = marker.Label;
                label.style.backgroundColor = marker.Color;
                label.EnableInClassList(ChosenClass, marker.ItemId == _chosen);

                // Centered on the point; the class sets the size.
                label.style.left = marker.Uv.x * size.x;
                label.style.top = marker.Uv.y * size.y;
                label.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            }
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (_open == null || _pointerId >= 0)
            {
                return;
            }

            Focus();
            Vector2 local = evt.localPosition;
            string picked = evt.button == 0 && !evt.shiftKey ? Pick(_markers, local, Size) : null;
            if (picked != null)
            {
                ItemChosen?.Invoke(picked);
                _mode = _view.BeginDrag(picked, UvOf(local)) ? Mode.Drag : Mode.None;
            }
            else if (evt.button == 2 || (evt.button == 0 && evt.shiftKey))
            {
                _mode = Mode.Pan;
            }
            else if (evt.button == 0 && evt.clickCount == 2)
            {
                _view.ResetView();
                _mode = Mode.None;
            }
            else if (evt.button == 0)
            {
                _mode = Mode.Orbit;
            }

            if (_mode != Mode.None)
            {
                _pointerId = evt.pointerId;
                this.CapturePointer(evt.pointerId);
            }

            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != _pointerId)
            {
                return;
            }

            Vector2 delta = evt.deltaPosition;
            switch (_mode)
            {
                case Mode.Orbit:
                    _view.Orbit(OrbitFor(delta, Preferences?.StageViewInvertX ?? false, Preferences?.StageViewInvertY ?? false));
                    break;
                case Mode.Pan:
                    _view.Pan(new Vector2(Size.x > 0 ? delta.x / Size.x : 0f, Size.y > 0 ? delta.y / Size.y : 0f));
                    break;
                case Mode.Drag:
                    _view.DragTo(UvOf(evt.localPosition));
                    break;
            }

            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != _pointerId)
            {
                return;
            }

            this.ReleasePointer(evt.pointerId);
            EndPointer();
            evt.StopPropagation();
        }

        private void EndPointer()
        {
            if (_mode == Mode.Drag)
            {
                _view.EndDrag();
            }

            _mode = Mode.None;
            _pointerId = -1;
        }

        private void OnWheel(WheelEvent evt)
        {
            if (_open == null || evt.delta.y == 0f)
            {
                return;
            }

            // The page would scroll too; the wheel belongs to the view while the pointer is on it.
            _view.Zoom(-Mathf.Sign(evt.delta.y));
            evt.StopPropagation();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            Vector2 step = evt.keyCode switch
            {
                KeyCode.LeftArrow => new Vector2(-KeyStep, 0f),
                KeyCode.RightArrow => new Vector2(KeyStep, 0f),
                KeyCode.UpArrow => new Vector2(0f, KeyStep),
                KeyCode.DownArrow => new Vector2(0f, -KeyStep),
                _ => Vector2.zero,
            };
            if (step != Vector2.zero)
            {
                if (evt.shiftKey)
                {
                    _view.Orbit(step);
                }
                else
                {
                    NudgeRequested?.Invoke(step);
                }

                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.F && _chosen != null)
            {
                _view.Frame(_chosen);
                evt.StopPropagation();
            }
        }

        private enum Mode
        {
            None,
            Orbit,
            Pan,
            Drag,
        }
    }
}
