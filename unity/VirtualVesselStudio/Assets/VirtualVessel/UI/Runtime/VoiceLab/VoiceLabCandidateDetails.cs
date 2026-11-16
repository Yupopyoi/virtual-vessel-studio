using System;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// STEP 1's folded details (the user's decision, 2026-11-14): the line the candidates read, which can go back to
    /// the default, and the seed the next candidates start at. Neither shapes the voice that is built from the
    /// candidate the user picks, only which candidates are made.
    /// </summary>
    public sealed class VoiceLabCandidateDetails : VisualElement
    {
        /// <summary>As generate.py's MAX_LINE.</summary>
        public const int MaxLine = 200;

        /// <summary>
        /// The largest first seed: generate.py takes seeds up to a signed 32-bit whole number, and the candidates
        /// after the first take the seeds after it, at most 24 of them.
        /// </summary>
        public const int MaxSeed = int.MaxValue - 24;

        private readonly string _defaultLine;
        private readonly Card _card;
        private readonly ActionButton _resetLine;

        public VoiceLabCandidateDetails(string defaultLine, int startSeed)
        {
            _defaultLine = defaultLine ?? string.Empty;
            _card = Ui.Card(VoiceLabStrings.Details, collapsible: true);
            _card.Expanded = false;

            Line = Ui.TextField(VoiceLabStrings.Line);
            Line.Multiline = true;
            Line.MaxLength = MaxLine;
            Line.SetValueWithoutNotify(_defaultLine);
            Line.RegisterValueChangedCallback(_ =>
            {
                Line.ErrorKey = null;
                ShowReset();
            });
            _card.Add(Line);
            _resetLine = Ui.Button(VoiceLabStrings.LineReset);
            _resetLine.clicked += () => Line.value = _defaultLine;
            _card.Add(VoiceLabScreen.Actions(_resetLine));
            _card.Add(Ui.Text(VoiceLabStrings.LineNote, VoiceLabScreen.NoteClass, "vv-text-body"));

            Seed = Ui.NumberField(VoiceLabStrings.Seed);
            Seed.Decimals = 0;
            Seed.Min = 0;
            Seed.Max = MaxSeed;
            Seed.SetValueWithoutNotify(Math.Max(0, Math.Min(MaxSeed, startSeed)));
            _card.Add(Seed);
            _card.Add(Ui.Text(VoiceLabStrings.SeedNote, VoiceLabScreen.NoteClass, "vv-text-body"));
            Add(_card);
            ShowReset();
        }

        public TextInput Line { get; }

        public NumberInput Seed { get; }

        public ActionButton ResetLineButton => _resetLine;

        public Card Card => _card;

        /// <summary>The seed the next candidates start at.</summary>
        public int FirstSeed => (int)Seed.value;

        /// <summary>
        /// The line to send: empty for the default, so the script reads the shelf's; null when the user left nothing
        /// to read, which shows an error on the field and opens the details.
        /// </summary>
        public string LineToSend()
        {
            string line = LineToSend(Line.value, _defaultLine);
            if (line == null)
            {
                Line.ErrorKey = VoiceLabStrings.LineRequired;
                _card.Expanded = true;
            }

            return line;
        }

        /// <summary>After candidates were made: the next ones start past their seeds, so pressing Make again gives new voices.</summary>
        public void Advance(int made)
        {
            Seed.value = NextSeed(FirstSeed, made);
        }

        /// <summary>A run's line and seed, to make its base again; empty line for the default, null seed to keep the one shown.</summary>
        public void Restore(string line, int? seed)
        {
            Line.value = string.IsNullOrEmpty(line) ? _defaultLine : line;
            if (seed.HasValue)
            {
                Seed.value = Math.Max(0, Math.Min(MaxSeed, seed.Value));
            }
        }

        public void SetBusy(bool busy)
        {
            Line.SetEnabled(!busy);
            Seed.SetEnabled(!busy);
            _resetLine.SetEnabled(!busy && Line.value != _defaultLine);
        }

        /// <summary>
        /// The line to send for <paramref name="text"/>: empty when it is the default, null when only spaces are
        /// left. The script cleans it further. Pure.
        /// </summary>
        public static string LineToSend(string text, string defaultLine)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return text == defaultLine ? string.Empty : text;
        }

        /// <summary>The seed after <paramref name="made"/> candidates from <paramref name="seed"/>; back to 0 past the largest. Pure.</summary>
        public static int NextSeed(int seed, int made)
        {
            long next = (long)seed + Math.Max(0, made);
            return next > MaxSeed ? 0 : (int)next;
        }

        private void ShowReset()
        {
            _resetLine.SetEnabled(Line.value != _defaultLine);
        }
    }
}
