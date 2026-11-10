using UnityEngine.UIElements;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// One setting: its name and description on the left and the control on the right
    /// (components.md 3.9). Children added in UXML or with <c>Add</c> become the control. On a narrow
    /// screen the control wraps below the description.
    /// </summary>
    [UxmlElement]
    public partial class SettingRow : VisualElement
    {
        public const string UssClass = "vv-setting-row";

        /// <summary>For an element holding more than one control on the row, so they keep the S space between them.</summary>
        public const string ControlsClass = UssClass + "__controls";

        private readonly KeyedText _name;
        private readonly KeyedText _description;
        private readonly VisualElement _text;
        private readonly VisualElement _control;
        private readonly HelpHint _help;

        public SettingRow()
            : this(null, null)
        {
        }

        public SettingRow(string nameKey, string descriptionKey)
        {
            AddToClassList(UssClass);
            var text = new VisualElement();
            text.AddToClassList(UssClass + "__text");
            _text = text;
            var name = new Label();
            name.AddToClassList(UssClass + "__name");
            name.AddToClassList("vv-text-body");
            var description = new Label();
            description.AddToClassList(UssClass + "__description");
            description.AddToClassList("vv-text-caption");
            // The name and, when the row has one, its help hint (components.md 3.28) on one line.
            var heading = new VisualElement();
            heading.AddToClassList(UssClass + "__heading");
            heading.Add(name);
            _help = new HelpHint(null);
            _help.style.display = DisplayStyle.None;
            heading.Add(_help);
            text.Add(heading);
            text.Add(description);
            hierarchy.Add(text);

            _control = new VisualElement();
            _control.AddToClassList(UssClass + "__control");
            hierarchy.Add(_control);

            _name = new KeyedText(this, value => name.text = value);
            _description = new KeyedText(this, value =>
            {
                description.text = value;
                description.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
            });
            NameKey = nameKey;
            DescriptionKey = descriptionKey;
        }

        public override VisualElement contentContainer => _control;

        /// <summary>
        /// The name and description. A value that reads with them and can be long, such as a folder's path,
        /// goes here under the description, so the control on the right stays as narrow as the others.
        /// </summary>
        public VisualElement TextArea => _text;

        [UxmlAttribute("name-key")]
        public string NameKey
        {
            get => _name.Key;
            set => _name.Key = value;
        }

        /// <summary>The explanation behind a help hint after the name; null for no hint.</summary>
        [UxmlAttribute("help-key")]
        public string HelpKey
        {
            get => _help.TooltipKey;
            set
            {
                _help.TooltipKey = value;
                _help.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        /// <summary>The help hint after the name, shown when <see cref="HelpKey"/> is set.</summary>
        public HelpHint Help => _help;

        [UxmlAttribute("description-key")]
        public string DescriptionKey
        {
            get => _description.Key;
            set => _description.Key = value;
        }
    }
}
