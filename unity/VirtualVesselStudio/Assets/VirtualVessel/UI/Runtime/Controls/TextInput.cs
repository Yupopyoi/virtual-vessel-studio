using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Localization;
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
            Label hint = QuietText.Of(new Label());
            hint.AddToClassList("vv-field__hint");
            hint.AddToClassList("vv-text-caption");
            Add(hint);
            new KeyedText(this, value => hint.text = value).Key = CommonStrings.EnglishOnly;
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

            // English only for now: Unity's field (with the Input System) left the caret one character short of
            // each word Japanese input committed, so what was typed next went in before the word's last
            // character ("入力欄が" became "入欄が…力", the user's video, 2026-10-25), and moving the caret back
            // after the edit did not help. Japanese input is turned off while the field is focused, once Unity
            // has turned it on, and characters it could still bring in are dropped (#74).
            _field.RegisterCallback<FocusInEvent>(_ =>
            {
                TurnOffJapaneseInput();
                _field.schedule.Execute(TurnOffJapaneseInput);
            });

            // Re-send what is typed, English only, from this element, so bindings see one field.
            _field.RegisterValueChangedCallback(evt =>
            {
                evt.StopPropagation();
                string kept = KeepTypedEnglish(evt.previousValue, evt.newValue, out int caret);
                if (kept != evt.newValue)
                {
                    _field.SetValueWithoutNotify(kept);
                    _field.textSelection.cursorIndex = caret;
                    _field.textSelection.selectIndex = caret;
                    if (kept == evt.previousValue)
                    {
                        return;
                    }
                }

                SendChange(evt.previousValue, kept);
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
            set
            {
                // Set by code, such as a saved description: kept as it is, as only typing is English only.
                string previous = _field.value;
                _field.SetValueWithoutNotify(value);
                if (!string.Equals(previous, _field.value, System.StringComparison.Ordinal))
                {
                    SendChange(previous, _field.value);
                }
            }
        }

        /// <summary>
        /// <paramref name="after"/>, an edit of <paramref name="before"/>, without the characters it put in
        /// that are not printable ASCII (English letters, digits, symbols and spaces), with the caret just
        /// after what is left of them. Text already there is kept as it is. Pure.
        /// </summary>
        public static string KeepTypedEnglish(string before, string after, out int caret)
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
            while (suffix < shorter - prefix && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
            {
                suffix++;
            }

            var kept = new System.Text.StringBuilder(after.Length);
            kept.Append(after, 0, prefix);
            for (int i = prefix; i < after.Length - suffix; i++)
            {
                if (after[i] >= ' ' && after[i] <= '~')
                {
                    kept.Append(after[i]);
                }
            }

            caret = kept.Length;
            kept.Append(after, after.Length - suffix, suffix);
            return kept.ToString();
        }

        /// <summary>Whether a key press is Enter, from the main keys or the keypad, or the line break it types. Pure.</summary>
        public static bool IsEnter(UnityEngine.KeyCode key, char character)
        {
            return key == UnityEngine.KeyCode.Return || key == UnityEngine.KeyCode.KeypadEnter || character == '\n' || character == '\r';
        }

        private void SendChange(string previous, string next)
        {
            using (ChangeEvent<string> outer = ChangeEvent<string>.GetPooled(previous, next))
            {
                outer.target = this;
                SendEvent(outer);
            }
        }

        private static void TurnOffJapaneseInput()
        {
            Keyboard.current?.SetIMEEnabled(false);
        }

        public void SetValueWithoutNotify(string newValue)
        {
            _field.SetValueWithoutNotify(newValue);
        }
    }
}
