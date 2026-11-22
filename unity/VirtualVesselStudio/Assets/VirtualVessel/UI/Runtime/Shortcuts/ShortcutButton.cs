using System;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Shortcuts
{
    /// <summary>String table keys of the shortcut button (docs/&lt;lang&gt;/ui/screens/setup.md 14).</summary>
    [StringKeys]
    public static class ShortcutStrings
    {
        public const string Column = "setup.se.shortcut";
        public const string Help = "setup.se.shortcut.help";
        public const string None = "setup.se.shortcut.none";
        public const string Press = "setup.se.shortcut.press";
        public const string NeedModifier = "setup.se.shortcut.need-modifier";
        public const string Taken = "setup.se.shortcut.taken";
        public const string HeldElsewhere = "setup.se.shortcut.held-elsewhere";
    }

    /// <summary>
    /// Shows a sound effect's keys and takes new ones (BGM and SE detailed design 8.3; setup.md 14): pressed, it waits
    /// for keys with Ctrl, Alt, or Shift; Esc cancels and Delete or Backspace removes them. While it waits, the global
    /// keys are paused, so the keys pressed reach it rather than playing a sound.
    /// </summary>
    public sealed class ShortcutButton : VisualElement
    {
        public const string UssClass = "vv-shortcut";
        public const string WaitingClass = UssClass + "--waiting";

        private readonly IGlobalShortcuts _keys;
        private string _shortcut;

        public ShortcutButton(string shortcut, IGlobalShortcuts keys)
        {
            _keys = keys;
            _shortcut = shortcut;
            AddToClassList(UssClass);
            Button = Ui.Button(null).WithTooltip(ShortcutStrings.Help);
            Button.AddToClassList("vv-mono");
            Button.clicked += Toggle;
            Button.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            Button.RegisterCallback<FocusOutEvent>(_ => Stop());
            Add(Button);
            RegisterCallback<DetachFromPanelEvent>(_ => Stop());
            Show();
        }

        /// <summary>The new keys as text, or an empty string when they were removed.</summary>
        public event Action<string> Chosen;

        public ActionButton Button { get; }

        public bool Waiting { get; private set; }

        /// <summary>
        /// What a key press does while waiting: keys to choose, removing them, cancelling, or nothing yet (a modifier
        /// alone). <paramref name="needsModifier"/> says the key was one a shortcut can have, pressed alone. Pure.
        /// </summary>
        public static (ShortcutPress Press, KeyChord? Chord, bool NeedsModifier) Read(KeyCode key, bool ctrl, bool alt, bool shift)
        {
            if (key == KeyCode.Escape)
            {
                return (ShortcutPress.Cancel, null, false);
            }

            if ((key == KeyCode.Delete || key == KeyCode.Backspace) && !ctrl && !alt && !shift)
            {
                return (ShortcutPress.Remove, null, false);
            }

            KeyChord? chord = KeyChord.FromKey(key, ctrl, alt, shift);
            if (chord.HasValue)
            {
                return (ShortcutPress.Choose, chord, false);
            }

            bool offered = KeyChord.FromKey(key, true, false, false).HasValue;
            return (ShortcutPress.Wait, null, offered && !ctrl && !alt && !shift);
        }

        /// <summary>Shows other keys, as after the library changed.</summary>
        public void SetShortcut(string shortcut)
        {
            _shortcut = shortcut;
            Show();
        }

        /// <summary>Waits for keys, as a press of the button does.</summary>
        public void Start()
        {
            if (Waiting)
            {
                return;
            }

            Waiting = true;
            if (_keys != null)
            {
                _keys.Paused = true;
            }

            AddToClassList(WaitingClass);
            Show();
        }

        /// <summary>Stops waiting without a change.</summary>
        public void Stop()
        {
            if (!Waiting)
            {
                return;
            }

            Waiting = false;
            if (_keys != null)
            {
                _keys.Paused = false;
            }

            RemoveFromClassList(WaitingClass);
            Button.TooltipKey = ShortcutStrings.Help;
            Show();
        }

        /// <summary>Takes a key press while waiting, as the keyboard does; for tests too.</summary>
        public void Take(KeyCode key, bool ctrl, bool alt, bool shift)
        {
            if (!Waiting)
            {
                return;
            }

            (ShortcutPress press, KeyChord? chord, bool needsModifier) = Read(key, ctrl, alt, shift);
            switch (press)
            {
                case ShortcutPress.Cancel:
                    Stop();
                    break;
                case ShortcutPress.Remove:
                    Stop();
                    Chosen?.Invoke(string.Empty);
                    break;
                case ShortcutPress.Choose:
                    Stop();
                    Chosen?.Invoke(chord.Value.ToString());
                    break;
                default:
                    if (needsModifier)
                    {
                        Button.TooltipKey = ShortcutStrings.NeedModifier;
                    }

                    break;
            }
        }

        private void Toggle()
        {
            if (Waiting)
            {
                Stop();
            }
            else
            {
                Start();
            }
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (!Waiting || evt.keyCode == KeyCode.None)
            {
                return;
            }

            // Every key goes to the waiting button, so Enter or Tab do not press or leave it by accident.
            evt.StopPropagation();
            focusController?.IgnoreEvent(evt);
            Take(evt.keyCode, evt.ctrlKey, evt.altKey, evt.shiftKey);
        }

        // Keys are shown as they are, the same in every language; the words come from the string tables. One is
        // cleared before the other is set, so a change of language does not write over the keys.
        private void Show()
        {
            KeyChord? chord = Waiting ? null : KeyChord.Parse(_shortcut);
            if (chord.HasValue)
            {
                Button.TextKey = null;
                Button.PlainText = chord.Value.ToString();
            }
            else
            {
                string key = Waiting ? ShortcutStrings.Press : ShortcutStrings.None;
                if (Button.PlainText != null)
                {
                    // The words are written again by the key only when it changes, so it is cleared first.
                    Button.TextKey = null;
                    Button.PlainText = null;
                }

                Button.TextKey = key;
            }
        }
    }

    /// <summary>What a key press does to a waiting <see cref="ShortcutButton"/>.</summary>
    public enum ShortcutPress
    {
        Wait,
        Choose,
        Remove,
        Cancel,
    }
}
