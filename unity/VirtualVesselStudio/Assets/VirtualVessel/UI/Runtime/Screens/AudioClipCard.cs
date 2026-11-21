using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using VirtualVessel.Audio.Clips;
using VirtualVessel.Audio.Playback;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Shortcuts;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of the BGM and SE Cards (docs/&lt;lang&gt;/ui/screens/setup.md 14).</summary>
    [StringKeys]
    public static class AudioClipStrings
    {
        public const string BgmTitle = "setup.bgm.title";
        public const string EffectsTitle = "setup.se.title";
        public const string Name = "setup.clips.name";
        public const string Length = "setup.clips.length";
        public const string Loop = "setup.clips.loop";
        public const string LoopDescription = "setup.clips.loop.description";
        public const string Gain = "setup.clips.gain";
        public const string Add = "setup.clips.add";
        public const string Rename = "setup.clips.rename";
        public const string Save = "setup.clips.save";
        public const string Delete = "setup.clips.delete";
        public const string Move = "setup.clips.move";
        public const string NameRequired = "setup.clips.name.required";
        public const string Filter = "setup.clips.filter";
        public const string BgmPlay = "setup.bgm.play";
        public const string BgmStop = "setup.bgm.stop";
        public const string BgmPlaying = "setup.bgm.playing";
        public const string BgmFailed = "setup.bgm.failed";
        public const string BgmEmpty = "setup.bgm.empty";
        public const string BgmDeleteTitle = "setup.bgm.delete.title";
        public const string BgmDeleteBody = "setup.bgm.delete.body";
        public const string EffectPlay = "setup.se.play";
        public const string EffectsStop = "setup.se.stop-all";
        public const string EffectsEmpty = "setup.se.empty";
        public const string EffectLoading = "setup.se.loading";
        public const string EffectDeleteTitle = "setup.se.delete.title";
        public const string EffectDeleteBody = "setup.se.delete.body";
        public const string DeleteInUse = "setup.clips.delete-in-use";
        public const string ProblemUnsupported = "setup.clips.problem.unsupported";
        public const string ProblemUnreadable = "setup.clips.problem.unreadable";
        public const string ProblemTooLong = "setup.clips.problem.too-long";
        public const string ProblemStorage = "setup.clips.problem.storage";
    }

    /// <summary>
    /// The BGM or the SE Card on the setup screen's Audio page (BGM and SE detailed design; setup.md 14): the registered
    /// files in rows with their play button, name, length, loop (BGM), and volume, renaming, deleting, and reordering
    /// them, and adding a WAV or MP3 file. What plays is the players' state; the Card only shows it and asks.
    /// </summary>
    public sealed partial class AudioClipCard : VisualElement
    {
        public const string UssClass = "vv-clips";
        public const string ColumnsClass = UssClass + "__columns";
        public const string RowClass = UssClass + "__row";
        public const string CellClass = UssClass + "__cell";
        public const string DraggingClass = RowClass + "--dragging";
        public const string DropClass = UssClass + "__drop";
        public const string MoveColumn = CellClass + "--move";
        public const string GripClass = UssClass + "__grip";
        public const string PlayColumn = CellClass + "--play";
        public const string NameColumn = CellClass + "--name";
        public const string LengthColumn = CellClass + "--length";
        public const string LoopColumn = CellClass + "--loop";
        public const string GainColumn = CellClass + "--gain";
        public const string ShortcutColumn = CellClass + "--shortcut";
        public const string ActionsColumn = CellClass + "--actions";
        public const string NameTextClass = UssClass + "__name";
        public const string NoteClass = UssClass + "__note";
        public const string ActionsClass = UssClass + "__actions";

        /// <summary>How often the Card looks whether the library changed, as it may change on another thread.</summary>
        public const long RefreshIntervalMs = 200;

        /// <summary>A press on the grip turns into a drag after moving this many pixels, as in the layer list.</summary>
        private const float DragThreshold = 4f;

        private readonly AudioClipKind _kind;
        private readonly IAudioClipLibrary _library;
        private readonly IBgmPlayer _bgm;
        private readonly ISoundEffectPlayer _effects;
        private readonly IFileDialog _files;
        private readonly IGlobalShortcuts _shortcuts;
        private readonly VisualElement _rows = new VisualElement();
        private readonly Label _empty;
        private readonly StatusChip _problem;
        private readonly List<ClipRow> _shown = new List<ClipRow>();
        private readonly VisualElement _drop;
        private Drag _drag;
        private string _language;
        private volatile bool _changed = true;

        /// <summary>The Card itself, which the user can close by its title.</summary>
        public Card Card { get; }

        /// <param name="shortcuts">SE only: the keys that play sound effects from any app; null leaves out their column.</param>
        public AudioClipCard(AudioClipKind kind, IAudioClipLibrary library, IBgmPlayer bgm, ISoundEffectPlayer effects, IFileDialog files, IGlobalShortcuts shortcuts = null)
        {
            _shortcuts = kind == AudioClipKind.SoundEffect ? shortcuts : null;
            _kind = kind;
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _bgm = bgm;
            _effects = effects;
            _files = files;
            AddToClassList(UssClass);
            // Collapsible, open at first: the lists grow long, and the user may close one to reach the other (setup.md 14).
            Card card = Ui.Card(kind == AudioClipKind.Bgm ? AudioClipStrings.BgmTitle : AudioClipStrings.EffectsTitle, collapsible: true);
            card.Expanded = true;
            Card = card;
            Add(card);
            if (kind == AudioClipKind.SoundEffect)
            {
                StopAllButton = Ui.Button(AudioClipStrings.EffectsStop);
                StopAllButton.clicked += () => _effects?.StopAll();
                card.HeaderEnd.Add(StopAllButton);
            }

            card.Add(Columns(kind, _shortcuts != null));
            card.Add(_rows);

            // The landing line is drawn over the rows, not inserted between them, so the rows do not move under the
            // pointer during a drag (as in the layer list).
            _drop = new VisualElement { pickingMode = PickingMode.Ignore };
            _drop.AddToClassList(DropClass);
            _drop.style.position = Position.Absolute;
            _drop.style.display = DisplayStyle.None;
            _empty = Ui.Text(kind == AudioClipKind.Bgm ? AudioClipStrings.BgmEmpty : AudioClipStrings.EffectsEmpty, NoteClass, "vv-text-body");
            card.Add(_empty);
            _problem = Ui.Chip(StatusKind.Error, AudioClipStrings.ProblemUnreadable);
            _problem.style.display = DisplayStyle.None;
            card.Add(_problem);

            AddButton = Ui.Button(AudioClipStrings.Add);
            AddButton.clicked += () => _ = AddFileAsync();
            AddButton.SetEnabled(files != null);
            var actions = new VisualElement();
            actions.AddToClassList(ActionsClass);
            actions.Add(AddButton);
            card.Add(actions);
            PanelLifetime.WhileAttached(this, Attach);
        }

        public ActionButton AddButton { get; }

        /// <summary>SE only: stops every sound effect.</summary>
        public ActionButton StopAllButton { get; }

        /// <summary>The rows as shown, for tests.</summary>
        public IReadOnlyList<ClipRow> Rows => _shown;

        /// <summary>The problem with the last file added, for tests.</summary>
        public StatusChip Problem => _problem;

        /// <summary>A length as the rows show it: minutes and seconds; empty when it is not known. Pure.</summary>
        public static string LengthText(TimeSpan length)
        {
            if (length <= TimeSpan.Zero)
            {
                return string.Empty;
            }

            int seconds = (int)Math.Round(length.TotalSeconds);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", seconds / 60, seconds % 60);
        }

        /// <summary>What the user is told when a file cannot be registered. Pure.</summary>
        public static string ProblemKey(AudioClipProblem problem)
        {
            return problem switch
            {
                AudioClipProblem.Unsupported => AudioClipStrings.ProblemUnsupported,
                AudioClipProblem.TooLong => AudioClipStrings.ProblemTooLong,
                _ => AudioClipStrings.ProblemUnreadable,
            };
        }

        /// <summary>Whether a BGM row shows its track playing (or starting), and whether it failed. Pure.</summary>
        public static (bool Playing, bool Failed) RowState(BgmStatus status, string id)
        {
            bool mine = status.TrackId == id;
            return (mine && (status.State == BgmPlayState.Starting || status.State == BgmPlayState.Playing), mine && status.State == BgmPlayState.Failed);
        }

        /// <summary>Plays, or stops, a row's file: a BGM that plays stops; anything else plays.</summary>
        internal void Press(AudioClipRecord record)
        {
            if (_kind == AudioClipKind.SoundEffect)
            {
                if (_effects != null)
                {
                    new PlaySoundEffectCommand(_effects, record.Id).Execute();
                }

                return;
            }

            if (_bgm == null)
            {
                return;
            }

            if (RowState(_bgm.Status.Value, record.Id).Playing)
            {
                _bgm.Stop();
            }
            else
            {
                _bgm.Play(new SingleTrack(record.Id, record.Settings.Loop));
            }
        }

        internal void Change(AudioClipRecord record, AudioClipChange change)
        {
            CrudResult<AudioClipRecord> updated = _library.Update(record.Id, change);
            if (!updated.Ok)
            {
                ShowFailure($"Changing {record.Id}", updated.Error, updated.Detail);
            }
        }

        /// <summary>
        /// Gives a sound effect the keys the user pressed, or removes them with an empty text. Keys another sound effect
        /// has are not taken from it: the user is told and chooses again.
        /// </summary>
        internal void ChooseShortcut(AudioClipRecord record, string shortcut)
        {
            _problem.style.display = DisplayStyle.None;
            if (TakenBy(_shown.ConvertAll(row => row.Record), record.Id, shortcut) != null)
            {
                ShowProblem(ShortcutStrings.Taken);
                return;
            }

            Change(record, new AudioClipChange(shortcut: shortcut));
        }

        /// <summary>The ID of another sound effect that has these keys already, or null. Pure.</summary>
        public static string TakenBy(IReadOnlyList<AudioClipRecord> effects, string id, string shortcut)
        {
            KeyChord? chord = KeyChord.Parse(shortcut);
            if (!chord.HasValue)
            {
                return null;
            }

            foreach (AudioClipRecord effect in effects)
            {
                if (effect.Id != id && KeyChord.Parse(effect.Settings.Shortcut) == chord)
                {
                    return effect.Id;
                }
            }

            return null;
        }

        internal async void Delete(AudioClipRecord record)
        {
            IDialogService dialogs = UiServices.Find(this)?.Dialogs;
            string title = _kind == AudioClipKind.Bgm ? AudioClipStrings.BgmDeleteTitle : AudioClipStrings.EffectDeleteTitle;
            string body = _kind == AudioClipKind.Bgm ? AudioClipStrings.BgmDeleteBody : AudioClipStrings.EffectDeleteBody;
            if (dialogs != null && !await dialogs.ConfirmAsync(title, body, AudioClipStrings.Delete, ConfirmStyle.Danger))
            {
                return;
            }

            CrudResult deleted = _library.Delete(record.Id);
            if (!deleted.Ok)
            {
                ShowFailure($"Deleting {record.Id}", deleted.Error, deleted.Detail);
            }
        }

        /// <summary>
        /// Puts a row's file at a position in the list. With <paramref name="focus"/>, the place of a control in the
        /// row's <see cref="ClipRow.Controls"/>, that control of the moved row takes the focus.
        /// </summary>
        internal void Move(AudioClipRecord record, int position, int focus = -1)
        {
            CrudResult moved = _library.Move(record.Id, position);
            if (!moved.Ok)
            {
                ShowFailure($"Moving {record.Id}", moved.Error, moved.Detail);
                return;
            }

            Refresh();
            if (focus >= 0)
            {
                _shown.Find(row => row.Record.Id == record.Id)?.Focus(focus);
            }
        }

        /// <summary>Reads the library again and shows it.</summary>
        public void Refresh()
        {
            if (_drag != null)
            {
                EndDrag();
            }

            _changed = false;
            CrudResult<IReadOnlyList<AudioClipRecord>> listed = _library.List(_kind);
            if (!listed.Ok)
            {
                ShowFailure($"Listing {_kind}", listed.Error, listed.Detail);
            }

            IReadOnlyList<AudioClipRecord> records = listed.Ok ? listed.Value : Array.Empty<AudioClipRecord>();
            _rows.Clear();
            _shown.Clear();
            foreach (AudioClipRecord record in records)
            {
                var row = new ClipRow(this, record, _kind, record.NameIn(_language), _shortcuts);
                _shown.Add(row);
                _rows.Add(row.Root);
            }

            _rows.Add(_drop);

            _empty.style.display = records.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            ShowPlayers();
        }

        private IDisposable Attach(UiServices services)
        {
            void OnChanged(AudioClipKind kind)
            {
                // Raised on any thread: the Card reads it again on its own.
                _changed = _changed || kind == _kind;
            }

            // Built-in files' names follow the screen's language until the user renames them.
            void OnLocaleChanged(LocaleInfo locale)
            {
                _language = locale?.Code;
                Refresh();
            }

            _language = services?.Localizer?.Locale?.Code;
            _library.Changed += OnChanged;
            Refresh();
            IVisualElementScheduledItem poll = schedule.Execute(() =>
            {
                if (_changed)
                {
                    Refresh();
                }
                else
                {
                    ShowPlayers();
                }
            }).Every(RefreshIntervalMs);
            var subscriptions = new List<IDisposable> { new DisposeAction(() => _library.Changed -= OnChanged), new DisposeAction(poll.Pause) };
            if (services?.Localizer != null)
            {
                services.Localizer.LocaleChanged += OnLocaleChanged;
                subscriptions.Add(new DisposeAction(() => services.Localizer.LocaleChanged -= OnLocaleChanged));
            }

            if (_bgm != null)
            {
                subscriptions.Add(_bgm.Status.Subscribe(_ => ShowPlayers()));
            }

            return new DisposeAll(subscriptions);
        }

        // What plays, and which effects are loaded: the players say, each row shows.
        private void ShowPlayers()
        {
            BgmStatus status = _bgm?.Status.Value ?? default;
            foreach (ClipRow row in _shown)
            {
                if (_kind == AudioClipKind.Bgm)
                {
                    (bool playing, bool failed) = RowState(status, row.Record.Id);
                    row.ShowBgm(playing, failed, status.HoldsTrack && status.TrackId == row.Record.Id, _bgm != null);
                }
                else
                {
                    KeyChord? chord = KeyChord.Parse(row.Record.Settings.Shortcut);
                    bool heldElsewhere = chord.HasValue && _shortcuts != null && _shortcuts.Refused.Contains(chord.Value);
                    row.ShowEffect(_effects != null && _effects.IsReady(row.Record.Id), heldElsewhere);
                }
            }
        }

        private async Task AddFileAsync()
        {
            if (_files == null)
            {
                return;
            }

            string filter = UiServices.Find(this)?.Localizer?.Get(AudioClipStrings.Filter) ?? "Audio";
            string path = await _files.OpenAsync(new FileDialogOptions { Filters = new[] { new FileFilter(filter, _library.FileExtensions) } });
            if (path == null)
            {
                return;
            }

            AddButton.SetEnabled(false);
            _problem.style.display = DisplayStyle.None;
            try
            {
                AudioClipProblem problem = await _library.CheckAsync(_kind, path, CancellationToken.None);
                if (problem != AudioClipProblem.None)
                {
                    ShowProblem(ProblemKey(problem));
                    return;
                }

                CrudResult<AudioClipRecord> added = await _library.AddAsync(_kind, path, null, null, CancellationToken.None);
                if (!added.Ok)
                {
                    ShowFailure("Adding a file", added.Error, added.Detail);
                    return;
                }

                Refresh();
            }
            finally
            {
                AddButton.SetEnabled(true);
            }
        }

        private void ShowProblem(string key)
        {
            _problem.TextKey = key;
            _problem.style.display = DisplayStyle.Flex;
        }

        // A CRUD failure as its caller (system design 6.31): the user is told, the log keeps the detail.
        private void ShowFailure(string what, CrudError error, string detail)
        {
            ShowProblem(error == CrudError.Busy ? AudioClipStrings.DeleteInUse : error == CrudError.Invalid ? AudioClipStrings.ProblemUnreadable : AudioClipStrings.ProblemStorage);
            UiServices.Find(this)?.Log?.Warning($"{what} failed ({error}): {detail}");
        }

        private static VisualElement Columns(AudioClipKind kind, bool shortcuts)
        {
            var columns = new VisualElement();
            columns.AddToClassList(ColumnsClass);
            columns.Add(Cell(MoveColumn));
            columns.Add(Cell(PlayColumn));
            columns.Add(Heading(AudioClipStrings.Name, NameColumn));
            columns.Add(Heading(AudioClipStrings.Length, LengthColumn));
            if (kind == AudioClipKind.Bgm)
            {
                VisualElement loop = Cell(LoopColumn);
                loop.Add(Ui.Text(AudioClipStrings.Loop, "vv-text-caption"));
                loop.Add(new HelpHint(AudioClipStrings.LoopDescription));
                columns.Add(loop);
            }

            columns.Add(Heading(AudioClipStrings.Gain, GainColumn));
            if (shortcuts)
            {
                VisualElement shortcut = Cell(ShortcutColumn);
                shortcut.Add(Ui.Text(ShortcutStrings.Column, "vv-text-caption"));
                shortcut.Add(new HelpHint(ShortcutStrings.Description));
                columns.Add(shortcut);
            }

            columns.Add(Cell(ActionsColumn));
            return columns;
        }

        private static VisualElement Heading(string key, string column)
        {
            VisualElement cell = Cell(column);
            cell.Add(Ui.Text(key, "vv-text-caption"));
            return cell;
        }

        internal static VisualElement Cell(string column)
        {
            var cell = new VisualElement();
            cell.AddToClassList(CellClass);
            cell.AddToClassList(column);
            return cell;
        }
    }
}
