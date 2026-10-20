using UnityEngine.UIElements;
using VirtualVessel.UI.Icons;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// A titled group with the accent decoration (components.md 3.10, design-system.md 3.6). Children
    /// added in UXML or with <c>Add</c> go into the body; <see cref="HeaderEnd"/> holds items at the
    /// right end of the title, such as a status chip. A collapsible Card opens and closes its body
    /// when the title is clicked or submitted from the keyboard.
    /// </summary>
    [UxmlElement]
    public partial class Card : VisualElement
    {
        public const string UssClass = "vv-card";

        public const string AlternateClass = UssClass + "--alt";

        public const string CollapsibleClass = UssClass + "--collapsible";

        public const string CollapsedClass = UssClass + "--collapsed";

        public const string HeaderClass = UssClass + "__header";

        private readonly KeyedText _title;
        private readonly VisualElement _body;
        private bool _expanded = true;

        public Card()
            : this(null)
        {
        }

        public Card(string titleKey, bool alternate = false, bool collapsible = false)
        {
            AddToClassList(UssClass);
            Header = new VisualElement();
            Header.AddToClassList(HeaderClass);
            var bar = new VisualElement();
            bar.AddToClassList(UssClass + "__bar");
            var title = new Label();
            title.AddToClassList(UssClass + "__title");
            title.AddToClassList("vv-bold");
            HeaderEnd = new VisualElement();
            HeaderEnd.AddToClassList(UssClass + "__header-end");

            // Shown only on collapsible Cards; points down while closed, up while open.
            var chevron = new Icon(IconName.ChevronDown);
            chevron.AddToClassList(UssClass + "__chevron");
            Header.Add(bar);
            Header.Add(title);
            Header.Add(HeaderEnd);
            Header.Add(chevron);
            hierarchy.Add(Header);

            FocusRing.Attach(Header);
            Header.AddManipulator(new Clickable(ToggleExpanded));
            Header.RegisterCallback<NavigationSubmitEvent>(evt =>
            {
                if (Collapsible)
                {
                    ToggleExpanded();
                    evt.StopPropagation();
                }
            });

            _body = new VisualElement();
            _body.AddToClassList(UssClass + "__body");
            hierarchy.Add(_body);

            _title = new KeyedText(this, value => title.text = value);
            TitleKey = titleKey;
            Alternate = alternate;
            Collapsible = collapsible;
        }

        public override VisualElement contentContainer => _body;

        /// <summary>The title band; on a collapsible Card, the part that opens and closes it.</summary>
        public VisualElement Header { get; }

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

        /// <summary>Lets the title open and close the body. A Card that is not collapsible always shows its body.</summary>
        [UxmlAttribute("collapsible")]
        public bool Collapsible
        {
            get => ClassListContains(CollapsibleClass);
            set
            {
                EnableInClassList(CollapsibleClass, value);
                Header.focusable = value;
                ShowBody();
            }
        }

        /// <summary>Whether the body is open. Kept while the Card is not collapsible, and used once it becomes so.</summary>
        [UxmlAttribute("expanded")]
        public bool Expanded
        {
            get => _expanded;
            set
            {
                _expanded = value;
                ShowBody();
            }
        }

        /// <summary>Whether the body shows: always, unless the Card is collapsible and closed. Pure.</summary>
        public static bool IsBodyShown(bool collapsible, bool expanded)
        {
            return !collapsible || expanded;
        }

        private void ToggleExpanded()
        {
            if (Collapsible)
            {
                Expanded = !Expanded;
            }
        }

        private void ShowBody()
        {
            bool shown = IsBodyShown(Collapsible, _expanded);
            _body.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            EnableInClassList(CollapsedClass, !shown);
        }
    }
}
