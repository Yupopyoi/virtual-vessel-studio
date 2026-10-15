using System;
using System.Collections.Generic;
using VirtualVessel.Core.Geometry;

namespace VirtualVessel.UI.Frames
{
    /// <summary>One frame's layout before and after an edit.</summary>
    public readonly struct FrameEdit
    {
        public FrameEdit(IFrameSource source, FrameLayout before, FrameLayout after)
        {
            Source = source;
            Before = before;
            After = after;
        }

        public IFrameSource Source { get; }

        public FrameLayout Before { get; }

        public FrameLayout After { get; }
    }

    /// <summary>
    /// Undo and redo of placement edits (UI foundation detailed design 9.3). One drag, one key press,
    /// one burst of wheel steps, or one menu command is one step; a step can change several frames, as
    /// changing the stacking order does. Kept only while the editor is open.
    /// </summary>
    public sealed class FrameHistory
    {
        /// <summary>Older steps are forgotten beyond this many.</summary>
        public const int Capacity = 100;

        private readonly List<FrameEdit[]> _undo = new List<FrameEdit[]>();
        private readonly List<FrameEdit[]> _redo = new List<FrameEdit[]>();

        public bool CanUndo => _undo.Count > 0;

        public bool CanRedo => _redo.Count > 0;

        public int UndoCount => _undo.Count;

        /// <summary>Records a one-frame step.</summary>
        public void Record(IFrameSource source, FrameLayout before, FrameLayout after)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            Record(new[] { new FrameEdit(source, before, after) });
        }

        /// <summary>
        /// Records a step. A new step clears what could be redone; frames that did not change are left
        /// out, and a step that changed nothing is ignored.
        /// </summary>
        public void Record(IReadOnlyList<FrameEdit> edits)
        {
            var changed = new List<FrameEdit>(edits.Count);
            foreach (FrameEdit edit in edits)
            {
                if (edit.Before != edit.After)
                {
                    changed.Add(edit);
                }
            }

            if (changed.Count == 0)
            {
                return;
            }

            _redo.Clear();
            _undo.Add(changed.ToArray());
            if (_undo.Count > Capacity)
            {
                _undo.RemoveAt(0);
            }
        }

        /// <summary>The step to revert (apply each <see cref="FrameEdit.Before"/>), or null.</summary>
        public IReadOnlyList<FrameEdit> Undo()
        {
            return Move(_undo, _redo);
        }

        /// <summary>The step to apply again (each <see cref="FrameEdit.After"/>), or null.</summary>
        public IReadOnlyList<FrameEdit> Redo()
        {
            return Move(_redo, _undo);
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }

        private static FrameEdit[] Move(List<FrameEdit[]> from, List<FrameEdit[]> to)
        {
            if (from.Count == 0)
            {
                return null;
            }

            FrameEdit[] step = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            to.Add(step);
            return step;
        }
    }
}
