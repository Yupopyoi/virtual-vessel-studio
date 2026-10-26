using UnityEngine.UIElements;
using VirtualVessel.UI.Icons;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// Short factories for building screens in C# without UXML (UI foundation detailed design 6.3). Every
    /// text is a string key; chain <c>WithTooltip</c> and <c>Bind</c> on buttons.
    /// </summary>
    /// <example>
    /// <code>
    /// var start = Ui.Button("live.start", ButtonVariant.Primary)
    ///     .WithTooltip("tip.live.start", "Ctrl+Enter")
    ///     .Bind(_startStreaming);
    /// </code>
    /// </example>
    public static class Ui
    {
        public static ActionButton Button(string textKey, ButtonVariant variant = ButtonVariant.Secondary)
        {
            return new ActionButton(textKey, variant);
        }

        public static IconButton IconButton(IconName icon, bool toggle = false)
        {
            return new IconButton(icon, toggle);
        }

        public static SwitchToggle Toggle(string textKey)
        {
            return new SwitchToggle(textKey);
        }

        public static Segmented Segmented(params string[] itemKeys)
        {
            return new Segmented(itemKeys);
        }

        public static TabBar Tabs(params string[] tabKeys)
        {
            return new TabBar(tabKeys);
        }

        public static Card Card(string titleKey, bool alternate = false, bool collapsible = false)
        {
            return new Card(titleKey, alternate, collapsible);
        }

        public static SettingRow SettingRow(string nameKey, string descriptionKey = null)
        {
            return new SettingRow(nameKey, descriptionKey);
        }

        public static StatusChip Chip(StatusKind status, string textKey)
        {
            return new StatusChip(status, textKey);
        }

        public static NavItem NavItem(IconName icon, string textKey)
        {
            return new NavItem(icon, textKey);
        }

        public static ValueSlider Slider(string labelKey, float lowValue, float highValue, string unit = null)
        {
            return new ValueSlider { LabelKey = labelKey, LowValue = lowValue, HighValue = highValue, Unit = unit };
        }

        public static SelectField Select(string labelKey)
        {
            return new SelectField { LabelKey = labelKey };
        }

        public static TextInput TextField(string labelKey)
        {
            return new TextInput { LabelKey = labelKey };
        }

        public static NumberInput NumberField(string labelKey, string unit = null)
        {
            return new NumberInput { LabelKey = labelKey, Unit = unit };
        }

        public static ProgressMeter Progress(string labelKey)
        {
            return new ProgressMeter { LabelKey = labelKey };
        }

        /// <summary>A table whose header and rows line up by the given columns (components.md 3.24).</summary>
        public static DataTable Table(params TableColumn[] columns)
        {
            return new DataTable(columns);
        }

        /// <summary>A label whose text follows the language, with optional classes such as <c>vv-text-caption</c>.</summary>
        public static Label Text(string textKey, params string[] classes)
        {
            Label label = QuietText.Of(new Label());
            foreach (string ussClass in classes)
            {
                label.AddToClassList(ussClass);
            }

            new KeyedText(label, value => label.text = value) { Key = textKey };
            return label;
        }
    }
}
