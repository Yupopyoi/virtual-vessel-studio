using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Tooltips;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// A slider with its label and current value (components.md 3.4). Drag the track, or use the left
    /// and right keys to move by one step.
    /// </summary>
    [UxmlElement]
    public partial class ValueSlider : VisualElement, INotifyValueChanged<float>, ITooltipSource
    {
        public const string UssClass = "vv-slider";

        private readonly KeyedText _label;
        private readonly Label _valueText;
        private readonly VisualElement _track;
        private readonly VisualElement _fill;
        private readonly VisualElement _thumb;
        private float _low;
        private float _high = 1f;
        private float _step;
        private float _value;
        private string _unit;
        private bool _signed;
        private int _decimals;

        public ValueSlider()
        {
            AddToClassList(UssClass);
            var header = new VisualElement();
            header.AddToClassList(UssClass + "__header");
            var label = new Label();
            label.AddToClassList(UssClass + "__label");
            label.AddToClassList("vv-text-caption");
            _valueText = new Label();
            _valueText.AddToClassList(UssClass + "__value");
            _valueText.AddToClassList("vv-mono");
            header.Add(label);
            header.Add(_valueText);
            Add(header);

            // The track is a 24px hit area around a 6px rail, so it is easy to grab.
            _track = new VisualElement();
            _track.AddToClassList(UssClass + "__track");
            var rail = new VisualElement { pickingMode = PickingMode.Ignore };
            rail.AddToClassList(UssClass + "__rail");
            _track.Add(rail);
            // The rail is Accent Line; this covers the part after the value with Surface 3, so the
            // colored part always ends exactly under the thumb's center.
            _fill = new VisualElement { pickingMode = PickingMode.Ignore };
            _fill.AddToClassList(UssClass + "__rest");
            _thumb = new VisualElement { pickingMode = PickingMode.Ignore };
            _thumb.AddToClassList(UssClass + "__thumb");
            _track.Add(_fill);
            _track.Add(_thumb);
            Add(_track);
            FocusRing.Attach(_track);

            _label = new KeyedText(this, value =>
            {
                label.text = value;
                label.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
            });

            _track.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _track.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _track.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _track.RegisterCallback<NavigationMoveEvent>(OnNavigationMove);
            _track.RegisterCallback<GeometryChangedEvent>(_ => Refresh());
            Refresh();
        }

        [UxmlAttribute("label-key")]
        public string LabelKey
        {
            get => _label.Key;
            set => _label.Key = value;
        }

        [UxmlAttribute("tooltip-key")]
        public string TooltipKey { get; set; }

        string ITooltipSource.CurrentTooltipKey => TooltipKey;

        string ITooltipSource.Shortcut => null;

        [UxmlAttribute("low-value")]
        public float LowValue
        {
            get => _low;
            set
            {
                _low = value;
                SetValueWithoutNotify(_value);
            }
        }

        [UxmlAttribute("high-value")]
        public float HighValue
        {
            get => _high;
            set
            {
                _high = value;
                SetValueWithoutNotify(_value);
            }
        }

        /// <summary>Snapping and keyboard step; 0 snaps to 1/100 of the range.</summary>
        [UxmlAttribute("step")]
        public float Step
        {
            get => _step;
            set
            {
                _step = Mathf.Max(0f, value);
                SetValueWithoutNotify(_value);
            }
        }

        /// <summary>Unit shown after the number, such as "dB". Units are not translated.</summary>
        [UxmlAttribute("unit")]
        public string Unit
        {
            get => _unit;
            set
            {
                _unit = value;
                Refresh();
            }
        }

        /// <summary>Shows "+" for positive values, for adjustments around zero such as pitch.</summary>
        [UxmlAttribute("signed")]
        public bool Signed
        {
            get => _signed;
            set
            {
                _signed = value;
                Refresh();
            }
        }

        [UxmlAttribute("decimals")]
        public int Decimals
        {
            get => _decimals;
            set
            {
                _decimals = Mathf.Clamp(value, 0, 4);
                Refresh();
            }
        }

        [UxmlAttribute("value")]
        public float value
        {
            get => _value;
            set
            {
                float next = Snap(value, _low, _high, _step);
                if (Mathf.Approximately(next, _value))
                {
                    return;
                }

                using (ChangeEvent<float> evt = ChangeEvent<float>.GetPooled(_value, next))
                {
                    evt.target = this;
                    SetValueWithoutNotify(next);
                    SendEvent(evt);
                }
            }
        }

        public string DisplayedValue => _valueText.text;

        /// <summary>
        /// Clamps <paramref name="value"/> to the range and snaps it to <paramref name="step"/> from
        /// <paramref name="low"/>. A step of 0 snaps to 1/100 of the range. Pure.
        /// </summary>
        public static float Snap(float value, float low, float high, float step)
        {
            float min = Mathf.Min(low, high);
            float max = Mathf.Max(low, high);
            float clamped = Mathf.Clamp(value, min, max);
            float size = step > 0f ? step : (max - min) / 100f;
            if (size <= 0f)
            {
                return clamped;
            }

            return Mathf.Clamp(min + (Mathf.Round((clamped - min) / size) * size), min, max);
        }

        /// <summary>The value at <paramref name="x"/> pixels into a track <paramref name="width"/> wide. Pure.</summary>
        public static float ValueAt(float x, float width, float low, float high, float step)
        {
            float t = width > 0f ? Mathf.Clamp01(x / width) : 0f;
            return Snap(Mathf.Lerp(low, high, t), low, high, step);
        }

        /// <summary>Where <paramref name="value"/> sits in the range, from 0 to 1. Pure.</summary>
        public static float Fraction(float value, float low, float high)
        {
            return Mathf.Approximately(low, high) ? 0f : Mathf.Clamp01((value - low) / (high - low));
        }

        /// <summary>
        /// The shown value, such as "−12 dB" or "+4": a true minus sign, an optional plus sign, and the
        /// unit after a space. Pure.
        /// </summary>
        public static string Format(float value, int decimals, bool signed, string unit)
        {
            float rounded = (float)Math.Round(value, decimals, MidpointRounding.AwayFromZero);
            string number = Math.Abs(rounded).ToString("F" + decimals, CultureInfo.InvariantCulture);
            string sign = rounded < 0f ? "−" : signed && rounded > 0f ? "+" : string.Empty;
            return string.IsNullOrEmpty(unit) ? sign + number : sign + number + " " + unit;
        }

        public void SetValueWithoutNotify(float newValue)
        {
            _value = Snap(newValue, _low, _high, _step);
            Refresh();
        }

        private void Refresh()
        {
            float fraction = Fraction(_value, _low, _high);
            _fill.style.left = Length.Percent(fraction * 100f);
            _thumb.style.left = Length.Percent(fraction * 100f);
            _valueText.text = Format(_value, _decimals, _signed, _unit);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (!enabledInHierarchy)
            {
                return;
            }

            _track.CapturePointer(evt.pointerId);
            value = ValueAt(evt.localPosition.x, _track.contentRect.width, _low, _high, _step);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_track.HasPointerCapture(evt.pointerId))
            {
                value = ValueAt(evt.localPosition.x, _track.contentRect.width, _low, _high, _step);
            }
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (_track.HasPointerCapture(evt.pointerId))
            {
                _track.ReleasePointer(evt.pointerId);
            }
        }

        private void OnNavigationMove(NavigationMoveEvent evt)
        {
            int direction = evt.direction == NavigationMoveEvent.Direction.Left ? -1
                : evt.direction == NavigationMoveEvent.Direction.Right ? 1
                : 0;
            if (direction == 0 || !enabledInHierarchy)
            {
                return;
            }

            float size = _step > 0f ? _step : Mathf.Abs(_high - _low) / 100f;
            value = _value + (direction * size * Mathf.Sign(_high - _low));
            evt.StopPropagation();
        }
    }
}
