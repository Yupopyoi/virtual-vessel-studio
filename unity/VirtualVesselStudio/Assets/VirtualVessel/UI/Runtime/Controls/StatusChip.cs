using System;
using UnityEngine.UIElements;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Icons;

namespace VirtualVessel.UI.Controls
{
    /// <summary>Statuses shown by shape, text, and color together, never by color alone (design-system.md 3.4, 9.1).</summary>
    public enum StatusKind
    {
        /// <summary>Stopped or not connected: outline circle.</summary>
        Idle,

        /// <summary>On air: filled dot that blinks slowly.</summary>
        Live,

        Success,
        Warning,
        Error,
    }

    /// <summary>A status indicator with an icon, text, and an optional number (components.md 3.13).</summary>
    [UxmlElement]
    public partial class StatusChip : VisualElement
    {
        public const string UssClass = "vv-chip";

        public const string DimClass = UssClass + "--dim";

        private const long BlinkIntervalMs = 800;

        private readonly Icon _icon;
        private readonly Label _label;
        private readonly Label _number;
        private readonly KeyedText _text;
        private StatusKind _status;

        public StatusChip()
            : this(StatusKind.Idle, null)
        {
        }

        public StatusChip(StatusKind status, string textKey)
        {
            AddToClassList(UssClass);
            _icon = new Icon();
            _icon.AddToClassList(UssClass + "__icon");
            _label = new Label();
            _label.AddToClassList(UssClass + "__label");
            _label.AddToClassList("vv-bold");
            _number = new Label();
            _number.AddToClassList(UssClass + "__number");
            _number.AddToClassList("vv-mono");
            Add(_icon);
            Add(_label);
            Add(_number);
            _text = new KeyedText(this, value =>
            {
                _label.text = value;
                _label.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
            });
            TextKey = textKey;
            Status = status;
            Number = null;

            // Blink only while on a panel, so a hidden chip does not keep a timer running.
            PanelLifetime.WhileAttached(this, _ =>
            {
                IVisualElementScheduledItem blink = schedule.Execute(Blink).Every(BlinkIntervalMs);
                return new DisposeAction(() =>
                {
                    blink.Pause();
                    _icon.RemoveFromClassList(DimClass);
                });
            });
        }

        [UxmlAttribute("status")]
        public StatusKind Status
        {
            get => _status;
            set
            {
                _status = value;
                foreach (StatusKind kind in (StatusKind[])Enum.GetValues(typeof(StatusKind)))
                {
                    EnableInClassList(ClassFor(kind), kind == value);
                }

                _icon.Name = IconFor(value);
                _icon.RemoveFromClassList(DimClass);
            }
        }

        [UxmlAttribute("text-key")]
        public string TextKey
        {
            get => _text.Key;
            set => _text.Key = value;
        }

        /// <summary>A measured value such as "38 ms", shown in monospace digits. Not translated.</summary>
        [UxmlAttribute("number")]
        public string Number
        {
            get => _number.text;
            set
            {
                _number.text = value ?? string.Empty;
                _number.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        [UxmlAttribute("tooltip-key")]
        public string TooltipKey { get; set; }

        /// <summary>The shape for each status (design-system.md 3.4). Pure.</summary>
        public static IconName IconFor(StatusKind status)
        {
            return status switch
            {
                StatusKind.Live => IconName.Dot,
                StatusKind.Success => IconName.Check,
                StatusKind.Warning => IconName.Warning,
                StatusKind.Error => IconName.Close,
                _ => IconName.Ring,
            };
        }

        public static string ClassFor(StatusKind status)
        {
            return UssClass + "--" + status.ToString().ToLowerInvariant();
        }

        /// <summary>Whether the icon blinks: only for Live, and never with reduced motion. Pure.</summary>
        public static bool ShouldBlink(StatusKind status, bool reducedMotion)
        {
            return status == StatusKind.Live && !reducedMotion;
        }

        private void Blink()
        {
            bool blink = ShouldBlink(_status, ReducedMotion.IsOn(this));
            _icon.EnableInClassList(DimClass, blink && !_icon.ClassListContains(DimClass));
        }
    }
}
