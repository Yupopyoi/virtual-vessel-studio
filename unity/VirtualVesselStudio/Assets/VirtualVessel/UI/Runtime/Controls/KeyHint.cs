using UnityEngine.UIElements;

namespace VirtualVessel.UI.Controls
{
    /// <summary>A keyboard key or shortcut, such as "Ctrl+M" (components.md 3.21). Key names are not translated.</summary>
    [UxmlElement]
    public partial class KeyHint : Label
    {
        public const string UssClass = "vv-key-hint";

        public KeyHint()
            : this(null)
        {
        }

        public KeyHint(string keys)
        {
            AddToClassList(UssClass);
            AddToClassList("vv-mono");
            text = keys;
        }

        [UxmlAttribute("keys")]
        public string Keys
        {
            get => text;
            set => text = value;
        }
    }
}
