using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.VoiceLab
{
    /// <summary>
    /// The Made voices tab (screen spec 8, Voice Lab detailed design 8.6): registered voices and runs, newest
    /// first, to play, rename, note, delete, and make again. Deleting asks first and says what stays.
    /// </summary>
    public sealed class VoiceLabLibraryView : VisualElement
    {
        public const string RowClass = VoiceLabScreen.UssClass + "__row";
        public const string NameClass = VoiceLabScreen.UssClass + "__name";

        private readonly IVoiceLabLibrary _library;
        private readonly VoiceLabPlayer _player;
        private readonly Action<string> _remake;
        private readonly Func<bool> _building;
        private readonly VisualElement _voices;
        private readonly VisualElement _runs;
        private readonly Label _empty;
        private readonly StatusChip _failed;

        /// <param name="remake">Makes a run again; null when voices cannot be made here, which disables it.</param>
        /// <param name="building">Whether a build runs now, so an unfinished run cannot be deleted.</param>
        public VoiceLabLibraryView(IVoiceLabLibrary library, VoiceLabPlayer player, Action<string> remake, Func<bool> building)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _remake = remake;
            _building = building ?? (() => false);
            Add(VoiceLabScreen.Lead(VoiceLabStrings.LibraryLead));
            _empty = Ui.Text(VoiceLabStrings.LibraryEmpty, VoiceLabScreen.NoteClass, "vv-text-body");
            Add(_empty);
            _failed = Ui.Chip(StatusKind.Error, VoiceLabStrings.LibraryFailed);
            _failed.style.display = DisplayStyle.None;
            Add(_failed);

            Card voices = Ui.Card(VoiceLabStrings.LibraryVoices);
            _voices = new VisualElement();
            voices.Add(_voices);
            Add(voices);

            Card runs = Ui.Card(VoiceLabStrings.LibraryRuns, alternate: true);
            _runs = new VisualElement();
            runs.Add(_runs);
            Add(runs);
        }

        /// <summary>Reads what was made again and shows it.</summary>
        public void Refresh()
        {
            ILocalizer localizer = UiServices.Find(this)?.Localizer;
            IReadOnlyList<VoiceLabVoiceRecord> voices = _library.ListVoices();
            IReadOnlyList<VoiceLabRunRecord> runs = _library.ListRuns();
            _voices.Clear();
            foreach (VoiceLabVoiceRecord voice in voices)
            {
                _voices.Add(VoiceRow(voice, localizer));
            }

            _runs.Clear();
            foreach (VoiceLabRunRecord run in runs)
            {
                _runs.Add(RunRow(run, localizer));
            }

            _voices.parent.parent.style.display = voices.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _runs.parent.parent.style.display = runs.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _empty.style.display = voices.Count == 0 && runs.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Local date and time, to the minute. Pure.</summary>
        public static string DateText(DateTimeOffset at)
        {
            return at == DateTimeOffset.MinValue ? "—" : at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>The run's words and timbre in one line, the words by their labels. Pure.</summary>
        public static string RunSummary(VoiceLabRunRecord run, ILocalizer localizer)
        {
            IEnumerable<string> words = run.Words.Select(id => KeyedText.Resolve(localizer, "lab.word." + id));
            string free = string.IsNullOrWhiteSpace(run.FreeWords) ? null : localizer?.Get(VoiceLabStrings.QuotedWords, ("words", run.FreeWords.Trim())) ?? run.FreeWords.Trim();
            string asked = string.Join(" · ", words.Concat(free == null ? Array.Empty<string>() : new[] { free }));
            string timbre = VoiceLabView.TimbreSummary(localizer, run.Timbre);
            return asked.Length > 0 ? asked + " / " + timbre : timbre;
        }

        /// <summary>The status a run shows, by its state. Pure.</summary>
        public static (StatusKind Kind, string Key) RunStatus(VoiceLabBuildState state)
        {
            switch (state)
            {
                case VoiceLabBuildState.Completed:
                    return (StatusKind.Success, VoiceLabStrings.RunCompleted);
                case VoiceLabBuildState.Cancelled:
                    return (StatusKind.Idle, VoiceLabStrings.RunCancelled);
                case VoiceLabBuildState.Failed:
                    return (StatusKind.Error, VoiceLabStrings.RunFailed);
                default:
                    return (StatusKind.Warning, VoiceLabStrings.RunMaking);
            }
        }

        private VisualElement VoiceRow(VoiceLabVoiceRecord voice, ILocalizer localizer)
        {
            VisualElement row = Row();
            row.Add(Play(voice.BaseFile));
            var name = new Label(voice.Name);
            name.AddToClassList("vv-text-body");
            name.AddToClassList(NameClass);
            row.Add(QuietText.Of(name));
            row.Add(Note(voice.MedianHz > 0 ? CandidateTile.HzText(voice.MedianHz) : "—"));
            row.Add(Note(DateText(voice.RegisteredAt)));

            TextInput rename = Ui.TextField(VoiceLabStrings.VoiceName);
            rename.MaxLength = 60;
            rename.style.display = DisplayStyle.None;
            ActionButton renameButton = Ui.Button(VoiceLabStrings.Rename);
            ActionButton save = Ui.Button(VoiceLabStrings.SaveName, ButtonVariant.Primary);
            save.style.display = DisplayStyle.None;
            renameButton.clicked += () =>
            {
                rename.value = voice.Name;
                rename.style.display = DisplayStyle.Flex;
                save.style.display = DisplayStyle.Flex;
                renameButton.style.display = DisplayStyle.None;
                rename.Focus();
            };
            save.clicked += () =>
            {
                if (string.IsNullOrWhiteSpace(rename.value))
                {
                    rename.ErrorKey = VoiceLabStrings.NameRequired;
                    return;
                }

                Try(() => _library.RenameVoice(voice.Id, rename.value));
            };
            ActionButton delete = Ui.Button(VoiceLabStrings.Delete);
            delete.clicked += () => Delete(VoiceLabStrings.DeleteVoiceTitle, VoiceLabStrings.DeleteVoiceBody, () => _library.DeleteVoice(voice.Id));
            row.Add(rename);
            row.Add(save);
            row.Add(renameButton);
            row.Add(delete);
            return row;
        }

        private VisualElement RunRow(VoiceLabRunRecord run, ILocalizer localizer)
        {
            var block = new VisualElement();
            block.AddToClassList(RowClass + "-block");
            VisualElement row = Row();
            row.Add(Play(run.BaseFile));
            row.Add(Note(DateText(run.CreatedAt)));
            (StatusKind kind, string key) = RunStatus(run.State);
            row.Add(Ui.Chip(kind, key));
            if (run.VoiceNames.Count > 0)
            {
                row.Add(Note(KeyedText.Resolve(localizer, VoiceLabStrings.RegisteredAs) + " " + string.Join(KeyedText.Resolve(localizer, "lab.ask.list"), run.VoiceNames)));
            }

            ActionButton remake = Ui.Button(VoiceLabStrings.Remake);
            remake.SetEnabled(_remake != null);
            remake.clicked += () => _remake?.Invoke(run.RunId);
            ActionButton delete = Ui.Button(VoiceLabStrings.Delete);
            bool unfinished = run.State != VoiceLabBuildState.Completed && run.State != VoiceLabBuildState.Failed && run.State != VoiceLabBuildState.Cancelled;
            delete.SetEnabled(!(unfinished && _building()));
            delete.TooltipKey = delete.enabledSelf ? null : VoiceLabStrings.DeleteWhileMaking;
            delete.clicked += () => Delete(VoiceLabStrings.DeleteRunTitle, VoiceLabStrings.DeleteRunBody, () => _library.DeleteRun(run.RunId));
            row.Add(remake);
            row.Add(delete);
            block.Add(row);
            block.Add(Note(RunSummary(run, localizer)));

            TextInput comment = Ui.TextField(VoiceLabStrings.Comment);
            comment.MaxLength = 500;
            comment.value = run.Comment;
            // Saved when the field is left, so typing does not write the file at every key.
            comment.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (comment.value != run.Comment)
                {
                    Try(() => _library.SetComment(run.RunId, comment.value), refresh: false);
                }
            });
            block.Add(comment);
            return block;
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.AddToClassList(RowClass);
            return row;
        }

        private static Label Note(string text)
        {
            var label = QuietText.Of(new Label(text));
            label.AddToClassList(VoiceLabScreen.NoteClass);
            return label;
        }

        private IconButton Play(string file)
        {
            IconButton play = Ui.IconButton(IconName.Play).WithTooltip(VoiceLabStrings.PlayBase);
            play.SetEnabled(file != null && _player.Available);
            play.clicked += () => _player.Toggle(play, file);
            return play;
        }

        private async void Delete(string titleKey, string bodyKey, Action delete)
        {
            var dialogs = UiServices.Find(this)?.Dialogs;
            if (dialogs != null && !await dialogs.ConfirmAsync(titleKey, bodyKey, VoiceLabStrings.Delete, ConfirmStyle.Danger))
            {
                return;
            }

            _player.Stop();
            Try(delete);
        }

        private void Try(Action work, bool refresh = true)
        {
            _failed.style.display = DisplayStyle.None;
            try
            {
                work();
            }
            catch (VoiceLabFailedException)
            {
                // The service has logged why.
                _failed.style.display = DisplayStyle.Flex;
                return;
            }

            if (refresh)
            {
                Refresh();
            }
        }
    }
}
