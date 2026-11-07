using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// STEP 2's held timbres (the user's request, 2026-11-07): the user keeps a few, plays them to compare, and
    /// goes back to one. They belong to one base, so picking another base clears them.
    /// </summary>
    public sealed class VoiceLabHoldList : VisualElement
    {
        public const int Most = 6;

        public const string RowClass = VoiceLabScreen.UssClass + "__held";

        private readonly Action<VoiceLabTimbre> _play;
        private readonly Action<VoiceLabTimbre> _restore;
        private readonly List<(int Number, VoiceLabTimbre Timbre)> _held = new List<(int, VoiceLabTimbre)>();
        private readonly VisualElement _rows;
        private int _next = 1;

        public VoiceLabHoldList(Action<VoiceLabTimbre> play, Action<VoiceLabTimbre> restore)
        {
            _play = play ?? throw new ArgumentNullException(nameof(play));
            _restore = restore ?? throw new ArgumentNullException(nameof(restore));
            Add(Ui.Text(VoiceLabStrings.HeldNote, VoiceLabScreen.NoteClass, "vv-text-body"));
            _rows = new VisualElement();
            Add(_rows);
            RegisterCallback<AttachToPanelEvent>(_ => Show());
            Show();
        }

        public IReadOnlyList<VoiceLabTimbre> Held => _held.ConvertAll(h => h.Timbre);

        /// <summary>Whether another can be held: not one already held, and not more than <see cref="Most"/>. Pure.</summary>
        public static bool CanHold(IReadOnlyList<VoiceLabTimbre> held, VoiceLabTimbre timbre)
        {
            if (held.Count >= Most)
            {
                return false;
            }

            foreach (VoiceLabTimbre t in held)
            {
                if (t.Equals(timbre))
                {
                    return false;
                }
            }

            return true;
        }

        public bool Hold(VoiceLabTimbre timbre)
        {
            if (!CanHold(Held, timbre))
            {
                return false;
            }

            _held.Add((_next++, timbre));
            Show();
            return true;
        }

        public void Clear()
        {
            _held.Clear();
            _next = 1;
            Show();
        }

        private void Show()
        {
            _rows.Clear();
            style.display = _held.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            ILocalizer localizer = UiServices.Find(this)?.Localizer;
            foreach ((int number, VoiceLabTimbre timbre) in _held)
            {
                var row = new VisualElement();
                row.AddToClassList(RowClass);
                IconButton play = Ui.IconButton(IconName.Play).WithTooltip(VoiceLabStrings.ListenShaped);
                play.clicked += () => _play(timbre);
                var name = QuietText.Of(new Label(localizer?.Get(VoiceLabStrings.HeldName, ("n", number.ToString(CultureInfo.InvariantCulture))) ?? "#" + number));
                name.AddToClassList("vv-text-body");
                var summary = QuietText.Of(new Label(VoiceLabView.TimbreSummary(localizer, timbre)));
                summary.AddToClassList(VoiceLabScreen.NoteClass);
                ActionButton restore = Ui.Button(VoiceLabStrings.Restore);
                restore.clicked += () => _restore(timbre);
                ActionButton remove = Ui.Button(VoiceLabStrings.RemoveHeld);
                remove.clicked += () =>
                {
                    _held.RemoveAll(h => h.Number == number);
                    Show();
                };
                row.Add(play);
                row.Add(name);
                row.Add(summary);
                row.Add(restore);
                row.Add(remove);
                _rows.Add(row);
            }
        }
    }
}
