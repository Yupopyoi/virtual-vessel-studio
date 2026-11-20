using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualVessel.UI.Shortcuts
{
    /// <summary>The modifier keys of a <see cref="KeyChord"/>, with Windows' RegisterHotKey values.</summary>
    [Flags]
    public enum KeyModifiers
    {
        None = 0,
        Alt = 1,
        Ctrl = 2,
        Shift = 4,
    }

    /// <summary>
    /// A key with at least one modifier, such as Ctrl+Alt+1, that plays a sound effect from any app (BGM and SE
    /// detailed design 8.3). A modifier is required so the keys a game uses alone are not taken from it. Written as
    /// text in the order Ctrl, Alt, Shift, then the key, the same in every language.
    /// </summary>
    public readonly struct KeyChord : IEquatable<KeyChord>
    {
        private static readonly Dictionary<string, int> s_keysByName = KeysByName();

        private KeyChord(KeyModifiers modifiers, int virtualKey, string keyName)
        {
            Modifiers = modifiers;
            VirtualKey = virtualKey;
            KeyName = keyName;
        }

        public KeyModifiers Modifiers { get; }

        /// <summary>The Windows virtual-key code.</summary>
        public int VirtualKey { get; }

        public string KeyName { get; }

        /// <summary>
        /// The chord a key press makes, or null when the key cannot be a shortcut's (a modifier alone, or a key not
        /// offered) or no modifier is held. Pure.
        /// </summary>
        public static KeyChord? FromKey(KeyCode key, bool ctrl, bool alt, bool shift)
        {
            KeyModifiers modifiers = (ctrl ? KeyModifiers.Ctrl : 0) | (alt ? KeyModifiers.Alt : 0) | (shift ? KeyModifiers.Shift : 0);
            string name = NameOf(key);
            return modifiers == KeyModifiers.None || name == null ? (KeyChord?)null : new KeyChord(modifiers, s_keysByName[name], name);
        }

        /// <summary>The chord written as text, such as "Ctrl+Alt+1", or null when it is not one. Pure.</summary>
        public static KeyChord? Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            KeyModifiers modifiers = KeyModifiers.None;
            string[] parts = text.Split('+');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                KeyModifiers modifier = ModifierOf(parts[i].Trim());
                if (modifier == KeyModifiers.None || (modifiers & modifier) != 0)
                {
                    return null;
                }

                modifiers |= modifier;
            }

            string key = parts[parts.Length - 1].Trim();
            foreach (KeyValuePair<string, int> pair in s_keysByName)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return modifiers == KeyModifiers.None ? (KeyChord?)null : new KeyChord(modifiers, pair.Value, pair.Key);
                }
            }

            return null;
        }

        public override string ToString()
        {
            return ((Modifiers & KeyModifiers.Ctrl) != 0 ? "Ctrl+" : string.Empty)
                + ((Modifiers & KeyModifiers.Alt) != 0 ? "Alt+" : string.Empty)
                + ((Modifiers & KeyModifiers.Shift) != 0 ? "Shift+" : string.Empty)
                + KeyName;
        }

        public bool Equals(KeyChord other)
        {
            return Modifiers == other.Modifiers && VirtualKey == other.VirtualKey;
        }

        public override bool Equals(object obj)
        {
            return obj is KeyChord other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)Modifiers * 397) ^ VirtualKey;
        }

        public static bool operator ==(KeyChord left, KeyChord right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(KeyChord left, KeyChord right)
        {
            return !left.Equals(right);
        }

        private static KeyModifiers ModifierOf(string name)
        {
            return string.Equals(name, "Ctrl", StringComparison.OrdinalIgnoreCase) ? KeyModifiers.Ctrl
                : string.Equals(name, "Alt", StringComparison.OrdinalIgnoreCase) ? KeyModifiers.Alt
                : string.Equals(name, "Shift", StringComparison.OrdinalIgnoreCase) ? KeyModifiers.Shift
                : KeyModifiers.None;
        }

        // The keys offered: letters, digits, the number pad's digits, and F1 to F12. They are on every keyboard
        // layout in the same place, unlike punctuation (UI foundation detailed design 9.8).
        private static string NameOf(KeyCode key)
        {
            if (key >= KeyCode.A && key <= KeyCode.Z)
            {
                return ((char)('A' + (key - KeyCode.A))).ToString();
            }

            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
            {
                return ((char)('0' + (key - KeyCode.Alpha0))).ToString();
            }

            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
            {
                return "Num" + (key - KeyCode.Keypad0);
            }

            if (key >= KeyCode.F1 && key <= KeyCode.F12)
            {
                return "F" + (key - KeyCode.F1 + 1);
            }

            return null;
        }

        private static Dictionary<string, int> KeysByName()
        {
            var keys = new Dictionary<string, int>();
            for (int i = 0; i < 26; i++)
            {
                keys[((char)('A' + i)).ToString()] = 0x41 + i;
            }

            for (int i = 0; i < 10; i++)
            {
                keys[i.ToString()] = 0x30 + i;
                keys["Num" + i] = 0x60 + i;
            }

            for (int i = 1; i <= 12; i++)
            {
                keys["F" + i] = 0x70 + i - 1;
            }

            return keys;
        }
    }
}
