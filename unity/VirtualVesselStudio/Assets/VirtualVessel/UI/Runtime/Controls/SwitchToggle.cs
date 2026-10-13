using UnityEngine.UIElements;

namespace VirtualVessel.UI.Controls
{
    /// <summary>An on/off switch with its label (components.md 3.3).</summary>
    [UxmlElement]
    public partial class SwitchToggle : VisualElement, INotifyValueChanged<bool>
    {
        public const string UssClass = "vv-switch";

        public const string OnClass = UssClass + "--on";

        private readonly KeyedText _text;
        private bool _value;

        public SwitchToggle()
            : this(null)
        {
        }

        public SwitchToggle(string textKey)
        {
            AddToClassList(UssClass);
            var label = new Label();
            label.AddToClassList(UssClass + "__label");
            label.AddToClassList("vv-text-body");
            var track = new VisualElement();
            track.AddToClassList(UssClass + "__track");
            var thumb = new VisualElement();
            thumb.AddToClassList(UssClass + "__thumb");
            track.Add(thumb);
            Add(track);
            Add(label);
            FocusRing.Attach(track);

            // The whole row is one control: the track is the focus target and the label is part of it.
            track.focusable = true;
            _text = new KeyedText(this, value => label.text = value);
            TextKey = textKey;

            RegisterCallback<ClickEvent>(_ => Flip());
            track.RegisterCallback<NavigationSubmitEvent>(_ => Flip());
        }

        [UxmlAttribute("text-key")]
        public string TextKey
        {
            get => _text.Key;
            set => _text.Key = value;
        }

        [UxmlAttribute("tooltip-key")]
        public string TooltipKey { get; set; }

        [UxmlAttribute("value")]
        public bool value
        {
            get => _value;
            set
            {
                if (_value == value)
                {
                    return;
                }

                using (ChangeEvent<bool> evt = ChangeEvent<bool>.GetPooled(_value, value))
                {
                    evt.target = this;
                    SetValueWithoutNotify(value);
                    SendEvent(evt);
                }
            }
        }

        public void SetValueWithoutNotify(bool newValue)
        {
            _value = newValue;
            EnableInClassList(OnClass, newValue);
        }

        private void Flip()
        {
            if (enabledInHierarchy)
            {
                value = !_value;
            }
        }
    }
}
