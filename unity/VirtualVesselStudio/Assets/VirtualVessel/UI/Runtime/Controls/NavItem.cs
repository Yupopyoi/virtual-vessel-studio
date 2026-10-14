using UnityEngine.UIElements;
using VirtualVessel.UI.Icons;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// An item of the side navigation (components.md 3.16). The screen host decides which item is
    /// selected; the item only shows it.
    /// </summary>
    [UxmlElement]
    public partial class NavItem : Button
    {
        public const string UssClass = "vv-nav-item";

        public const string SelectedClass = UssClass + "--selected";

        private readonly Icon _icon;
        private readonly Label _label;
        private readonly KeyedText _text;

        public NavItem()
            : this(IconName.None, null)
        {
        }

        public NavItem(IconName icon, string textKey)
        {
            RemoveFromClassList(ussClassName);
            AddToClassList(UssClass);
            _icon = new Icon(icon);
            _icon.AddToClassList(UssClass + "__icon");
            _label = new Label { pickingMode = PickingMode.Ignore };
            _label.AddToClassList(UssClass + "__label");
            _label.AddToClassList("vv-text-body");
            Add(_icon);
            Add(_label);
            FocusRing.Attach(this);
            _text = new KeyedText(this, value => _label.text = value);
            TextKey = textKey;
        }

        [UxmlAttribute("icon")]
        public IconName IconName
        {
            get => _icon.Name;
            set => _icon.Name = value;
        }

        [UxmlAttribute("text-key")]
        public string TextKey
        {
            get => _text.Key;
            set => _text.Key = value;
        }

        [UxmlAttribute("tooltip-key")]
        public string TooltipKey { get; set; }

        [UxmlAttribute("selected")]
        public bool Selected
        {
            get => ClassListContains(SelectedClass);
            set => EnableInClassList(SelectedClass, value);
        }

        public string DisplayedText => _label.text;
    }
}
