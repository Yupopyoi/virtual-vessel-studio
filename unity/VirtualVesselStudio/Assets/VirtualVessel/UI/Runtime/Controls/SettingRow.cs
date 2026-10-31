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

        private readonly KeyedText _name;
        private readonly KeyedText _description;
        private readonly VisualElement _text;
        private readonly VisualElement _control;

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
            text.Add(name);
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

        [UxmlAttribute("description-key")]
        public string DescriptionKey
        {
            get => _description.Key;
            set => _description.Key = value;
        }
    }
}
