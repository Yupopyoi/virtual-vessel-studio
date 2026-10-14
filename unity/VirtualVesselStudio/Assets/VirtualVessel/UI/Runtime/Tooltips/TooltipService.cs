using System;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.Tooltips
{
    /// <summary>
    /// Shows one shared tooltip for every part under a root (UI foundation detailed design 7.6). It appears
    /// after the pointer rests on a part with a tooltip, or at once when a part is focused from the
    /// keyboard, and disappears when the pointer leaves, a button is pressed, or the view scrolls.
    /// </summary>
    /// <remarks>Main thread only. The UI root attaches it once to the themed root.</remarks>
    public sealed class TooltipService : IDisposable
    {
        public const string UssClass = "vv-tooltip";

        private readonly TooltipOptions _options;
        private VisualElement _root;
        private VisualElement _tooltip;
        private Label _text;
        private KeyHint _shortcut;
        private VisualElement _hovered;
        private VisualElement _shownFor;
        private IVisualElementScheduledItem _pending;

        public TooltipService(TooltipOptions options = null)
        {
            _options = options ?? new TooltipOptions();
        }

        public bool IsVisible => _tooltip != null && _tooltip.style.display == DisplayStyle.Flex;

        public string ShownText => IsVisible ? _text.text : null;

        public VisualElement Element => _tooltip;

        /// <summary>The nearest part at or above <paramref name="element"/> with a tooltip, or null. Pure.</summary>
        public static VisualElement FindSource(VisualElement element)
        {
            for (VisualElement current = element; current != null; current = current.hierarchy.parent)
            {
                if (current is ITooltipSource source && !string.IsNullOrEmpty(source.CurrentTooltipKey))
                {
                    return current;
                }
            }

            return null;
        }

        public void Attach(VisualElement root)
        {
            Detach();
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _tooltip = new VisualElement { pickingMode = PickingMode.Ignore };
            _tooltip.AddToClassList(UssClass);
            _tooltip.style.display = DisplayStyle.None;
            _text = QuietText.Of(new Label());
            _text.AddToClassList(UssClass + "__text");
            _text.AddToClassList("vv-text-caption");
            _shortcut = new KeyHint();
            _shortcut.AddToClassList(UssClass + "__shortcut");
            _tooltip.Add(_text);
            _tooltip.Add(_shortcut);
            _root.Add(_tooltip);
            _tooltip.RegisterCallback<GeometryChangedEvent>(_ => Position());

            // Trickle-down on the root sees every pointer and focus event, including those whose target
            // is a disabled part; disabled parts are exactly where the "why" tooltip matters.
            _root.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerDownEvent>(OnDismiss, TrickleDown.TrickleDown);
            _root.RegisterCallback<WheelEvent>(OnDismiss, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerLeaveEvent>(OnPointerLeave);
            _root.RegisterCallback<FocusInEvent>(OnFocusIn, TrickleDown.TrickleDown);
            _root.RegisterCallback<FocusOutEvent>(OnFocusOut, TrickleDown.TrickleDown);
        }

        public void Detach()
        {
            if (_root == null)
            {
                return;
            }

            Hide();
            _root.UnregisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerDownEvent>(OnDismiss, TrickleDown.TrickleDown);
            _root.UnregisterCallback<WheelEvent>(OnDismiss, TrickleDown.TrickleDown);
            _root.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave);
            _root.UnregisterCallback<FocusInEvent>(OnFocusIn, TrickleDown.TrickleDown);
            _root.UnregisterCallback<FocusOutEvent>(OnFocusOut, TrickleDown.TrickleDown);
            _tooltip.RemoveFromHierarchy();
            _tooltip = null;
            _root = null;
        }

        public void Dispose()
        {
            Detach();
        }

        /// <summary>Starts the delay for the part under the pointer, as resting the pointer does.</summary>
        public void Hover(VisualElement element)
        {
            VisualElement source = FindSource(element);
            if (source == _hovered)
            {
                return;
            }

            _hovered = source;
            Hide();
            if (source != null && _root != null)
            {
                _pending = _root.schedule.Execute(() => Show(source)).StartingIn((long)_options.ShowDelay.TotalMilliseconds);
            }
        }

        /// <summary>Shows the tooltip of <paramref name="element"/> now.</summary>
        public void Show(VisualElement element)
        {
            VisualElement source = FindSource(element);
            if (_tooltip == null || source == null || source.panel == null)
            {
                return;
            }

            _pending?.Pause();
            _pending = null;
            var tooltip = (ITooltipSource)source;
            _text.text = KeyedText.Resolve(UiServices.Find(source)?.Localizer, tooltip.CurrentTooltipKey);
            _shortcut.Keys = tooltip.Shortcut;
            _shortcut.style.display = string.IsNullOrEmpty(tooltip.Shortcut) ? DisplayStyle.None : DisplayStyle.Flex;
            _shownFor = source;

            // Shown invisibly first so that its size is known before it is placed.
            _tooltip.style.visibility = Visibility.Hidden;
            _tooltip.style.display = DisplayStyle.Flex;
            _tooltip.BringToFront();
            Position();
        }

        public void Hide()
        {
            _pending?.Pause();
            _pending = null;
            _shownFor = null;
            if (_tooltip != null)
            {
                _tooltip.style.display = DisplayStyle.None;
            }
        }

        private void Position()
        {
            if (_shownFor == null || _shownFor.panel == null || float.IsNaN(_tooltip.layout.width))
            {
                return;
            }

            Rect target = _root.WorldToLocal(_shownFor.worldBound);
            var size = new Vector2(_tooltip.layout.width, _tooltip.layout.height);
            Vector2 position = TooltipPlacement.Place(target, size, new Rect(Vector2.zero, _root.layout.size), _options.Gap, _options.EdgeMargin);
            _tooltip.style.left = position.x;
            _tooltip.style.top = position.y;
            _tooltip.style.visibility = Visibility.Visible;
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            Hover(_root.panel?.Pick(evt.position));
        }

        private void OnPointerLeave(PointerLeaveEvent evt)
        {
            _hovered = null;
            Hide();
        }

        private void OnDismiss(EventBase evt)
        {
            Hide();
        }

        private void OnFocusIn(FocusInEvent evt)
        {
            // The focus ring decides on the same event whether focus came from the keyboard; check after it.
            if (evt.target is VisualElement target && FocusRing.IsFromKeyboard(evt.direction))
            {
                _root.schedule.Execute(() => Show(target));
            }
        }

        private void OnFocusOut(FocusOutEvent evt)
        {
            if (_shownFor != null && evt.target is VisualElement target && _shownFor.Contains(target))
            {
                Hide();
            }
        }
    }
}
