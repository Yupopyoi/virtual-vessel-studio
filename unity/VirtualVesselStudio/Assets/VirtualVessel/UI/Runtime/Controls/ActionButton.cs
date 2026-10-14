using UnityEngine.UIElements;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Tooltips;

namespace VirtualVessel.UI.Controls
{
    public enum ButtonVariant
    {
        Primary,
        Secondary,

        /// <summary>For operations that are hard to undo, such as ending a stream (components.md 3.1).</summary>
        Danger,
    }

    /// <summary>A text button (components.md 3.1).</summary>
    [UxmlElement]
    public partial class ActionButton : Button, ITooltipSource
    {
        public const string UssClass = "vv-button";

        private readonly Label _label;
        private readonly KeyedText _text;
        private readonly CommandBinding _command;
        private ButtonVariant _variant;

        public ActionButton()
            : this(null)
        {
        }

        public ActionButton(string textKey, ButtonVariant variant = ButtonVariant.Secondary)
        {
            // Drop Unity's default button look; every state is styled from tokens instead.
            RemoveFromClassList(ussClassName);
            AddToClassList(UssClass);

            // The text is a child label: a text element with children (the focus ring) is no longer
            // measured by its text, so the button would collapse to its padding.
            _label = new Label { pickingMode = PickingMode.Ignore };
            _label.AddToClassList(UssClass + "__label");
            _label.AddToClassList("vv-text-button");
            Add(_label);
            FocusRing.Attach(this);
            _text = new KeyedText(this, value => _label.text = value);
            _command = new CommandBinding(this);
            TextKey = textKey;
            Variant = variant;
        }

        [UxmlAttribute("text-key")]
        public string TextKey
        {
            get => _text.Key;
            set => _text.Key = value;
        }

        /// <summary>The text shown now, from <see cref="TextKey"/>.</summary>
        public string DisplayedText => _label.text;

        [UxmlAttribute("tooltip-key")]
        public string TooltipKey { get; set; }

        [UxmlAttribute("shortcut")]
        public string Shortcut { get; set; }

        [UxmlAttribute("variant")]
        public ButtonVariant Variant
        {
            get => _variant;
            set
            {
                _variant = value;
                EnableInClassList(UssClass + "--primary", value == ButtonVariant.Primary);
                EnableInClassList(UssClass + "--secondary", value == ButtonVariant.Secondary);
                EnableInClassList(UssClass + "--danger", value == ButtonVariant.Danger);
            }
        }

        /// <summary>The tooltip to show now: why the button cannot be pressed, or its normal tooltip.</summary>
        public string CurrentTooltipKey => _command.DisabledReasonKey ?? TooltipKey;

        /// <summary>Runs <paramref name="command"/> on click; see <see cref="CommandBinding"/>.</summary>
        public ActionButton Bind(IUiCommand command)
        {
            _command.Set(command);
            return this;
        }

        public ActionButton WithTooltip(string tooltipKey, string shortcut = null)
        {
            TooltipKey = tooltipKey;
            Shortcut = shortcut;
            return this;
        }
    }
}
