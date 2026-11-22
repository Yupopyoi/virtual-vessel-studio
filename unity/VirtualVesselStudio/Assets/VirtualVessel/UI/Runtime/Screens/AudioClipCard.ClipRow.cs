using System.Collections.Generic;
using UnityEngine.UIElements;
using VirtualVessel.Audio.Clips;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Shortcuts;
using VirtualVessel.UI.Tooltips;

namespace VirtualVessel.UI.Screens
{
    /// <summary>One row of the Card, and the grip at its start (setup.md 14).</summary>
    public sealed partial class AudioClipCard
    {
        /// <summary>The "≡" mark at a row's start, with room around it for the pointer and the tooltip.</summary>
        private sealed class GripHandle : VisualElement, ITooltipSource
        {
            private readonly string _tooltipKey;

            public GripHandle(string tooltipKey)
            {
                _tooltipKey = tooltipKey;
                AddToClassList(GripClass);
                Add(new Icon(IconName.Grip));
            }

            string ITooltipSource.CurrentTooltipKey => _tooltipKey;

            string ITooltipSource.Shortcut => null;
        }

        /// <summary>One registered file's row.</summary>
        public sealed class ClipRow
        {
            private readonly AudioClipCard _card;
            private readonly Label _name;
            private readonly TextInput _rename;
            private readonly StatusChip _state;

            internal ClipRow(AudioClipCard card, AudioClipRecord record, AudioClipKind kind, string shownName, IGlobalShortcuts shortcuts)
            {
                ShownName = shownName;
                _card = card;
                Record = record;
                Root = new VisualElement();
                Root.AddToClassList(RowClass);

                // A plain mark, not a button, as in the layer list: it takes the pointer for dragging but not the focus.
                Grip = new GripHandle(kind == AudioClipKind.Bgm ? AudioClipStrings.BgmMove : AudioClipStrings.Move);
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
                Label lengthText = QuietText.Of(new Label(LengthText(record.File.Duration)));
                lengthText.AddToClassList("vv-text-body");
                lengthText.AddToClassList("vv-mono");
                length.Add(lengthText);
                Root.Add(length);

                if (kind == AudioClipKind.Bgm)
                {
                    LoopToggle = new SwitchToggle();
                    LoopToggle.SetValueWithoutNotify(record.Settings.Loop);
                    LoopToggle.TooltipKey = AudioClipStrings.LoopHelp;
                    LoopToggle.RegisterValueChangedCallback(evt => _card.Change(Record, new AudioClipChange(loop: evt.newValue)));
                    VisualElement loop = Cell(LoopColumn);
                    loop.Add(LoopToggle);
                    Root.Add(loop);
                }

                Gain = Ui.Slider(null, VirtualVessel.Audio.AudioSourceOptions.MinGainDb, VirtualVessel.Audio.AudioSourceOptions.MaxGainDb, "dB");
                Gain.Step = 1f;
                Gain.Signed = true;
                Gain.SetValueWithoutNotify(record.Settings.GainDb);
                Gain.RegisterValueChangedCallback(evt => _card.Change(Record, new AudioClipChange(gainDb: evt.newValue)));
                VisualElement gain = Cell(GainColumn);
                gain.Add(Gain);
                Root.Add(gain);

                if (shortcuts != null)
                {
                    Shortcut = new ShortcutButton(record.Settings.Shortcut, shortcuts);
                    Shortcut.Chosen += keys => _card.ChooseShortcut(Record, keys);
                    VisualElement shortcut = Cell(ShortcutColumn);
                    shortcut.Add(Shortcut);
                    Root.Add(shortcut);
                }

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

                Controls.Add(Gain);
                if (Shortcut != null)
                {
                    Controls.Add(Shortcut.Button);
                }

                Controls.AddRange(new VisualElement[] { RenameButton, SaveButton, DeleteButton });
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

            /// <summary>SE only, with the global keys: the keys that play it from any app.</summary>
            public ShortcutButton Shortcut { get; }

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

            /// <param name="heldElsewhere">Its keys are another app's, so they do not play it; told by shape and text.</param>
            internal void ShowEffect(bool ready, bool heldElsewhere)
            {
                PlayButton.SetEnabled(ready);
                PlayButton.TooltipKey = ready ? AudioClipStrings.EffectPlay : AudioClipStrings.EffectLoading;
                _state.style.display = heldElsewhere ? DisplayStyle.Flex : DisplayStyle.None;
                _state.Status = StatusKind.Warning;
                _state.TextKey = ShortcutStrings.HeldElsewhere;
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
