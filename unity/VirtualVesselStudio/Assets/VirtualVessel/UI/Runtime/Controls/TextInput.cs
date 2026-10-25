using UnityEngine.UIElements;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Tooltips;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// The error line under an input: a warning shape and a message that says what is wrong and what to
    /// do (components.md 3.7). Hidden while there is no error.
    /// </summary>
    internal sealed class FieldError
    {
        public const string ErrorClass = "vv-field--error";

        private readonly VisualElement _owner;
        private readonly VisualElement _row;
        private readonly KeyedText _text;

        public FieldError(VisualElement owner)
        {
            _owner = owner;
            _row = new VisualElement();
            _row.AddToClassList("vv-field__error");
            _row.Add(new Icon(IconName.Warning));
            Label label = QuietText.Of(new Label());
            label.AddToClassList("vv-text-caption");
            _row.Add(label);
            _text = new KeyedText(owner, value => label.text = value);
            Key = null;
        }

        public VisualElement Element => _row;

        public string Key
        {
            get => _text.Key;
            set
            {
                _text.Key = value;
                bool show = !string.IsNullOrEmpty(value);
                _row.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                _owner.EnableInClassList(ErrorClass, show);
            }
        }
    }

    /// <summary>A text field with its label and an error line (components.md 3.7).</summary>
    [UxmlElement]
    public partial class TextInput : VisualElement, INotifyValueChanged<string>, ITooltipSource
    {
        public const string UssClass = "vv-text-input";

        private readonly KeyedText _label;
        private readonly KeyedText _placeholder;
        private readonly TextField _field;
        private readonly FieldError _error;

        public TextInput()
        {
            AddToClassList(UssClass);
            AddToClassList("vv-field");
            Label label = QuietText.Of(new Label());
            label.AddToClassList("vv-field__label");
            label.AddToClassList("vv-text-caption");
            Add(label);
            _field = new TextField();
            _field.AddToClassList("vv-field__input");
            _field.AddToClassList("vv-text-body");
            Add(_field);
            _error = new FieldError(this);
            Add(_error.Element);

            _label = new KeyedText(this, value =>
            {
                label.text = value;
                label.style.display = string.IsNullOrEmpty(value) ? DisplayStyle.None : DisplayStyle.Flex;
            });
            _placeholder = new KeyedText(this, value => _field.textEdition.placeholder = value);

            // The example goes away as the field is focused. Left in view, the first characters typed were
            // shown as the example, and the caret stood where its text ended (the user's report, 2026-10-25).
            _field.textEdition.hidePlaceholderOnFocus = true;

            // Enter does nothing in these one-line fields, and Unity's field moved the caret to the start on
            // it. Japanese input commits a word with Enter, so what was typed next went in near the start
            // ("じゃんけん" became "じんけんゃ", the user's report, 2026-10-25). Enter and the submit it
            // becomes never reach the field.
            _field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (IsEnter(evt.keyCode, evt.character))
                {
                    evt.StopImmediatePropagation();
                }
            }, TrickleDown.TrickleDown);
            _field.RegisterCallback<NavigationSubmitEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);

            // Re-send the inner field's changes from this element, so bindings see one field.
            _field.RegisterValueChangedCallback(evt =>
            {
                evt.StopPropagation();
                KeepCaretAfterEdit(evt.previousValue, evt.newValue);
                using (ChangeEvent<string> outer = ChangeEvent<string>.GetPooled(evt.previousValue, evt.newValue))
                {
                    outer.target = this;
                    SendEvent(outer);
                }
            });
        }

        [UxmlAttribute("label-key")]
        public string LabelKey
        {
            get => _label.Key;
            set => _label.Key = value;
        }

        [UxmlAttribute("placeholder-key")]
        public string PlaceholderKey
        {
            get => _placeholder.Key;
            set => _placeholder.Key = value;
        }

        [UxmlAttribute("tooltip-key")]
        public string TooltipKey { get; set; }

        string ITooltipSource.CurrentTooltipKey => TooltipKey;

        string ITooltipSource.Shortcut => null;

        /// <summary>The message key shown under the field, or null for no error.</summary>
        public string ErrorKey
        {
            get => _error.Key;
            set => _error.Key = value;
        }

        [UxmlAttribute("max-length")]
        public int MaxLength
        {
            get => _field.maxLength;
            set => _field.maxLength = value;
        }

        [UxmlAttribute("value")]
        public string value
        {
            get => _field.value;
            set => _field.value = value;
        }

        /// <summary>
        /// Where the caret may stand after an edit from <paramref name="before"/> to <paramref name="after"/>:
        /// right after the text put in, from <paramref name="lo"/> to <paramref name="hi"/> when the same
        /// characters around it leave the place open (typing "a" into "aa"). False for an edit that only took
        /// text away, or no edit. Pure.
        /// </summary>
        public static bool TryCaretAfterEdit(string before, string after, out int lo, out int hi)
        {
            before ??= string.Empty;
            after ??= string.Empty;
            int shorter = System.Math.Min(before.Length, after.Length);
            int prefix = 0;
            while (prefix < shorter && before[prefix] == after[prefix])
            {
                prefix++;
            }

            int suffix = 0;
            while (suffix < shorter && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
            {
                suffix++;
            }

            int grown = after.Length - before.Length;
            if (grown > 0 && prefix + suffix >= before.Length)
            {
                // Text put in without taking any out: anywhere the characters on both sides still match.
                lo = before.Length - System.Math.Min(suffix, before.Length) + grown;
                hi = System.Math.Min(prefix, before.Length) + grown;
                return lo <= hi;
            }

            // Text replaced, as Japanese input turns its reading into the chosen word: after the new text.
            suffix = System.Math.Min(suffix, shorter - prefix);
            lo = hi = after.Length - suffix;
            return lo > prefix;
        }

        /// <summary>Whether a key press is Enter, from the main keys or the keypad, or the line break it types. Pure.</summary>
        public static bool IsEnter(UnityEngine.KeyCode key, char character)
        {
            return key == UnityEngine.KeyCode.Return || key == UnityEngine.KeyCode.KeypadEnter || character == '\n' || character == '\r';
        }

        /// <summary>
        /// With Japanese input, Unity's field (with the Input System) left the caret one character short of
        /// each word it committed, so what was typed next went in before the word's last character
        /// ("入力欄が" became "入欄が…力", the user's video, 2026-10-25). After an edit, once Unity has placed the
        /// caret, it is moved to just after the text put in if it stands anywhere else.
        /// </summary>
        private void KeepCaretAfterEdit(string before, string after)
        {
            if (!TryCaretAfterEdit(before, after, out int lo, out int hi))
            {
                return;
            }

            _field.schedule.Execute(() =>
            {
                ITextSelection selection = _field.textSelection;
                if (_field.value != after || selection.cursorIndex != selection.selectIndex)
                {
                    return;
                }

                int caret = selection.cursorIndex;
                if (caret < lo || caret > hi)
                {
                    int place = caret < lo ? lo : hi;
                    selection.cursorIndex = place;
                    selection.selectIndex = place;
                }
            });
        }

        public void SetValueWithoutNotify(string newValue)
        {
            _field.SetValueWithoutNotify(newValue);
        }
    }
}
