using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
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

        // Longer than a frame, so Unity has put in all of a commit's characters and left its caret. The text
        // is put right sooner, when the last character arrives; this also covers a commit that changes nothing.
        private const long CommitSettleMs = 30;

        private readonly KeyedText _label;
        private readonly KeyedText _placeholder;
        private readonly TextField _field;
        private readonly FieldError _error;
        private readonly ImeCommit _imeCommit = new ImeCommit();

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

            // Japanese input is followed only while this field has the focus (ImeCommit).
            _field.RegisterCallback<FocusInEvent>(_ => FollowKeyboard(true));
            _field.RegisterCallback<FocusOutEvent>(_ => FollowKeyboard(false));
            _field.RegisterCallback<DetachFromPanelEvent>(_ => FollowKeyboard(false));

            // Re-send what is typed from this element, so bindings see one field.
            _field.RegisterValueChangedCallback(evt =>
            {
                evt.StopPropagation();
                string next = evt.newValue;
                if (_imeCommit.Changed())
                {
                    (string text, int caret) = _imeCommit.Result;
                    PutText(text, caret);
                    next = text;
                }

                SendChange(evt.previousValue, next);
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
                string previous = _field.value;
                _field.SetValueWithoutNotify(value);
                if (!string.Equals(previous, _field.value, System.StringComparison.Ordinal))
                {
                    SendChange(previous, _field.value);
                }
            }
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

        private void FollowKeyboard(bool follow)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            keyboard.onIMECompositionChange -= OnComposition;
            keyboard.onTextInput -= OnKeyboardText;
            if (follow)
            {
                keyboard.onIMECompositionChange += OnComposition;
                keyboard.onTextInput += OnKeyboardText;
            }
            else
            {
                _imeCommit.Cancel();
            }
        }

        private void OnComposition(IMECompositionString composition)
        {
            if (_imeCommit.Composition(composition.ToString(), _field.value ?? string.Empty, _field.textSelection.cursorIndex, _field.textSelection.selectIndex))
            {
                // Unity puts the characters in later in this frame.
                _field.schedule.Execute(FinishCommit).StartingIn(CommitSettleMs);
            }
        }

        private void OnKeyboardText(char character)
        {
            _imeCommit.Text(character);
        }

        private void PutText(string text, int caret)
        {
            _field.SetValueWithoutNotify(text);
            _field.textSelection.cursorIndex = caret;
            _field.textSelection.selectIndex = caret;
        }

        private void FinishCommit()
        {
            if (!_imeCommit.TryFinish(out (string Text, int Caret) result))
            {
                return;
            }

            string previous = _field.value;
            PutText(result.Text, result.Caret);
            if (!string.Equals(previous, result.Text, System.StringComparison.Ordinal))
            {
                SendChange(previous, result.Text);
            }
        }

        public void SetValueWithoutNotify(string newValue)
        {
            _field.SetValueWithoutNotify(newValue);
        }
    }
}
