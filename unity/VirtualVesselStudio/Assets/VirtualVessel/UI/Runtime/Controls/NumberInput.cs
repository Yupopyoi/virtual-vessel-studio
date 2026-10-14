using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// A number field with its label and unit (components.md 3.7). The typed text is committed on
    /// Enter or when focus leaves: values outside the range are brought into it, and text that is not a
    /// number shows an error and keeps the previous value.
    /// </summary>
    [UxmlElement]
    public partial class NumberInput : VisualElement, INotifyValueChanged<double>
    {
        public const string UssClass = "vv-number-input";

        private readonly KeyedText _label;
        private readonly TextField _field;
        private readonly Label _unit;
        private readonly FieldError _error;
        private double _value;
        private double _min = double.MinValue;
        private double _max = double.MaxValue;
        private int _decimals;

        public NumberInput()
        {
            AddToClassList(UssClass);
            AddToClassList("vv-field");
            Label label = QuietText.Of(new Label());
            label.AddToClassList("vv-field__label");
            label.AddToClassList("vv-text-caption");
            Add(label);

            var row = new VisualElement();
            row.AddToClassList(UssClass + "__row");
            _field = new TextField();
            _field.AddToClassList("vv-field__input");
            _field.AddToClassList(UssClass + "__input");
            _field.AddToClassList("vv-mono");
            _unit = QuietText.Of(new Label());
            _unit.AddToClassList(UssClass + "__unit");
            _unit.AddToClassList("vv-mono");
            row.Add(_field);
            row.Add(_unit);
            Add(row);
            _error = new FieldError(this);
            Add(_error.Element);

            _label = new KeyedText(this, value =>
            {
                label.text = value;
                label.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
            });

            // Typing changes only the text; the value changes when the text is committed.
            _field.RegisterValueChangedCallback(evt => evt.StopPropagation());
            _field.RegisterCallback<FocusOutEvent>(_ => Commit());
            _field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    Commit();
                }
            }, TrickleDown.TrickleDown);
            Unit = null;
            ShowValue();
        }

        [UxmlAttribute("label-key")]
        public string LabelKey
        {
            get => _label.Key;
            set => _label.Key = value;
        }

        [UxmlAttribute("tooltip-key")]
        public string TooltipKey { get; set; }

        /// <summary>Unit shown in its own cell to the right, such as "Hz". Units are not translated.</summary>
        [UxmlAttribute("unit")]
        public string Unit
        {
            get => _unit.text;
            set
            {
                _unit.text = value ?? string.Empty;
                _unit.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
                EnableInClassList(UssClass + "--unit", !string.IsNullOrEmpty(value));
            }
        }

        [UxmlAttribute("min")]
        public double Min
        {
            get => _min;
            set
            {
                _min = value;
                SetValueWithoutNotify(_value);
            }
        }

        [UxmlAttribute("max")]
        public double Max
        {
            get => _max;
            set
            {
                _max = value;
                SetValueWithoutNotify(_value);
            }
        }

        [UxmlAttribute("decimals")]
        public int Decimals
        {
            get => _decimals;
            set
            {
                _decimals = Mathf.Clamp(value, 0, 6);
                SetValueWithoutNotify(_value);
            }
        }

        /// <summary>The message key shown under the field, or null for no error.</summary>
        public string ErrorKey
        {
            get => _error.Key;
            set => _error.Key = value;
        }

        public string Text => _field.value;

        [UxmlAttribute("value")]
        public double value
        {
            get => _value;
            set
            {
                double next = Normalize(value, _min, _max, _decimals);
                if (next.Equals(_value))
                {
                    ShowValue();
                    return;
                }

                using (ChangeEvent<double> evt = ChangeEvent<double>.GetPooled(_value, next))
                {
                    evt.target = this;
                    SetValueWithoutNotify(next);
                    SendEvent(evt);
                }
            }
        }

        /// <summary>
        /// Reads typed text as a number: invariant digits, an optional sign (the true minus sign too),
        /// and surrounding spaces. Full-width digits typed with a Japanese IME are accepted. Pure.
        /// </summary>
        public static bool TryParse(string text, out double number)
        {
            number = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var normalized = new char[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                normalized[i] = c >= '０' && c <= '９' ? (char)('0' + (c - '０'))
                    : c == '−' || c == '－' ? '-'
                    : c == '．' ? '.'
                    : c == '＋' ? '+'
                    : c;
            }

            return double.TryParse(new string(normalized).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                && !double.IsNaN(number) && !double.IsInfinity(number);
        }

        /// <summary>Brings <paramref name="number"/> into the range and rounds it. Pure.</summary>
        public static double Normalize(double number, double min, double max, int decimals)
        {
            double low = Math.Min(min, max);
            double high = Math.Max(min, max);
            return Math.Round(Math.Max(low, Math.Min(high, number)), decimals, MidpointRounding.AwayFromZero);
        }

        /// <summary>The text shown for <paramref name="number"/>. Pure.</summary>
        public static string Format(double number, int decimals)
        {
            return number.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        public void SetValueWithoutNotify(double newValue)
        {
            _value = Normalize(newValue, _min, _max, _decimals);
            ShowValue();
        }

        /// <summary>Applies the typed text, as Enter or leaving the field does.</summary>
        public void Commit()
        {
            if (TryParse(_field.value, out double number))
            {
                ErrorKey = null;
                value = number;
            }
            else
            {
                ErrorKey = CommonStrings.NumberInvalid;
            }
        }

        private void ShowValue()
        {
            _field.SetValueWithoutNotify(Format(_value, _decimals));
        }
    }
}
