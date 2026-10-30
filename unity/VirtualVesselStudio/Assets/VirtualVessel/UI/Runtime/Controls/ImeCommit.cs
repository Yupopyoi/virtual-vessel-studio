using System;
using System.Text;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// The text a commit of Japanese input should leave. Unity's field keeps the word being composed in its text;
    /// on commit it replaces that with the first character and the rest follow within the same frame, but its
    /// insertion point stays where the composition ended. So "こんにちは" became "こにちはん" (the user's log,
    /// 2026-10-31; #74), and moving the caret, at once or a tick later, did not help. The characters themselves
    /// arrive in the right order, so the field's text is rebuilt from them.
    /// </summary>
    internal sealed class ImeCommit
    {
        private readonly StringBuilder _committed = new StringBuilder();
        private string _before;
        private string _after;
        private bool _ending;
        private int _changes;

        /// <summary>The text the commit should leave, and the caret in it.</summary>
        public (string Text, int Caret) Result
        {
            get
            {
                string before = _before + _committed;
                return (before + _after, before.Length);
            }
        }

        /// <summary>
        /// The composition changed. The field's text and selection are as they are before Unity applies it.
        /// True when a composition ended: Unity tells of no change when the word stays as it was composed, so
        /// the end of the composition, not a change, ends the commit.
        /// </summary>
        public bool Composition(string composition, string text, int cursor, int select)
        {
            if (string.IsNullOrEmpty(composition))
            {
                _ending = _before != null;
                return _ending;
            }

            if (_before == null)
            {
                _before = text.Substring(0, Math.Min(Math.Min(cursor, select), text.Length));
                _after = text.Substring(Math.Min(Math.Max(cursor, select), text.Length));
            }

            // What was committed before this composition stays, whether part of a word or the word before.
            _before += _committed.ToString();
            _committed.Clear();
            _changes = 0;
            _ending = false;
            return false;
        }

        /// <summary>
        /// A character arrived: while composing, part of what is about to be committed. Backspace and Enter
        /// arrive this way too, and are no part of the word.
        /// </summary>
        public void Text(char character)
        {
            if (_before != null && !char.IsControl(character))
            {
                _committed.Append(character);
            }
        }

        /// <summary>
        /// Unity changed the text. True when that was the last of a commit's characters, so the text can be put
        /// right within the frame, before it is drawn out of order.
        /// </summary>
        public bool Changed()
        {
            if (!_ending || _committed.Length == 0)
            {
                return false;
            }

            _changes++;
            return _changes == _committed.Length;
        }

        /// <summary>
        /// Ends a commit that is over. False when there is none, as when the next word has begun: that
        /// composition carries this one's text on, and a later commit finishes both.
        /// </summary>
        public bool TryFinish(out (string Text, int Caret) result)
        {
            result = Result;
            if (!_ending)
            {
                return false;
            }

            Cancel();
            return true;
        }

        /// <summary>Forgets the composition, as when the field loses the focus in the middle of one.</summary>
        public void Cancel()
        {
            _before = null;
            _after = null;
            _committed.Clear();
            _changes = 0;
            _ending = false;
        }
    }
}
