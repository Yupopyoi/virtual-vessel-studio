using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// Underlined tabs (components.md 3.8). The value is the selected index. The tabs scroll sideways
    /// when there is not enough width, and left and right keys move the selection when focused.
    /// </summary>
    [UxmlElement]
    public partial class TabBar : VisualElement, INotifyValueChanged<int>
    {
        public const string UssClass = "vv-tabs";

        public const string TabClass = UssClass + "__tab";

        public const string SelectedClass = TabClass + "--selected";

        private readonly ScrollView _scroll;
        private readonly List<Label> _tabs = new List<Label>();
        private string[] _tabKeys = Array.Empty<string>();
        private int _value;

        public TabBar()
        {
            AddToClassList(UssClass);
            _scroll = new ScrollView(ScrollViewMode.Horizontal)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
            };
            _scroll.AddToClassList(UssClass + "__scroll");
            hierarchy.Add(_scroll);
            FocusRing.Attach(this);
            RegisterCallback<NavigationMoveEvent>(OnNavigationMove);
        }

        public TabBar(params string[] tabKeys)
            : this()
        {
            SetTabs(tabKeys);
        }

        /// <summary>Tab text keys, comma-separated.</summary>
        [UxmlAttribute("tab-keys")]
        public string TabKeys
        {
            get => string.Join(",", _tabKeys);
            set => SetTabs(Segmented.SplitKeys(value));
        }

        [UxmlAttribute("value")]
        public int value
        {
            get => _value;
            set
            {
                int next = _tabs.Count == 0 ? value : Segmented.Clamp(value, _tabs.Count);
                if (_value == next)
                {
                    return;
                }

                using (ChangeEvent<int> evt = ChangeEvent<int>.GetPooled(_value, next))
                {
                    evt.target = this;
                    SetValueWithoutNotify(next);
                    SendEvent(evt);
                }
            }
        }

        public void SetTabs(params string[] tabKeys)
        {
            _tabKeys = tabKeys ?? Array.Empty<string>();
            foreach (Label tab in _tabs)
            {
                tab.RemoveFromHierarchy();
            }

            _tabs.Clear();
            for (int i = 0; i < _tabKeys.Length; i++)
            {
                int index = i;
                var tab = new Label();
                tab.AddToClassList(TabClass);
                tab.AddToClassList("vv-text-body");
                new KeyedText(tab, text => tab.text = text) { Key = _tabKeys[i] };
                tab.RegisterCallback<ClickEvent>(_ =>
                {
                    if (enabledInHierarchy)
                    {
                        value = index;
                    }
                });
                _tabs.Add(tab);
                _scroll.Add(tab);
            }

            SetValueWithoutNotify(_value);
        }

        public void SetValueWithoutNotify(int newValue)
        {
            _value = _tabs.Count == 0 ? newValue : Segmented.Clamp(newValue, _tabs.Count);
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].EnableInClassList(SelectedClass, i == _value);
            }

            if (_value >= 0 && _value < _tabs.Count && _tabs[_value].panel != null)
            {
                _scroll.ScrollTo(_tabs[_value]);
            }
        }

        private void OnNavigationMove(NavigationMoveEvent evt)
        {
            int step = evt.direction == NavigationMoveEvent.Direction.Left ? -1
                : evt.direction == NavigationMoveEvent.Direction.Right ? 1
                : 0;
            if (step == 0 || _tabs.Count == 0)
            {
                return;
            }

            value = Segmented.Clamp(_value + step, _tabs.Count);
            evt.StopPropagation();
        }
    }
}
