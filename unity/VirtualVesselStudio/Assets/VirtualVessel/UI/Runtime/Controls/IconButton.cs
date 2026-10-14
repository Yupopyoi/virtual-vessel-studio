using UnityEngine.UIElements;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Tooltips;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// A round icon button (components.md 3.2). With <see cref="Toggle"/> it keeps an on/off state, such
    /// as mute, and reports changes like any field.
    /// </summary>
    [UxmlElement]
    public partial class IconButton : Button, INotifyValueChanged<bool>, ITooltipSource
    {
        public const string UssClass = "vv-icon-button";

        public const string OnClass = UssClass + "--on";

        private readonly Icon _icon;
        private readonly CommandBinding _command;
        private bool _value;

        public IconButton()
            : this(IconName.None)
        {
        }

        public IconButton(IconName icon, bool toggle = false)
        {
            RemoveFromClassList(ussClassName);
            AddToClassList(UssClass);
            FocusRing.Attach(this);
            _icon = new Icon(icon);
            Add(_icon);
            _command = new CommandBinding(this);
            Toggle = toggle;
            clicked += OnClicked;
        }

        [UxmlAttribute("icon")]
        public IconName IconName
        {
            get => _icon.Name;
            set => _icon.Name = value;
        }

        [UxmlAttribute("toggle")]
        public bool Toggle { get; set; }

        [UxmlAttribute("tooltip-key")]
        public string TooltipKey { get; set; }

        [UxmlAttribute("shortcut")]
        public string Shortcut { get; set; }

        public string CurrentTooltipKey => _command.DisabledReasonKey ?? TooltipKey;

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

        public IconButton Bind(IUiCommand command)
        {
            _command.Set(command);
            return this;
        }

        public IconButton WithTooltip(string tooltipKey, string shortcut = null)
        {
            TooltipKey = tooltipKey;
            Shortcut = shortcut;
            return this;
        }

        private void OnClicked()
        {
            if (Toggle)
            {
                value = !_value;
            }
        }
    }
}
