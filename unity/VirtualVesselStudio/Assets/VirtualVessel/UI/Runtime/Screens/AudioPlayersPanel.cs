using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine.UIElements;
using VirtualVessel.Audio;
using VirtualVessel.Audio.Clips;
using VirtualVessel.Audio.Playback;
using VirtualVessel.ProjectData.Records;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Shortcuts;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of the live screen's BGM and SE (docs/&lt;lang&gt;/ui/screens/live.md).</summary>
    [StringKeys]
    public static class AudioPlayersStrings
    {
        public const string Title = "live.players.title";
        public const string Bgm = "live.players.bgm";
        public const string Effects = "live.players.effects";
        public const string Play = "live.players.play";
        public const string Stop = "live.players.stop";
        public const string Switch = "live.players.switch";
        public const string Ducking = "live.players.ducking";
        public const string DuckingHelp = "live.players.ducking.help";
        public const string Volume = "live.players.volume";
        public const string EffectsVolume = "live.players.effects-volume";
        public const string StopEffects = "live.players.stop-effects";
        public const string NoBgm = "live.players.no-bgm";
        public const string NoEffects = "live.players.no-effects";
    }

    /// <summary>What the live screen's BGM button does.</summary>
    public enum BgmAction
    {
        Play,
        Stop,
        Switch,
    }

    /// <summary>
    /// BGM and SE on the live screen (BGM and SE detailed design; live.md 2): choose a track, play, stop, or switch to
    /// it, see how far it is, set the BGM's and the sound effects' volumes, lower BGM while talking, and press a sound
    /// effect's button. Registering files is the setup screen's.
    /// </summary>
    public sealed class AudioPlayersPanel : VisualElement
    {
        public const string UssClass = "vv-players";
        public const string RowClass = UssClass + "__row";
        public const string LabelClass = UssClass + "__label";
        public const string PadClass = UssClass + "__pad";
        public const string TimeClass = UssClass + "__time";

        /// <summary>How often the time and the library are looked at again.</summary>
        public const long RefreshIntervalMs = 250;

        private readonly IAudioClipLibrary _library;
        private readonly IBgmPlayer _bgm;
        private readonly ISoundEffectPlayer _effects;
        private readonly IAudioSource _music;
        private readonly IAudioSource _effectsSource;
        private string _language;
        private readonly VisualElement _pad = new VisualElement();
        private readonly Label _time;
        private readonly Label _noBgm;
        private readonly Label _noEffects;
        private readonly List<AudioClipRecord> _tracks = new List<AudioClipRecord>();
        private readonly List<(ActionButton Button, string Id)> _effectButtons = new List<(ActionButton, string)>();
        private readonly IAudioService _audio;
        private string _shownPlaying;
        private volatile bool _changed = true;

        /// <param name="music">The BGM source, whose volume the BGM row's slider sets; null leaves the slider out.</param>
        /// <param name="effectsSource">The sound effects' source, for the effects row's slider; null leaves it out.</param>
        /// <param name="audio">For lowering BGM while the user talks (live.md 2); null leaves its toggle out.</param>
        public AudioPlayersPanel(IAudioClipLibrary library, IBgmPlayer bgm, ISoundEffectPlayer effects, IAudioSource music, IAudioSource effectsSource = null, IAudioService audio = null)
        {
            _audio = audio;
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _bgm = bgm;
            _effects = effects;
            _music = music;
            _effectsSource = effectsSource;
            AddToClassList(UssClass);
            Card card = Ui.Card(AudioPlayersStrings.Title);
            Add(card);

            var bgmRow = new VisualElement();
            bgmRow.AddToClassList(RowClass);
            bgmRow.Add(Ui.Text(AudioPlayersStrings.Bgm, LabelClass, "vv-text-body"));
            Tracks = Ui.Select(null);
            Tracks.TooltipKey = AudioPlayersStrings.Bgm;
            bgmRow.Add(Tracks);
            PlayButton = Ui.Button(AudioPlayersStrings.Play, ButtonVariant.Primary);
            PlayButton.clicked += PressPlay;
            bgmRow.Add(PlayButton);
            // Hidden, not removed, when nothing plays, so the time and the slider after it do not move.
            State = Ui.Chip(StatusKind.Live, AudioClipStrings.BgmPlaying);
            bgmRow.Add(State);
            _time = QuietText.Of(new Label());
            _time.AddToClassList(TimeClass);
            _time.AddToClassList("vv-mono");
            bgmRow.Add(_time);
            if (music != null)
            {
                Volume = VolumeSlider(music, AudioPlayersStrings.Volume);
                bgmRow.Add(Volume);
            }

            if (audio != null)
            {
                DuckingToggle = Ui.Toggle(AudioPlayersStrings.Ducking);
                DuckingToggle.TooltipKey = AudioPlayersStrings.DuckingHelp;
                DuckingToggle.RegisterValueChangedCallback(evt => audio.SetBgmMix(audio.BgmMix.Value.WithDucking(evt.newValue)));
                bgmRow.Add(DuckingToggle);
            }

            card.Add(bgmRow);
            _noBgm = Ui.Text(AudioPlayersStrings.NoBgm, LabelClass, "vv-text-body");
            card.Add(_noBgm);

            var effectsRow = new VisualElement();
            effectsRow.AddToClassList(RowClass);
            effectsRow.Add(Ui.Text(AudioPlayersStrings.Effects, LabelClass, "vv-text-body"));
            _pad.AddToClassList(PadClass);
            effectsRow.Add(_pad);
            if (effectsSource != null)
            {
                EffectsVolume = VolumeSlider(effectsSource, AudioPlayersStrings.EffectsVolume);
                effectsRow.Add(EffectsVolume);
            }

            StopEffectsButton = Ui.Button(AudioPlayersStrings.StopEffects);
            StopEffectsButton.clicked += () => _effects?.StopAll();
            effectsRow.Add(StopEffectsButton);
            card.Add(effectsRow);
            _noEffects = Ui.Text(AudioPlayersStrings.NoEffects, LabelClass, "vv-text-body");
            card.Add(_noEffects);

            PanelLifetime.WhileAttached(this, Attach);
        }

        public SelectField Tracks { get; }

        public ActionButton PlayButton { get; }

        /// <summary>The BGM's state beside the button, by its shape and text.</summary>
        public StatusChip State { get; }

        /// <summary>The BGM source's volume; null without the source.</summary>
        public ValueSlider Volume { get; }

        /// <summary>Lowering BGM while the user talks; null without the audio service.</summary>
        public SwitchToggle DuckingToggle { get; }

        /// <summary>The sound effects source's volume; null without the source.</summary>
        public ValueSlider EffectsVolume { get; }

        public ActionButton StopEffectsButton { get; }

        /// <summary>The sound effects' buttons, in the order they were added.</summary>
        public IReadOnlyList<ActionButton> EffectButtons => _effectButtons.ConvertAll(entry => entry.Button);

        /// <summary>The time through the track and its length, as "0:12 / 0:47"; empty when unknown. Pure.</summary>
        public static string TimeText(float? progress, TimeSpan length)
        {
            if (progress == null || length <= TimeSpan.Zero)
            {
                return string.Empty;
            }

            return AudioClipCard.LengthText(TimeSpan.FromSeconds(Math.Max(1, progress.Value * length.TotalSeconds))) + " / " + AudioClipCard.LengthText(length);
        }

        /// <summary>What the BGM button does: play the chosen track, stop the one playing, or switch to the chosen one. Pure.</summary>
        public static BgmAction ActionFor(bool playing, string playingId, string chosen)
        {
            if (!playing)
            {
                return BgmAction.Play;
            }

            return string.IsNullOrEmpty(chosen) || chosen == playingId ? BgmAction.Stop : BgmAction.Switch;
        }

        /// <summary>Which track the list shows: the one playing, else the one chosen if it is still there, else the first. Pure.</summary>
        public static string ChosenTrack(IReadOnlyList<string> ids, string playing, string chosen)
        {
            if (playing != null && ids.Contains(playing))
            {
                return playing;
            }

            return chosen != null && ids.Contains(chosen) ? chosen : ids.FirstOrDefault();
        }

        private IDisposable Attach(UiServices services)
        {
            void OnChanged(AudioClipKind kind)
            {
                // Raised on any thread: read again on this one.
                _changed = true;
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

                ShowPlayers();
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

            if (_music != null)
            {
                subscriptions.Add(_music.Options.Subscribe(options => Volume.SetValueWithoutNotify(options.GainDb)));
            }

            if (_audio != null)
            {
                DuckingToggle.SetValueWithoutNotify(_audio.BgmMix.Value.Ducking);
                subscriptions.Add(_audio.BgmMix.Subscribe(options => DuckingToggle.SetValueWithoutNotify(options.Ducking)));
            }

            Tracks.RegisterValueChangedCallback(OnTrackChosen);
            subscriptions.Add(new DisposeAction(() => Tracks.UnregisterValueChangedCallback(OnTrackChosen)));

            if (_effectsSource != null)
            {
                subscriptions.Add(_effectsSource.Options.Subscribe(options => EffectsVolume.SetValueWithoutNotify(options.GainDb)));
            }

            return new DisposeAll(subscriptions);
        }

        private void OnTrackChosen(ChangeEvent<string> evt)
        {
            ShowPlayers();
        }

        private void Refresh()
        {
            _changed = false;
            CrudResult<IReadOnlyList<AudioClipRecord>> tracks = _library.List(AudioClipKind.Bgm);
            CrudResult<IReadOnlyList<AudioClipRecord>> effects = _library.List(AudioClipKind.SoundEffect);
            _tracks.Clear();
            _tracks.AddRange(tracks.Ok ? tracks.Value : Array.Empty<AudioClipRecord>());
            Tracks.SetChoices(_tracks.Select(track => SelectChoice.FromText(track.Id, track.NameIn(_language))).ToArray());
            // A track the user chose to switch to is kept while another plays.
            string[] ids = _tracks.Select(track => track.Id).ToArray();
            string chosen = ChosenTrack(ids, ids.Contains(Tracks.value) ? null : _bgm?.Status.Value.TrackId, Tracks.value);
            Tracks.SetValueWithoutNotify(chosen ?? string.Empty);
            _noBgm.style.display = _tracks.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            Tracks.parent.style.display = _tracks.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;

            _pad.Clear();
            _effectButtons.Clear();
            IReadOnlyList<AudioClipRecord> list = effects.Ok ? effects.Value : Array.Empty<AudioClipRecord>();
            foreach (AudioClipRecord effect in list)
            {
                // The tooltip tells its keys, which play it from any app (BGM and SE detailed design 8.3).
                var button = new ActionButton(null) { PlainText = effect.NameIn(_language), Shortcut = KeyChord.Parse(effect.Settings.Shortcut)?.ToString() };
                string id = effect.Id;
                PlaySoundEffectCommand command = _effects != null ? new PlaySoundEffectCommand(_effects, id) : null;
                button.clicked += () => command?.Execute();
                _pad.Add(button);
                _effectButtons.Add((button, id));
            }

            _noEffects.style.display = list.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            StopEffectsButton.parent.style.display = list.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            ShowPlayers();
        }

        private void ShowPlayers()
        {
            BgmStatus status = _bgm?.Status.Value ?? default;
            (bool playing, bool failed) = AudioClipCard.RowState(status, status.TrackId);

            // A track started elsewhere (the setup screen) is shown as the one chosen.
            if (playing && status.TrackId != _shownPlaying && _tracks.Exists(t => t.Id == status.TrackId))
            {
                Tracks.SetValueWithoutNotify(status.TrackId);
            }

            _shownPlaying = playing ? status.TrackId : null;

            // Another track can be chosen while one plays; the button then switches to it, crossing the two
            // (BGM and SE detailed design 8.5).
            BgmAction action = ActionFor(playing, status.TrackId, Tracks.value);
            PlayButton.TextKey = action == BgmAction.Stop ? AudioPlayersStrings.Stop : (action == BgmAction.Switch ? AudioPlayersStrings.Switch : AudioPlayersStrings.Play);
            PlayButton.Variant = action == BgmAction.Stop ? ButtonVariant.Secondary : ButtonVariant.Primary;
            PlayButton.SetEnabled(_bgm != null && (playing || !string.IsNullOrEmpty(Tracks.value)));
            State.style.visibility = playing || failed ? Visibility.Visible : Visibility.Hidden;
            State.Status = failed ? StatusKind.Error : StatusKind.Live;
            State.TextKey = failed ? AudioClipStrings.BgmFailed : AudioClipStrings.BgmPlaying;
            AudioClipRecord track = _tracks.Find(t => t.Id == status.TrackId);
            _time.text = playing && track != null ? TimeText(_bgm.Progress, track.File.Duration) : string.Empty;
            foreach ((ActionButton button, string id) in _effectButtons)
            {
                button.SetEnabled(_effects != null && _effects.IsReady(id));
            }
        }

        // A source's volume, the same value as its mixer row's.
        private static ValueSlider VolumeSlider(IAudioSource source, string tooltipKey)
        {
            ValueSlider slider = Ui.Slider(null, AudioSourceOptions.MinGainDb, AudioSourceOptions.MaxGainDb, "dB");
            slider.Step = 1f;
            slider.Signed = true;
            slider.TooltipKey = tooltipKey;
            slider.SetValueWithoutNotify(source.Options.Value.GainDb);
            slider.RegisterValueChangedCallback(evt => source.SetOptions(source.Options.Value.WithGain(evt.newValue)));
            return slider;
        }

        private void PressPlay()
        {
            if (_bgm == null)
            {
                return;
            }

            BgmStatus status = _bgm.Status.Value;
            if (ActionFor(AudioClipCard.RowState(status, status.TrackId).Playing, status.TrackId, Tracks.value) == BgmAction.Stop)
            {
                _bgm.Stop();
                return;
            }

            AudioClipRecord track = _tracks.Find(t => t.Id == Tracks.value);
            if (track != null)
            {
                _bgm.Play(new SingleTrack(track.Id, track.Settings.Loop));
            }
        }
    }
}
