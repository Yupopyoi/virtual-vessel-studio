using UnityEngine.UIElements;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// A titled group with the accent decoration (components.md 3.10, design-system.md 3.6). Children
    /// added in UXML or with <c>Add</c> go into the body; <see cref="HeaderEnd"/> holds items at the
    /// right end of the title, such as a status chip.
    /// </summary>
    [UxmlElement]
    public partial class Card : VisualElement
    {
        public const string UssClass = "vv-card";

        public const string AlternateClass = UssClass + "--alt";

        private readonly KeyedText _title;
        private readonly VisualElement _body;

        public Card()
            : this(null)
        {
        }

        public Card(string titleKey, bool alternate = false)
        {
            AddToClassList(UssClass);
            var header = new VisualElement();
            header.AddToClassList(UssClass + "__header");
            var bar = new VisualElement();
            bar.AddToClassList(UssClass + "__bar");
            var title = new Label();
            title.AddToClassList(UssClass + "__title");
            title.AddToClassList("vv-bold");
            HeaderEnd = new VisualElement();
            HeaderEnd.AddToClassList(UssClass + "__header-end");
            header.Add(bar);
            header.Add(title);
            header.Add(HeaderEnd);
            hierarchy.Add(header);

            _body = new VisualElement();
            _body.AddToClassList(UssClass + "__body");
            hierarchy.Add(_body);

            _title = new KeyedText(this, value => title.text = value);
            TitleKey = titleKey;
            Alternate = alternate;
        }

        public override VisualElement contentContainer => _body;

        public VisualElement HeaderEnd { get; }

        [UxmlAttribute("title-key")]
        public string TitleKey
        {
            get => _title.Key;
            set => _title.Key = value;
        }

        /// <summary>Uses Secondary for the decoration; alternate cards in a row (design-system.md 3.6).</summary>
        [UxmlAttribute("alternate")]
        public bool Alternate
        {
            get => ClassListContains(AlternateClass);
            set => EnableInClassList(AlternateClass, value);
        }
    }
}
