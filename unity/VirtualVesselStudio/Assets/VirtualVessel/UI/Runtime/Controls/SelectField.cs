using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Theme;
using VirtualVessel.UI.Tooltips;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// One option of a <see cref="SelectField"/>: either a string table key, or text that is data and
    /// not translated, such as a device name.
    /// </summary>
    public readonly struct SelectChoice
    {
        private SelectChoice(string id, string textKey, string text)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            TextKey = textKey;
            Text = text;
        }

        public string Id { get; }

        public string TextKey { get; }

        public string Text { get; }

        public static SelectChoice FromKey(string id, string textKey)
        {
            return new SelectChoice(id, textKey ?? throw new ArgumentNullException(nameof(textKey)), null);
        }

        /// <summary>For names that come from data, such as an audio device; they are shown as they are.</summary>
        public static SelectChoice FromText(string id, string text)
        {
            return new SelectChoice(id, null, text ?? string.Empty);
        }

        /// <summary>The text to show. Pure.</summary>
        public string Resolve(ILocalizer localizer)
        {
            return TextKey == null ? Text : KeyedText.Resolve(localizer, TextKey);
        }
    }

    /// <summary>
    /// A dropdown with its label (components.md 3.6). The value is the id of the chosen option.
    /// The list opens on the nearest themed root so that it sits above other parts.
    /// </summary>
    [UxmlElement]
    public partial class SelectField : VisualElement, INotifyValueChanged<string>, ITooltipSource
    {
        public const string UssClass = "vv-select";

        public const string MenuClass = UssClass + "__menu";

        public const string OptionClass = UssClass + "__option";

        public const string HighlightClass = OptionClass + "--highlight";

        // Keeps an open list off the window's edge, as the popup menu does.
        private const float MenuEdgeMargin = 8f;

        private readonly KeyedText _label;
        private readonly VisualElement _box;
        private readonly Label _current;
        private readonly VisualElement _sizer;
        private readonly List<SelectChoice> _choices = new List<SelectChoice>();
        private VisualElement _menu;
        private EventCallback<PointerDownEvent> _outsideClick;
        private Action<LocaleInfo> _onLocaleChanged;
        private ILocalizer _menuLocalizer;
        private int _highlight = -1;
        private string _value;

        public SelectField()
        {
            AddToClassList(UssClass);
            Label label = QuietText.Of(new Label());
            label.AddToClassList(UssClass + "__label");
            label.AddToClassList("vv-text-caption");
            Add(label);

            _box = new VisualElement();
            _box.AddToClassList(UssClass + "__box");
            // Choices can be data, such as device names or users' descriptions; never parse them as rich text.
            _current = QuietText.Of(new Label { enableRichText = false });
            _current.AddToClassList(UssClass + "__current");
            _current.AddToClassList("vv-text-body");

            // Every choice, invisible and zero-high, under the current text: the field is as wide as its
            // longest choice whatever is selected, so it does not jump, and the list opens no wider.
            var text = new VisualElement();
            text.AddToClassList(UssClass + "__text");
            _sizer = new VisualElement { pickingMode = PickingMode.Ignore };
            _sizer.AddToClassList(UssClass + "__sizer");
            text.Add(_current);
            text.Add(_sizer);
            var chevron = new Icon(IconName.ChevronDown);
            chevron.AddToClassList(UssClass + "__chevron");
            _box.Add(text);
            _box.Add(chevron);
            Add(_box);
            FocusRing.Attach(_box);

            _label = new KeyedText(this, value =>
            {
                label.text = value;
                label.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
            });

            // The current text follows the language like any keyed text.
            PanelLifetime.WhileAttached(this, services =>
            {
                RefreshCurrent();
                ILocalizer localizer = services?.Localizer;
                if (localizer == null)
                {
                    return DisposeAction.None;
                }

                Action<LocaleInfo> onChanged = _ => RefreshCurrent();
                localizer.LocaleChanged += onChanged;
                return new DisposeAction(() => localizer.LocaleChanged -= onChanged);
            });

            _box.RegisterCallback<ClickEvent>(_ => Toggle());
            _box.RegisterCallback<NavigationSubmitEvent>(_ =>
            {
                if (_menu != null && _highlight >= 0)
                {
                    Choose(_highlight);
                }
                else
                {
                    Toggle();
                }
            });
            _box.RegisterCallback<NavigationMoveEvent>(OnNavigationMove);
            _box.RegisterCallback<NavigationCancelEvent>(_ => Close());
            RegisterCallback<DetachFromPanelEvent>(_ => Close());
        }

        [UxmlAttribute("label-key")]
        public string LabelKey
        {
            get => _label.Key;
            set => _label.Key = value;
        }

        [UxmlAttribute("tooltip-key")]
        public string TooltipKey { get; set; }

        string ITooltipSource.CurrentTooltipKey => TooltipKey;

        string ITooltipSource.Shortcut => null;

        /// <summary>Options given as text keys, comma-separated; each key is also the option id.</summary>
        [UxmlAttribute("choice-keys")]
        public string ChoiceKeys
        {
            get => string.Join(",", ChoiceIds());
            set
            {
                var choices = new List<SelectChoice>();
                foreach (string key in Segmented.SplitKeys(value))
                {
                    choices.Add(SelectChoice.FromKey(key, key));
                }

                SetChoices(choices);
            }
        }

        public IReadOnlyList<SelectChoice> Choices => _choices;

        public bool IsOpen => _menu != null;

        [UxmlAttribute("value")]
        public string value
        {
            get => _value;
            set
            {
                if (string.Equals(_value, value, StringComparison.Ordinal))
                {
                    return;
                }

                using (ChangeEvent<string> evt = ChangeEvent<string>.GetPooled(_value, value))
                {
                    evt.target = this;
                    SetValueWithoutNotify(value);
                    SendEvent(evt);
                }
            }
        }

        public string DisplayedText => _current.text;

        /// <summary>The index of <paramref name="id"/>, or -1. Pure.</summary>
        public static int IndexOf(IReadOnlyList<SelectChoice> choices, string id)
        {
            for (int i = 0; i < choices.Count; i++)
            {
                if (string.Equals(choices[i].Id, id, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Moves the highlight by <paramref name="step"/>, stopping at the ends. Pure.</summary>
        public static int MoveHighlight(int current, int step, int count)
        {
            if (count == 0)
            {
                return -1;
            }

            return Mathf.Clamp(current < 0 ? (step > 0 ? 0 : count - 1) : current + step, 0, count - 1);
        }

        public void SetChoices(IEnumerable<SelectChoice> choices)
        {
            Close();
            _choices.Clear();
            if (choices != null)
            {
                _choices.AddRange(choices);
            }

            RefreshCurrent();
        }

        public void SetValueWithoutNotify(string newValue)
        {
            _value = newValue;
            RefreshCurrent();
        }

        public void Open()
        {
            if (_menu != null || !enabledInHierarchy || _choices.Count == 0 || panel == null)
            {
                return;
            }

            VisualElement host = MenuHost();
            _menuLocalizer = UiServices.Find(this)?.Localizer;
            _menu = new VisualElement();
            _menu.AddToClassList(MenuClass);
            for (int i = 0; i < _choices.Count; i++)
            {
                int index = i;
                var option = new Label(_choices[i].Resolve(_menuLocalizer)) { enableRichText = false };
                option.AddToClassList(OptionClass);
                option.AddToClassList("vv-text-body");
                option.EnableInClassList(OptionClass + "--selected", string.Equals(_choices[i].Id, _value, StringComparison.Ordinal));
                option.RegisterCallback<PointerDownEvent>(evt =>
                {
                    Choose(index);
                    evt.StopPropagation();
                });
                option.RegisterCallback<PointerEnterEvent>(_ => Highlight(index));
                _menu.Add(option);
            }

            // Below the field, as wide as it; above it when there is no room below, and shifted left
            // when it would pass the right edge of the window.
            Rect field = host.WorldToLocal(_box.worldBound);
            _menu.style.left = field.xMin;
            _menu.style.minWidth = field.width;
            _menu.style.top = field.yMax + 4f;
            host.Add(_menu);
            _menu.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (_menu == null)
                {
                    return;
                }

                if (field.yMax + 4f + _menu.layout.height > host.layout.height && field.yMin - 4f - _menu.layout.height >= 0f)
                {
                    _menu.style.top = field.yMin - 4f - _menu.layout.height;
                }

                _menu.style.left = MenuLeft(field, _menu.layout.width, host.layout.width, MenuEdgeMargin);
            });

            _highlight = IndexOf(_choices, _value);
            Highlight(_highlight);
            _outsideClick = evt =>
            {
                if (evt.target is VisualElement target && !_menu.Contains(target) && !_box.Contains(target))
                {
                    Close();
                }
            };
            host.RegisterCallback(_outsideClick, TrickleDown.TrickleDown);

            // A language change while open would leave stale text; close instead.
            if (_menuLocalizer != null)
            {
                _onLocaleChanged = _ => Close();
                _menuLocalizer.LocaleChanged += _onLocaleChanged;
            }
        }

        /// <summary>
        /// The left edge of a list <paramref name="menuWidth"/> wide under <paramref name="field"/>: at the
        /// field, or right-aligned with it when that would pass the window's edge, never past the left. Pure.
        /// </summary>
        public static float MenuLeft(Rect field, float menuWidth, float hostWidth, float margin)
        {
            float left = field.xMin + menuWidth > hostWidth - margin ? field.xMax - menuWidth : field.xMin;
            return Mathf.Max(margin, Mathf.Min(left, hostWidth - margin - menuWidth));
        }

        public void Close()
        {
            if (_menu == null)
            {
                return;
            }

            VisualElement host = _menu.hierarchy.parent;
            host?.UnregisterCallback(_outsideClick, TrickleDown.TrickleDown);
            if (_menuLocalizer != null && _onLocaleChanged != null)
            {
                _menuLocalizer.LocaleChanged -= _onLocaleChanged;
            }

            _menu.RemoveFromHierarchy();
            _menu = null;
            _outsideClick = null;
            _onLocaleChanged = null;
            _menuLocalizer = null;
            _highlight = -1;
        }

        /// <summary>Chooses the option at <paramref name="index"/> and closes the list.</summary>
        public void Choose(int index)
        {
            if (index >= 0 && index < _choices.Count)
            {
                value = _choices[index].Id;
            }

            Close();
            _box.Focus();
        }

        private IEnumerable<string> ChoiceIds()
        {
            foreach (SelectChoice choice in _choices)
            {
                yield return choice.Id;
            }
        }

        private void Toggle()
        {
            if (_menu == null)
            {
                Open();
            }
            else
            {
                Close();
            }
        }

        private void OnNavigationMove(NavigationMoveEvent evt)
        {
            int step = evt.direction == NavigationMoveEvent.Direction.Down ? 1
                : evt.direction == NavigationMoveEvent.Direction.Up ? -1
                : 0;
            if (step == 0)
            {
                return;
            }

            if (_menu == null)
            {
                // Without the list open, up and down change the value directly.
                int next = MoveHighlight(IndexOf(_choices, _value), step, _choices.Count);
                if (next >= 0)
                {
                    value = _choices[next].Id;
                }
            }
            else
            {
                Highlight(MoveHighlight(_highlight, step, _choices.Count));
            }

            evt.StopPropagation();

            // StopPropagation alone leaves Unity's arrow-key focus navigation running, which would move
            // focus to the next row while the value changes.
            focusController?.IgnoreEvent(evt);
        }

        private void Highlight(int index)
        {
            _highlight = index;
            if (_menu == null)
            {
                return;
            }

            for (int i = 0; i < _menu.childCount; i++)
            {
                _menu[i].EnableInClassList(HighlightClass, i == index);
            }
        }

        private void RefreshCurrent()
        {
            ILocalizer localizer = UiServices.Find(this)?.Localizer;
            int index = IndexOf(_choices, _value);
            _current.text = index < 0 ? string.Empty : _choices[index].Resolve(localizer);

            // Rebuilt with the current texts, so the width follows the language too.
            _sizer.Clear();
            foreach (SelectChoice choice in _choices)
            {
                var sizing = new Label(choice.Resolve(localizer)) { enableRichText = false };
                sizing.AddToClassList(UssClass + "__current");
                sizing.AddToClassList("vv-text-body");
                _sizer.Add(sizing);
            }
        }

        private VisualElement MenuHost()
        {
            for (VisualElement current = this; current != null; current = current.hierarchy.parent)
            {
                if (current.ClassListContains(ThemeService.RootClass))
                {
                    return current;
                }
            }

            return panel.visualTree;
        }
    }
}
