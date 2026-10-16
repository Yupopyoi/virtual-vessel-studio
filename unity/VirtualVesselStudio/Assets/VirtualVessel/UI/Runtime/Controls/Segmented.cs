using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VirtualVessel.UI.Tooltips;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// Picks one of two to four options (components.md 3.18). The value is the selected index.
    /// Left and right arrows move the selection when focused.
    /// </summary>
    [UxmlElement]
    public partial class Segmented : VisualElement, INotifyValueChanged<int>, ITooltipSource
    {
        public const string UssClass = "vv-segmented";

        public const string ItemClass = UssClass + "__item";

        public const string SelectedClass = ItemClass + "--selected";

        private readonly List<Label> _items = new List<Label>();
        private string[] _itemKeys = Array.Empty<string>();
        private int _value;

        public Segmented()
        {
            AddToClassList(UssClass);
            FocusRing.Attach(this);
            RegisterCallback<NavigationMoveEvent>(OnNavigationMove);
        }

        public Segmented(params string[] itemKeys)
            : this()
        {
            SetItems(itemKeys);
        }

        /// <summary>Option text keys, comma-separated, such as "demo.dark,demo.light".</summary>
        [UxmlAttribute("item-keys")]
        public string ItemKeys
        {
            get => string.Join(",", _itemKeys);
            set => SetItems(SplitKeys(value));
        }

        [UxmlAttribute("tooltip-key")]
        public string TooltipKey { get; set; }

        string ITooltipSource.CurrentTooltipKey => TooltipKey;

        string ITooltipSource.Shortcut => null;

        [UxmlAttribute("value")]
        public int value
        {
            get => _value;
            set
            {
                int clamped = _items.Count == 0 ? value : Clamp(value, _items.Count);
                if (_value == clamped)
                {
                    return;
                }

                using (ChangeEvent<int> evt = ChangeEvent<int>.GetPooled(_value, clamped))
                {
                    evt.target = this;
                    SetValueWithoutNotify(clamped);
                    SendEvent(evt);
                }
            }
        }

        /// <summary>Splits the attribute value into keys, ignoring spaces and empty entries. Pure.</summary>
        public static string[] SplitKeys(string keys)
        {
            if (string.IsNullOrWhiteSpace(keys))
            {
                return Array.Empty<string>();
            }

            var result = new List<string>();
            foreach (string key in keys.Split(','))
            {
                string trimmed = key.Trim();
                if (trimmed.Length > 0)
                {
                    result.Add(trimmed);
                }
            }

            return result.ToArray();
        }

        /// <summary>Keeps an index inside the options; 0 when there are none. Pure.</summary>
        public static int Clamp(int index, int count)
        {
            return count == 0 ? 0 : Math.Max(0, Math.Min(count - 1, index));
        }

        public void SetItems(params string[] itemKeys)
        {
            _itemKeys = itemKeys ?? Array.Empty<string>();
            foreach (Label item in _items)
            {
                item.RemoveFromHierarchy();
            }

            _items.Clear();
            for (int i = 0; i < _itemKeys.Length; i++)
            {
                int index = i;
                var item = new Label();
                item.AddToClassList(ItemClass);
                item.AddToClassList("vv-text-button");
                new KeyedText(item, value => item.text = value) { Key = _itemKeys[i] };
                item.RegisterCallback<ClickEvent>(_ =>
                {
                    if (enabledInHierarchy)
                    {
                        value = index;
                    }
                });
                _items.Add(item);
                Add(item);
            }

            SetValueWithoutNotify(_value);
        }

        public void SetValueWithoutNotify(int newValue)
        {
            // UXML may set the value before the items; keep it until there are items to clamp against.
            _value = _items.Count == 0 ? newValue : Clamp(newValue, _items.Count);
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].EnableInClassList(SelectedClass, i == _value);
            }
        }

        private void OnNavigationMove(NavigationMoveEvent evt)
        {
            int step = evt.direction == NavigationMoveEvent.Direction.Left ? -1
                : evt.direction == NavigationMoveEvent.Direction.Right ? 1
                : 0;
            if (step == 0)
            {
                return;
            }

            value = Clamp(_value + step, _items.Count);
            evt.StopPropagation();

            // StopPropagation alone leaves Unity's arrow-key focus navigation running, which would move
            // focus to the next row while the value changes.
            focusController?.IgnoreEvent(evt);
        }
    }
}
