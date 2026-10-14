using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Theme;

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
    public partial class SelectField : VisualElement, INotifyValueChanged<string>
    {
        public const string UssClass = "vv-select";

        public const string MenuClass = UssClass + "__menu";

        public const string OptionClass = UssClass + "__option";

        public const string HighlightClass = OptionClass + "--highlight";

        private readonly KeyedText _label;
        private readonly VisualElement _box;
        private readonly Label _current;
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
            _current = QuietText.Of(new Label());
            _current.AddToClassList(UssClass + "__current");
            _current.AddToClassList("vv-text-body");
            var chevron = new Icon(IconName.ChevronDown);
            chevron.AddToClassList(UssClass + "__chevron");
            _box.Add(_current);
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
                var option = new Label(_choices[i].Resolve(_menuLocalizer));
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

            // Below the field, as wide as it; above it when there is no room below.
            Rect field = host.WorldToLocal(_box.worldBound);
            _menu.style.left = field.xMin;
            _menu.style.minWidth = field.width;
            _menu.style.top = field.yMax + 4f;
            host.Add(_menu);
            _menu.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (_menu != null && field.yMax + 4f + _menu.layout.height > host.layout.height && field.yMin - 4f - _menu.layout.height >= 0f)
                {
                    _menu.style.top = field.yMin - 4f - _menu.layout.height;
                }
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
            int index = IndexOf(_choices, _value);
            _current.text = index < 0 ? string.Empty : _choices[index].Resolve(UiServices.Find(this)?.Localizer);
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
