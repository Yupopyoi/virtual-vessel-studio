using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using VirtualVessel.Audio.Clips;
using VirtualVessel.Audio.Playback;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Localization;

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
    /// files in rows with their play button, name, length, loop (BGM), and volume, renaming and deleting them, and
    /// adding a WAV or MP3 file. What plays is the players' state; the Card only shows it and asks.
    /// </summary>
    public sealed class AudioClipCard : VisualElement
    {
        public const string UssClass = "vv-clips";
        public const string ColumnsClass = UssClass + "__columns";
        public const string RowClass = UssClass + "__row";
        public const string CellClass = UssClass + "__cell";
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

        private readonly AudioClipKind _kind;
        private readonly IAudioClipLibrary _library;
        private readonly IBgmPlayer _bgm;
        private readonly ISoundEffectPlayer _effects;
        private readonly IFileDialog _files;
        private readonly VisualElement _rows = new VisualElement();
        private readonly Label _empty;
        private readonly StatusChip _problem;
        private readonly List<ClipRow> _shown = new List<ClipRow>();
        private volatile bool _changed = true;

        public AudioClipCard(AudioClipKind kind, IAudioClipLibrary library, IBgmPlayer bgm, ISoundEffectPlayer effects, IFileDialog files)
        {
            _kind = kind;
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _bgm = bgm;
            _effects = effects;
            _files = files;
            AddToClassList(UssClass);
            Card card = Ui.Card(kind == AudioClipKind.Bgm ? AudioClipStrings.BgmTitle : AudioClipStrings.EffectsTitle);
            Add(card);
            if (kind == AudioClipKind.SoundEffect)
            {
                StopAllButton = Ui.Button(AudioClipStrings.EffectsStop);
                StopAllButton.clicked += () => _effects?.StopAll();
                card.HeaderEnd.Add(StopAllButton);
            }

            card.Add(Columns(kind));
            card.Add(_rows);
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

        /// <summary>Reads the library again and shows it.</summary>
        public void Refresh()
        {
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
                var row = new ClipRow(this, record, _kind);
                _shown.Add(row);
                _rows.Add(row.Root);
            }

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

                CrudResult<AudioClipRecord> added = await _library.AddAsync(_kind, path, null, CancellationToken.None);
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

        private static VisualElement Columns(AudioClipKind kind)
        {
            var columns = new VisualElement();
            columns.AddToClassList(ColumnsClass);
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

        /// <summary>One registered file's row.</summary>
        public sealed class ClipRow
        {
            private readonly AudioClipCard _card;
            private readonly Label _name;
            private readonly TextInput _rename;
            private readonly StatusChip _state;

            internal ClipRow(AudioClipCard card, AudioClipRecord record, AudioClipKind kind)
            {
                _card = card;
                Record = record;
                Root = new VisualElement();
                Root.AddToClassList(RowClass);

                PlayButton = Ui.IconButton(IconName.Play).WithTooltip(kind == AudioClipKind.Bgm ? AudioClipStrings.BgmPlay : AudioClipStrings.EffectPlay);
                PlayButton.clicked += () => _card.Press(Record);
                VisualElement play = Cell(PlayColumn);
                play.Add(PlayButton);
                Root.Add(play);

                // The name and its rename field share one place, so renaming happens where the name is.
                VisualElement name = Cell(NameColumn);
                _name = QuietText.Of(new Label(record.Name));
                _name.AddToClassList(NameTextClass);
                _name.AddToClassList("vv-text-body");
                _name.tooltip = record.Name;
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
            }

            public AudioClipRecord Record { get; }

            public VisualElement Root { get; }

            public IconButton PlayButton { get; }

            /// <summary>BGM only.</summary>
            public SwitchToggle LoopToggle { get; }

            public ValueSlider Gain { get; }

            public ActionButton RenameButton { get; }

            public ActionButton SaveButton { get; }

            public ActionButton DeleteButton { get; }

            public TextInput NameInput => _rename;

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

            private void StartRename()
            {
                _rename.value = Record.Name;
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

                _card.Change(Record, new AudioClipChange(name: _rename.value));
            }
        }
    }
}
