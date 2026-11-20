using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.Audio.Clips;
using VirtualVessel.Audio.Playback;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Frames;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Tooltips;

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
    public sealed class AudioClipCard : VisualElement
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

        public AudioClipCard(AudioClipKind kind, IAudioClipLibrary library, IBgmPlayer bgm, ISoundEffectPlayer effects, IFileDialog files)
        {
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

            card.Add(Columns(kind));
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
                _effects?.Play(record.Id);
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
                _bgm.Play(new SingleTrack(record.Id, record.Loop));
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
                var row = new ClipRow(this, record, _kind, record.NameIn(_language));
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
                    row.ShowEffect(_effects != null && _effects.IsReady(row.Record.Id));
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

        internal void StartDrag(ClipRow row, PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            _drag = new Drag(row, evt.position.y);
            row.Grip.CapturePointer(evt.pointerId);
        }

        internal void DragTo(PointerMoveEvent evt)
        {
            if (_drag == null)
            {
                return;
            }

            if (!_drag.Moving)
            {
                if (Mathf.Abs(evt.position.y - _drag.StartY) < DragThreshold)
                {
                    return;
                }

                _drag.Moving = true;
                _drag.Row.Root.AddToClassList(DraggingClass);

                // The rows do not move during a drag, so their places are measured once.
                foreach (ClipRow row in _shown)
                {
                    _drag.Centers.Add(row.Root.worldBound.center.y);
                    _drag.Edges.Add(row.Root.layout.yMin);
                }

                _drag.Edges.Add(_shown[_shown.Count - 1].Root.layout.yMax);
            }

            _drag.DropIndex = LayerListMath.DropIndex(_drag.Centers, evt.position.y);
            _drop.style.top = _drag.Edges[_drag.DropIndex] - 1;
            _drop.style.display = DisplayStyle.Flex;
        }

        internal void Drop(PointerUpEvent evt)
        {
            Drag drag = _drag;
            if (drag == null)
            {
                return;
            }

            if (drag.Row.Grip.HasPointerCapture(evt.pointerId))
            {
                drag.Row.Grip.ReleasePointer(evt.pointerId);
            }

            EndDrag();
            int from = _shown.IndexOf(drag.Row);
            int to = LayerListMath.TargetPosition(from, drag.DropIndex);
            if (drag.Moving && to != from)
            {
                Move(drag.Row.Record, to);
            }
        }

        /// <summary>
        /// Ctrl+Up and Ctrl+Down with the focus in a row move it by one, as in the layer list. The grip is only for the
        /// pointer, so the keys work from the row's buttons; the rename field keeps them for its text.
        /// </summary>
        internal void Step(ClipRow row, KeyDownEvent evt)
        {
            int step = evt.keyCode == KeyCode.UpArrow ? -1 : evt.keyCode == KeyCode.DownArrow ? 1 : 0;
            if (step == 0 || !(evt.ctrlKey || evt.commandKey) || (evt.target is VisualElement target && row.NameInput.Contains(target)))
            {
                return;
            }

            // Kept from the focus navigation too, which would otherwise move on from the old row after it is rebuilt.
            evt.StopPropagation();
            focusController?.IgnoreEvent(evt);
            int from = _shown.IndexOf(row);
            int to = from + step;
            if (from >= 0 && to >= 0 && to < _shown.Count)
            {
                // Contains does not count the element itself.
                var focused = evt.target as VisualElement;
                Move(row.Record, to, row.Controls.FindIndex(control => control == focused || control.Contains(focused)));
            }
        }

        private void EndDrag()
        {
            _drag?.Row.Root.RemoveFromClassList(DraggingClass);
            _drag = null;
            _drop.style.display = DisplayStyle.None;
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

        private static VisualElement Columns(AudioClipKind kind)
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

        private sealed class Drag
        {
            public Drag(ClipRow row, float startY)
            {
                Row = row;
                StartY = startY;
            }

            public ClipRow Row { get; }

            public float StartY { get; }

            public bool Moving { get; set; }

            public int DropIndex { get; set; }

            /// <summary>Row centers in panel space, for choosing the landing row.</summary>
            public List<float> Centers { get; } = new List<float>();

            /// <summary>Row tops in the list, then the bottom of the last row, for drawing the line.</summary>
            public List<float> Edges { get; } = new List<float>();
        }

        /// <summary>The "≡" mark at a row's start, with room around it for the pointer and the tooltip.</summary>
        private sealed class GripHandle : VisualElement, ITooltipSource
        {
            public GripHandle()
            {
                AddToClassList(GripClass);
                Add(new Icon(IconName.Grip));
            }

            string ITooltipSource.CurrentTooltipKey => AudioClipStrings.Move;

            string ITooltipSource.Shortcut => null;
        }

        /// <summary>One registered file's row.</summary>
        public sealed class ClipRow
        {
            private readonly AudioClipCard _card;
            private readonly Label _name;
            private readonly TextInput _rename;
            private readonly StatusChip _state;

            internal ClipRow(AudioClipCard card, AudioClipRecord record, AudioClipKind kind, string shownName)
            {
                ShownName = shownName;
                _card = card;
                Record = record;
                Root = new VisualElement();
                Root.AddToClassList(RowClass);

                // A plain mark, not a button, as in the layer list: it takes the pointer for dragging but not the focus.
                Grip = new GripHandle();
                Grip.RegisterCallback<PointerDownEvent>(evt => _card.StartDrag(this, evt));
                Grip.RegisterCallback<PointerMoveEvent>(evt => _card.DragTo(evt));
                Grip.RegisterCallback<PointerUpEvent>(evt => _card.Drop(evt));
                Root.RegisterCallback<KeyDownEvent>(evt => _card.Step(this, evt), TrickleDown.TrickleDown);
                VisualElement move = Cell(MoveColumn);
                move.Add(Grip);
                Root.Add(move);

                PlayButton = Ui.IconButton(IconName.Play).WithTooltip(kind == AudioClipKind.Bgm ? AudioClipStrings.BgmPlay : AudioClipStrings.EffectPlay);
                PlayButton.clicked += () => _card.Press(Record);
                VisualElement play = Cell(PlayColumn);
                play.Add(PlayButton);
                Root.Add(play);

                // The name and its rename field share one place, so renaming happens where the name is.
                VisualElement name = Cell(NameColumn);
                _name = QuietText.Of(new Label(shownName));
                _name.AddToClassList(NameTextClass);
                _name.AddToClassList("vv-text-body");
                _name.tooltip = shownName;
                name.Add(_name);
                _rename = Ui.TextField(null);
                _rename.TooltipKey = AudioClipStrings.Name;
                _rename.MaxLength = AudioClipLibrary.MaxNameLength;
                _rename.style.display = DisplayStyle.None;
                name.Add(_rename);
                _state = Ui.Chip(StatusKind.Live, AudioClipStrings.BgmPlaying);
                _state.style.display = DisplayStyle.None;
                name.Add(_state);
                Root.Add(name);

                VisualElement length = Cell(LengthColumn);
                Label lengthText = QuietText.Of(new Label(LengthText(record.Duration)));
                lengthText.AddToClassList("vv-text-body");
                lengthText.AddToClassList("vv-mono");
                length.Add(lengthText);
                Root.Add(length);

                if (kind == AudioClipKind.Bgm)
                {
                    LoopToggle = new SwitchToggle();
                    LoopToggle.SetValueWithoutNotify(record.Loop);
                    LoopToggle.TooltipKey = AudioClipStrings.LoopDescription;
                    LoopToggle.RegisterValueChangedCallback(evt => _card.Change(Record, new AudioClipChange(loop: evt.newValue)));
                    VisualElement loop = Cell(LoopColumn);
                    loop.Add(LoopToggle);
                    Root.Add(loop);
                }

                Gain = Ui.Slider(null, VirtualVessel.Audio.AudioSourceOptions.MinGainDb, VirtualVessel.Audio.AudioSourceOptions.MaxGainDb, "dB");
                Gain.Step = 1f;
                Gain.Signed = true;
                Gain.SetValueWithoutNotify(record.GainDb);
                Gain.RegisterValueChangedCallback(evt => _card.Change(Record, new AudioClipChange(gainDb: evt.newValue)));
                VisualElement gain = Cell(GainColumn);
                gain.Add(Gain);
                Root.Add(gain);

                RenameButton = Ui.Button(AudioClipStrings.Rename);
                RenameButton.clicked += StartRename;
                SaveButton = Ui.Button(AudioClipStrings.Save, ButtonVariant.Primary);
                SaveButton.style.display = DisplayStyle.None;
                SaveButton.clicked += SaveName;
                DeleteButton = Ui.Button(AudioClipStrings.Delete);
                DeleteButton.clicked += () => _card.Delete(Record);
                VisualElement actions = Cell(ActionsColumn);
                actions.Add(RenameButton);
                actions.Add(SaveButton);
                actions.Add(DeleteButton);
                Root.Add(actions);

                Controls = new List<VisualElement> { PlayButton };
                if (LoopToggle != null)
                {
                    Controls.Add(LoopToggle);
                }

                Controls.AddRange(new VisualElement[] { Gain, RenameButton, SaveButton, DeleteButton });
            }

            public AudioClipRecord Record { get; }

            /// <summary>The name as shown, in the screen's language for a built-in file not renamed.</summary>
            public string ShownName { get; }

            public VisualElement Root { get; }

            /// <summary>Dragged to reorder; Ctrl+Up and Ctrl+Down with the focus in the row do the same by keys.</summary>
            public VisualElement Grip { get; }

            public IconButton PlayButton { get; }

            /// <summary>BGM only.</summary>
            public SwitchToggle LoopToggle { get; }

            public ValueSlider Gain { get; }

            public ActionButton RenameButton { get; }

            public ActionButton SaveButton { get; }

            public ActionButton DeleteButton { get; }

            public TextInput NameInput => _rename;

            /// <summary>The row's controls in order, to find the same one again after the rows are rebuilt.</summary>
            internal List<VisualElement> Controls { get; }

            /// <summary>Playing or failed, shown by its shape and text beside the name.</summary>
            public StatusChip State => _state;

            internal void ShowBgm(bool playing, bool failed, bool holdsFile, bool canPlay)
            {
                PlayButton.IconName = playing ? IconName.Stop : IconName.Play;
                PlayButton.TooltipKey = playing ? AudioClipStrings.BgmStop : AudioClipStrings.BgmPlay;
                PlayButton.SetEnabled(canPlay);
                _state.style.display = playing || failed ? DisplayStyle.Flex : DisplayStyle.None;
                _state.Status = failed ? StatusKind.Error : StatusKind.Live;
                _state.TextKey = failed ? AudioClipStrings.BgmFailed : AudioClipStrings.BgmPlaying;

                // The track that plays keeps its file open until it has faded out.
                DeleteButton.SetEnabled(!holdsFile);
                DeleteButton.TooltipKey = holdsFile ? AudioClipStrings.DeleteInUse : null;
            }

            internal void ShowEffect(bool ready)
            {
                PlayButton.SetEnabled(ready);
                PlayButton.TooltipKey = ready ? AudioClipStrings.EffectPlay : AudioClipStrings.EffectLoading;
            }

            /// <summary>Focuses a control by its place in <see cref="Controls"/>; the rename button if it cannot.</summary>
            internal void Focus(int control)
            {
                VisualElement target = control < Controls.Count ? Controls[control] : null;
                (target != null && target.canGrabFocus ? target : RenameButton).Focus();
            }

            private void StartRename()
            {
                _rename.value = ShownName;
                _rename.ErrorKey = null;
                _rename.style.display = DisplayStyle.Flex;
                _name.style.display = DisplayStyle.None;
                RenameButton.style.display = DisplayStyle.None;
                SaveButton.style.display = DisplayStyle.Flex;
                _rename.FocusInput();
            }

            private void SaveName()
            {
                if (AudioClipLibrary.CleanName(_rename.value) == null)
                {
                    _rename.ErrorKey = AudioClipStrings.NameRequired;
                    return;
                }

                // Saving the shown name as it is keeps a built-in file's names per language.
                if (_rename.value.Trim() == ShownName)
                {
                    _card.Refresh();
                    return;
                }

                _card.Change(Record, new AudioClipChange(name: _rename.value));
            }
        }
    }
}
