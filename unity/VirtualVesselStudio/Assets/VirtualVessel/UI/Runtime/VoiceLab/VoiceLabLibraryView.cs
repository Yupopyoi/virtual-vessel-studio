using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine.UIElements;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Voice;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Binding;

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

        // A voice's row in columns of fixed width, so the buttons stay in line whatever the name, the pitch, or whether
        // it is in use (the user's review, 2026-11-15).
        public const string VoiceNameClass = VoiceLabScreen.UssClass + "__voice-name";
        public const string VoiceNameTextClass = VoiceLabScreen.UssClass + "__voice-name-text";
        public const string VoiceHzClass = VoiceLabScreen.UssClass + "__voice-hz";
        public const string VoiceDateClass = VoiceLabScreen.UssClass + "__voice-date";
        public const string VoiceUseClass = VoiceLabScreen.UssClass + "__voice-use";

        // A written prompt is shown this far in a run's one line; the run keeps it whole.
        private const int SummaryPrompt = 80;

        private readonly IVoiceLabLibrary _library;
        private readonly VoiceLabPlayer _player;
        private readonly Action<string> _remake;
        private readonly Action<string, string> _try;
        private readonly IVoiceConversion _voice;
        private readonly Func<bool> _building;
        private readonly VisualElement _voices;
        private readonly VisualElement _runs;
        private readonly Card _voicesCard;
        private readonly Card _runsCard;
        private readonly Label _empty;
        private readonly StatusChip _failed;
        private readonly Card _storage;
        private readonly SettingRow _samplesRow;
        private readonly SettingRow _candidatesRow;

        /// <param name="remake">Makes a run again; null when voices cannot be made here, which disables it.</param>
        /// <param name="tryVoice">Opens STEP 4 with a run (and a registered voice's folder); null disables Try.</param>
        /// <param name="building">Whether a build runs now, so an unfinished run cannot be deleted.</param>
        /// <param name="voice">The voice conversion, to use a voice for the stream and show the one in use; optional.</param>
        public VoiceLabLibraryView(IVoiceLabLibrary library, VoiceLabPlayer player, Action<string> remake, Action<string, string> tryVoice, Func<bool> building, IVoiceConversion voice = null)
        {
            _try = tryVoice;
            _voice = voice;
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _remake = remake;
            _building = building ?? (() => false);
            PanelLifetime.OnLocaleChanged(this, Refresh);
            Add(VoiceLabScreen.Lead(VoiceLabStrings.LibraryLead));
            _empty = Ui.Text(VoiceLabStrings.LibraryEmpty, VoiceLabScreen.NoteClass, "vv-text-body");
            Add(_empty);
            _failed = Ui.Chip(StatusKind.Error, VoiceLabStrings.CrudStorageFailed);
            _failed.style.display = DisplayStyle.None;
            Add(_failed);

            _voicesCard = Ui.Card(VoiceLabStrings.LibraryVoices);
            _voices = new VisualElement();
            _voicesCard.Add(_voices);
            Add(_voicesCard);

            _runsCard = Ui.Card(VoiceLabStrings.LibraryRuns, alternate: true);
            _runs = new VisualElement();
            _runsCard.Add(_runs);
            Add(_runsCard);

            // What can be deleted to free space (the user's request, 2026-11-11).
            _storage = Ui.Card(VoiceLabStrings.Storage);
            DeleteSamplesButton = Ui.Button(VoiceLabStrings.Delete);
            DeleteSamplesButton.clicked += () => Delete(VoiceLabStrings.DeleteSamplesTitle, VoiceLabStrings.DeleteSamplesBody, _library.DeleteSamples, "Deleting the samples");
            _samplesRow = StorageRow(VoiceLabStrings.StorageSamples, VoiceLabStrings.StorageSamplesHelp, DeleteSamplesButton);
            DeleteCandidatesButton = Ui.Button(VoiceLabStrings.Delete);
            DeleteCandidatesButton.clicked += () => Delete(VoiceLabStrings.DeleteCandidatesTitle, VoiceLabStrings.DeleteCandidatesBody, _library.DeleteOldCandidates, "Deleting old candidates");
            _candidatesRow = StorageRow(VoiceLabStrings.StorageCandidates, VoiceLabStrings.StorageCandidatesHelp, DeleteCandidatesButton);
            _storage.Add(_samplesRow);
            _storage.Add(_candidatesRow);
            Add(_storage);
        }

        private const string SizeName = "size";

        /// <summary>The stored data Card, where the screen adds the training environment's row.</summary>
        public Card StorageCard => _storage;

        public ActionButton DeleteSamplesButton { get; }

        public ActionButton DeleteCandidatesButton { get; }

        /// <summary>A size as the tab shows it: KB under 1 MB; MB with one decimal under 10, else whole; GB with one decimal from 1 GB. Pure.</summary>
        public static string SizeText(long bytes)
        {
            const double Mb = 1024d * 1024d;
            if (bytes >= 1024L * 1024L * 1024L)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0.0} GB", bytes / (Mb * 1024d));
            }

            if (bytes < 1024L * 1024L)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0} KB", Math.Ceiling(bytes / 1024d));
            }

            double mb = bytes / Mb;
            return string.Format(CultureInfo.InvariantCulture, mb < 10d ? "{0:0.0} MB" : "{0:0} MB", mb);
        }

        // The size goes under the name, as text this tab sets itself; a keyed label would put its key's text back on a
        // language change.
        private static SettingRow StorageRow(string nameKey, string helpKey, ActionButton delete)
        {
            SettingRow row = Ui.SettingRow(nameKey);
            row.HelpKey = helpKey;
            Label size = VoiceLabScreen.PlainNote();
            size.name = SizeName;
            row.TextArea.Add(size);
            row.Add(delete);
            return row;
        }

        private void ShowStorage()
        {
            CrudResult<VoiceLabStorageUse> use = _library.StorageUse();
            if (!use.Ok)
            {
                ShowFailure("Measuring Voice Lab's storage", use.Error, use.Detail);
            }

            VoiceLabStorageUse value = use.Ok ? use.Value : default;
            ShowSize(_samplesRow, DeleteSamplesButton, value.SamplesBytes);
            ShowSize(_candidatesRow, DeleteCandidatesButton, value.CandidatesBytes);
        }

        private static void ShowSize(SettingRow row, ActionButton delete, long bytes)
        {
            row.Q<Label>(SizeName).text = SizeText(bytes);
            delete.SetEnabled(bytes > 0);
        }

        private void TryVoice(VoiceLabVoiceRecord voice)
        {
            CrudResult<string> folder = _library.VoiceFolder(voice.Id);
            if (!folder.Ok)
            {
                ShowFailure("Finding voice " + voice.Id, folder.Error, folder.Detail);
                return;
            }

            _try?.Invoke(voice.RunId, folder.Value);
        }

        /// <summary>Whether a voice is the one used for the stream. Pure.</summary>
        public static bool IsInUse(string inUseVoiceId, string voiceId)
        {
            return inUseVoiceId != null && inUseVoiceId == voiceId;
        }

        /// <summary>Reads what was made again and shows it.</summary>
        public void Refresh()
        {
            ILocalizer localizer = UiServices.Find(this)?.Localizer;
            CrudResult<IReadOnlyList<VoiceLabVoiceRecord>> voicesRead = _library.ListVoices();
            CrudResult<IReadOnlyList<VoiceLabRunRecord>> runsRead = _library.ListRuns();
            if (!voicesRead.Ok || !runsRead.Ok)
            {
                CrudResult failed = voicesRead.Ok ? runsRead.WithoutValue : voicesRead.WithoutValue;
                ShowFailure("Listing what was made", failed.Error, failed.Detail);
            }

            IReadOnlyList<VoiceLabVoiceRecord> voices = voicesRead.Ok ? voicesRead.Value : Array.Empty<VoiceLabVoiceRecord>();
            IReadOnlyList<VoiceLabRunRecord> runs = runsRead.Ok ? runsRead.Value : Array.Empty<VoiceLabRunRecord>();
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

            // The Cards by name: their rows' parent's parent was this view itself, so a refresh while another tab
            // showed, as on a language change, put Made voices under Make a voice (2026-11-15).
            _voicesCard.style.display = voices.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _runsCard.style.display = runs.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _empty.style.display = voices.Count == 0 && runs.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            ShowStorage();
        }

        /// <summary>Local date and time, to the minute. Pure.</summary>
        public static string DateText(DateTimeOffset at)
        {
            return at == DateTimeOffset.MinValue ? "—" : at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>The run's words and timbre in one line, the words by their labels. Pure.</summary>
        public static string RunSummary(VoiceLabRunRecord run, ILocalizer localizer)
        {
            if (!string.IsNullOrEmpty(run.Prompt))
            {
                string shown = run.Prompt.Length > SummaryPrompt ? run.Prompt.Substring(0, SummaryPrompt) + "…" : run.Prompt;
                string quoted = localizer?.Get(VoiceLabStrings.QuotedWords, ("words", shown)) ?? shown;
                return quoted + " / " + VoiceLabView.TimbreSummary(localizer, run.Timbre);
            }

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
            name.AddToClassList(VoiceNameTextClass);
            name.tooltip = voice.Name;

            // The name and its rename field share one slot by the play button, so renaming happens where the name is
            // (the user's review, 2026-11-15: the field opened at the row's end, and the row wrapped).
            var nameSlot = new VisualElement();
            nameSlot.AddToClassList(VoiceNameClass);
            nameSlot.Add(QuietText.Of(name));
            row.Add(nameSlot);
            Label hz = Note(voice.MedianHz > 0 ? CandidateTile.HzText(voice.MedianHz) : "—");
            hz.AddToClassList(VoiceHzClass);
            row.Add(hz);
            Label date = Note(DateText(voice.RegisteredAt));
            date.AddToClassList(VoiceDateClass);
            row.Add(date);
            var useSlot = new VisualElement();
            useSlot.AddToClassList(VoiceUseClass);
            row.Add(useSlot);

            // No caption above the field: it stands in for the name, and its tooltip says what it is.
            TextInput rename = Ui.TextField(null);
            rename.TooltipKey = VoiceLabStrings.VoiceName;
            rename.MaxLength = 60;
            rename.style.display = DisplayStyle.None;
            nameSlot.Add(rename);
            ActionButton renameButton = Ui.Button(VoiceLabStrings.Rename);
            ActionButton save = Ui.Button(VoiceLabStrings.SaveName, ButtonVariant.Primary);
            save.style.display = DisplayStyle.None;
            renameButton.clicked += () =>
            {
                rename.value = voice.Name;
                rename.style.display = DisplayStyle.Flex;
                name.style.display = DisplayStyle.None;
                save.style.display = DisplayStyle.Flex;
                renameButton.style.display = DisplayStyle.None;
                rename.FocusInput();
            };
            save.clicked += () =>
            {
                if (string.IsNullOrWhiteSpace(rename.value))
                {
                    rename.ErrorKey = VoiceLabStrings.NameRequired;
                    return;
                }

                Try(() => _library.RenameVoice(voice.Id, rename.value), "Renaming voice " + voice.Id);
            };
            ActionButton delete = Ui.Button(VoiceLabStrings.Delete);
            delete.clicked += () => Delete(VoiceLabStrings.DeleteVoiceTitle, VoiceLabStrings.DeleteVoiceBody, () => _library.DeleteVoice(voice.Id), "Deleting voice " + voice.Id);
            ActionButton tryButton = Ui.Button(VoiceLabStrings.LibraryTry);
            tryButton.SetEnabled(_try != null);
            tryButton.clicked += () => TryVoice(voice);
            bool inUse = IsInUse(_voice?.InUseVoiceId, voice.Id);
            if (inUse)
            {
                useSlot.Add(Ui.Chip(StatusKind.Success, VoiceLabStrings.InUse));
                delete.SetEnabled(false);
                delete.TooltipKey = VoiceLabStrings.DeleteInUse;
            }
            else if (_voice != null)
            {
                ActionButton use = Ui.Button(VoiceLabStrings.Use, ButtonVariant.Primary);
                use.clicked += () =>
                {
                    _voice.SelectVoice(voice.Id);
                    Refresh();
                };
                useSlot.Add(use);
            }

            // Save takes Rename's place, so the buttons around it stay where they were.
            row.Add(tryButton);
            row.Add(renameButton);
            row.Add(save);
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
            delete.clicked += () => Delete(VoiceLabStrings.DeleteRunTitle, VoiceLabStrings.DeleteRunBody, () => _library.DeleteRun(run.RunId), "Deleting run " + run.RunId);
            ActionButton tryButton = Ui.Button(VoiceLabStrings.LibraryTry);
            tryButton.SetEnabled(_try != null && run.State == VoiceLabBuildState.Completed);
            tryButton.clicked += () => _try?.Invoke(run.RunId, null);
            row.Add(tryButton);
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
                    Try(() => _library.SetComment(run.RunId, comment.value), "Saving the note of run " + run.RunId, refresh: false);
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

        /// <summary>Tells the user a CRUD operation failed and logs why (system design 6.31).</summary>
        public void ShowFailure(string what, CrudError error, string detail)
        {
            VoiceLabScreen.ShowFailure(this, _failed, what, error, detail);
        }

        private async void Delete(string titleKey, string bodyKey, Func<CrudResult> delete, string what)
        {
            var dialogs = UiServices.Find(this)?.Dialogs;
            if (dialogs != null && !await dialogs.ConfirmAsync(titleKey, bodyKey, VoiceLabStrings.Delete, ConfirmStyle.Danger))
            {
                return;
            }

            _player.Stop();
            Try(delete, what);
        }

        private void Try(Func<CrudResult> work, string what, bool refresh = true)
        {
            _failed.style.display = DisplayStyle.None;
            CrudResult result = work();
            if (!result.Ok)
            {
                ShowFailure(what, result.Error, result.Detail);
                return;
            }

            if (refresh)
            {
                Refresh();
            }
        }
    }
}
