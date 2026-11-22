using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using VirtualVessel.Audio;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of the audio Card and the live screen's meters (docs/&lt;lang&gt;/ui/screens/setup.md 11, live.md).</summary>
    [StringKeys]
    public static class AudioStrings
    {
        public const string Title = "setup.audio.title";
        public const string Unavailable = "setup.audio.unavailable";
        public const string Microphone = "setup.audio.microphone";
        public const string GameAudio = "setup.audio.game";
        public const string Source = "setup.audio.source";
        public const string Device = "setup.audio.device";
        public const string DeviceHelp = "setup.audio.device.help";
        public const string NoDevice = "setup.audio.device.none";
        public const string MissingDevice = "setup.audio.device.missing";
        public const string ApplicationSound = "setup.audio.device.application";
        public const string Gain = "setup.audio.gain";
        public const string GainHelp = "setup.audio.gain.help";
        public const string Mute = "setup.audio.mute";
        public const string MuteHelp = "setup.audio.mute.help";
        public const string Level = "setup.audio.level";
        public const string LevelHelp = "setup.audio.level.help";
        public const string Off = "setup.audio.state.off";
        public const string Starting = "setup.audio.state.starting";
        public const string Running = "setup.audio.state.running";
        public const string NotFound = "setup.audio.state.not-found";
        public const string Failed = "setup.audio.state.failed";
        public const string Muted = "setup.audio.state.muted";
        public const string ProblemNotFound = "setup.audio.problem.not-found";
        public const string ProblemInUse = "setup.audio.problem.in-use";
        public const string ProblemAccessDenied = "setup.audio.problem.access-denied";
        public const string ProblemFailed = "setup.audio.problem.failed";
        public const string OpenPrivacy = "setup.audio.open-privacy";
        public const string Monitored = "setup.audio.monitored";
        public const string MonitoredHelp = "setup.audio.monitored.help";
        public const string Denoise = "setup.audio.denoise";
        public const string DenoiseHelp = "setup.audio.denoise.help";
        public const string GameDeviceNote = "setup.audio.game-device";
        public const string Monitor = "setup.audio.monitor";
        public const string MonitorDevice = "setup.audio.monitor.device";
        public const string MonitorHelp = "setup.audio.monitor.help";
        public const string Delay = "setup.audio.delay";
        public const string DelayHelp = "setup.audio.delay.help";
        public const string Music = "setup.audio.music";
        public const string SoundEffect = "setup.audio.sound-effect";
        public const string LiveMicrophone = "live.audio.microphone";
        public const string LiveGameAudio = "live.audio.game";
        public const string LiveMusic = "live.audio.music";
        public const string LiveSoundEffect = "live.audio.sound-effect";
        public const string LiveMute = "live.audio.mute";
        public const string LiveUnmute = "live.audio.unmute";

        /// <summary>A source's name in the mixer. Pure.</summary>
        public static string NameOf(AudioSourceRole role)
        {
            return role switch
            {
                AudioSourceRole.Microphone => Microphone,
                AudioSourceRole.Music => Music,
                AudioSourceRole.SoundEffect => SoundEffect,
                _ => GameAudio,
            };
        }

        /// <summary>A source's name on the live screen. Pure.</summary>
        public static string LiveNameOf(AudioSourceRole role)
        {
            return role switch
            {
                AudioSourceRole.Microphone => LiveMicrophone,
                AudioSourceRole.Music => LiveMusic,
                AudioSourceRole.SoundEffect => LiveSoundEffect,
                _ => LiveGameAudio,
            };
        }

        /// <summary>A source's icon on the live screen, crossed out when muted: its shape says it, not its color. Pure.</summary>
        public static IconName IconOf(AudioSourceRole role, bool muted)
        {
            return role switch
            {
                AudioSourceRole.Music => muted ? IconName.MusicOff : IconName.Music,
                AudioSourceRole.SoundEffect => muted ? IconName.SpeakerOff : IconName.Speaker,
                _ => muted ? IconName.MicOff : IconName.Mic,
            };
        }
    }

    /// <summary>
    /// The audio on the setup screen as a mixer (audio detailed design 18; setup.md 11): a row per source, then
    /// what the user hears, in columns, so another source adds a row and not a block of rows. A row shows
    /// the source's state, and what to do when it has a problem.
    /// </summary>
    public sealed class AudioCard : VisualElement
    {
        public const string UssClass = "vv-audio-card";
        public const string NarrowClass = UssClass + "--narrow";
        public const string GroupClass = UssClass + "__group";
        public const string ColumnsClass = UssClass + "__columns";
        public const string RowClass = UssClass + "__row";
        public const string CellClass = UssClass + "__cell";
        public const string CellLabelClass = CellClass + "-label";
        public const string NameColumn = CellClass + "--name";
        public const string DeviceColumn = CellClass + "--device";
        public const string GainColumn = CellClass + "--gain";
        public const string ToggleColumn = CellClass + "--toggle";
        public const string DenoiseColumn = CellClass + "--denoise";
        public const string HelpCellClass = CellClass + "--help";
        public const string LevelColumn = CellClass + "--level";
        public const string EmptyCellClass = CellClass + "--empty";
        public const string NoteClass = UssClass + "__note";
        public const string HeadingClass = UssClass + "__heading";
        public const string ProblemClass = UssClass + "__problem";
        public const string PrivacyClass = UssClass + "__privacy";

        /// <summary>How often devices are listed again while the Card is shown, so a plugged-in one appears.</summary>
        public const long DeviceIntervalMs = 5000;

        /// <summary>Below this width, in px, the columns do not fit: a row wraps and each control is named.</summary>
        public const float NarrowWidth = 870f;

        private readonly IAudioService _audio;
        private readonly List<AudioSourceRow> _groups = new List<AudioSourceRow>();

        /// <param name="gameDeviceOnCapture">The game capture Card chooses the game audio's device, so this one only shows it.</param>
        public AudioCard(IAudioService audio, ISystemSettings systemSettings, bool gameDeviceOnCapture = false)
        {
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            AddToClassList(UssClass);
            Card card = Ui.Card(AudioStrings.Title);
            Add(card);
            card.Add(AudioMixerRow.Columns());
            foreach (IAudioSource source in audio.Sources)
            {
                var group = new AudioSourceRow(source, systemSettings, gameDeviceOnCapture && source.Role == AudioSourceRole.GameAudio);
                _groups.Add(group);
                card.Add(group.Root);
            }

            Monitor = new AudioMonitorRow(audio.Monitor);
            card.Add(Monitor.Root);
            // Recording and streaming get the sound later, to meet the avatar's motion (audio detailed design 8.5).
            Delay = Ui.Slider(null, 0, AudioSettings.MaxStreamDelayMs, "ms");
            Delay.Step = 10f;
            Delay.RegisterValueChangedCallback(evt => _audio.SetStreamDelay(TimeSpan.FromMilliseconds(evt.newValue)));
            SettingRow delay = Ui.SettingRow(AudioStrings.Delay, helpKey: AudioStrings.DelayHelp);
            delay.Add(Delay);
            card.Add(delay);

            RegisterCallback<GeometryChangedEvent>(evt => EnableInClassList(NarrowClass, IsNarrow(evt.newRect.width)));
            PanelLifetime.WhileAttached(this, Attach);
        }

        /// <summary>What the user hears.</summary>
        public AudioMonitorRow Monitor { get; }

        /// <summary>How much later recording and streaming get the sound, in ms.</summary>
        public ValueSlider Delay { get; }

        /// <summary>The groups in the order of the sources: the microphone, then the game audio.</summary>
        public IReadOnlyList<AudioSourceRow> Groups => _groups;

        /// <summary>Whether a Card this wide wraps its rows. Pure.</summary>
        public static bool IsNarrow(float width)
        {
            return width < NarrowWidth;
        }

        /// <summary>A level in dBFS on the meter, from -60 dB (empty) to 0 dB (full). Pure.</summary>
        public static float LevelOf(float peakDb)
        {
            return float.IsNaN(peakDb) || float.IsNegativeInfinity(peakDb) ? 0f : Math.Max(0f, Math.Min(1f, (peakDb + 60f) / 60f));
        }

        /// <summary>The chip and the problem's message for a source: a shape and text, never color alone. Pure.</summary>
        public static (StatusKind Kind, string StateKey, string ProblemKey) Describe(AudioSourceState state, AudioProblem problem, bool muted)
        {
            return state switch
            {
                AudioSourceState.Running when muted => (StatusKind.Warning, AudioStrings.Muted, null),
                AudioSourceState.Running => (StatusKind.Success, AudioStrings.Running, null),
                AudioSourceState.Starting => (StatusKind.Idle, AudioStrings.Starting, null),
                AudioSourceState.Unavailable => (StatusKind.Warning, AudioStrings.NotFound, AudioStrings.ProblemNotFound),
                AudioSourceState.Error => (StatusKind.Error, AudioStrings.Failed, problem switch
                {
                    AudioProblem.InUse => AudioStrings.ProblemInUse,
                    AudioProblem.AccessDenied => AudioStrings.ProblemAccessDenied,
                    _ => AudioStrings.ProblemFailed,
                }),
                _ => (StatusKind.Idle, AudioStrings.Off, null),
            };
        }

        /// <summary>
        /// The device choices: "None" first, then the devices, and the chosen one marked as not connected
        /// when it is missing, so the choice is not lost while it is unplugged. Pure.
        /// </summary>
        /// <summary>
        /// The name of a chosen device that is not in the list: a program's sound ("Sound of game.exe", audio detailed
        /// design 8.7), or a device not connected now.
        /// </summary>
        public static string UnlistedName(ILocalizer localizer, string deviceId)
        {
            string program = AudioApplication.ProgramOf(deviceId);
            if (program != null)
            {
                return localizer?.Get(AudioStrings.ApplicationSound, ("name", program)) ?? program;
            }

            return localizer?.Get(AudioStrings.MissingDevice) ?? AudioStrings.MissingDevice;
        }

        public static IReadOnlyList<SelectChoice> Choices(IReadOnlyList<AudioDevice> devices, string chosen, string missingText)
        {
            var choices = new List<SelectChoice> { SelectChoice.FromKey(string.Empty, AudioStrings.NoDevice) };
            choices.AddRange(devices.Select(device => SelectChoice.FromText(device.Id, device.Name)));
            if (!string.IsNullOrEmpty(chosen) && devices.All(device => device.Id != chosen))
            {
                choices.Add(SelectChoice.FromText(chosen, missingText));
            }

            return choices;
        }

        private IDisposable Attach(UiServices services)
        {
            _audio.RefreshDevices();
            IVisualElementScheduledItem refresh = schedule.Execute(_audio.RefreshDevices).Every(DeviceIntervalMs);
            var subscriptions = new List<IDisposable>
            {
                new DisposeAction(refresh.Pause),
                _audio.InputDevices.Subscribe(_ => ShowDevices(services)),
                _audio.OutputDevices.Subscribe(_ => ShowDevices(services)),
                _audio.StreamDelay.Subscribe(delay => Delay.SetValueWithoutNotify((float)delay.TotalMilliseconds)),
            };
            if (services?.Localizer != null)
            {
                Action<LocaleInfo> relabel = _ => ShowDevices(services);
                services.Localizer.LocaleChanged += relabel;
                subscriptions.Add(new DisposeAction(() => services.Localizer.LocaleChanged -= relabel));
            }

            foreach (AudioSourceRow group in _groups)
            {
                subscriptions.Add(group.Attach());
            }

            subscriptions.Add(Monitor.Attach());
            ShowDevices(services);
            Delay.SetValueWithoutNotify((float)_audio.StreamDelay.Value.TotalMilliseconds);
            return new DisposeAll(subscriptions);
        }

        private void ShowDevices(UiServices services)
        {
            foreach (AudioSourceRow group in _groups)
            {
                group.ShowDevices(_audio.InputDevices.Value, UnlistedName(services?.Localizer, group.DeviceId));
            }

            Monitor.ShowDevices(_audio.OutputDevices.Value, UnlistedName(services?.Localizer, Monitor.DeviceId));
        }
    }
}
