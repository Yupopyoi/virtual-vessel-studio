using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using VirtualVessel.Audio;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
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
        public const string Device = "setup.audio.device";
        public const string MicrophoneDescription = "setup.audio.device.microphone";
        public const string GameAudioDescription = "setup.audio.device.game";
        public const string NoDevice = "setup.audio.device.none";
        public const string MissingDevice = "setup.audio.device.missing";
        public const string Gain = "setup.audio.gain";
        public const string Mute = "setup.audio.mute";
        public const string Level = "setup.audio.level";
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
        public const string MonitoredDescription = "setup.audio.monitored.description";
        public const string Monitor = "setup.audio.monitor";
        public const string MonitorDevice = "setup.audio.monitor.device";
        public const string MonitorDeviceDescription = "setup.audio.monitor.device.description";
        public const string MonitorGain = "setup.audio.monitor.gain";
        public const string MonitorMute = "setup.audio.monitor.mute";
        public const string Delay = "setup.audio.delay";
        public const string DelayDescription = "setup.audio.delay.description";
        public const string LiveMicrophone = "live.audio.microphone";
        public const string LiveGameAudio = "live.audio.game";
        public const string LiveMute = "live.audio.mute";
        public const string LiveUnmute = "live.audio.unmute";
    }

    /// <summary>
    /// The audio sources on the setup screen (audio detailed design 18; setup.md 11): for the microphone and
    /// the game audio, the device, the gain, mute, and the level, with the source's state in the group's
    /// heading and what to do when it has a problem.
    /// </summary>
    public sealed class AudioCard : VisualElement
    {
        public const string UssClass = "vv-audio-card";
        public const string GroupClass = UssClass + "__group";
        public const string HeadingClass = UssClass + "__heading";
        public const string ProblemClass = UssClass + "__problem";
        public const string PrivacyClass = UssClass + "__privacy";

        /// <summary>How often devices are listed again while the Card is shown, so a plugged-in one appears.</summary>
        public const long DeviceIntervalMs = 5000;

        private readonly IAudioService _audio;
        private readonly List<SourceGroup> _groups = new List<SourceGroup>();

        public AudioCard(IAudioService audio, ISystemSettings systemSettings)
        {
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            AddToClassList(UssClass);
            Card card = Ui.Card(AudioStrings.Title);
            Add(card);
            foreach (IAudioSource source in audio.Sources)
            {
                var group = new SourceGroup(source, systemSettings);
                _groups.Add(group);
                card.Add(group.Root);
            }

            Monitor = new MonitorGroup(audio.Monitor);
            card.Add(Monitor.Root);

            // Recording and streaming get the sound later, to meet the avatar's motion (audio detailed design 8.5).
            Delay = Ui.Slider(null, 0, AudioSettings.MaxStreamDelayMs, "ms");
            Delay.Step = 10f;
            Delay.RegisterValueChangedCallback(evt => _audio.SetStreamDelay(TimeSpan.FromMilliseconds(evt.newValue)));
            SettingRow delay = Ui.SettingRow(AudioStrings.Delay, AudioStrings.DelayDescription);
            delay.Add(Delay);
            card.Add(delay);

            PanelLifetime.WhileAttached(this, Attach);
        }

        /// <summary>What the user hears.</summary>
        public MonitorGroup Monitor { get; }

        /// <summary>How much later recording and streaming get the sound, in ms.</summary>
        public ValueSlider Delay { get; }

        /// <summary>The groups in the order of the sources: the microphone, then the game audio.</summary>
        public IReadOnlyList<SourceGroup> Groups => _groups;

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

            foreach (SourceGroup group in _groups)
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
            string missing = services?.Localizer?.Get(AudioStrings.MissingDevice) ?? AudioStrings.MissingDevice;
            foreach (SourceGroup group in _groups)
            {
                group.ShowDevices(_audio.InputDevices.Value, missing);
            }

            Monitor.ShowDevices(_audio.OutputDevices.Value, missing);
        }

        /// <summary>What the user hears: where it plays, how loud, and its level.</summary>
        public sealed class MonitorGroup
        {
            private readonly IAudioMonitor _monitor;
            private readonly KeyedText _problemText;

            public MonitorGroup(IAudioMonitor monitor)
            {
                _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
                Root = new VisualElement();
                Root.AddToClassList(GroupClass);

                var heading = new VisualElement();
                heading.AddToClassList(HeadingClass);
                heading.Add(Ui.Text(AudioStrings.Monitor, "vv-text-body"));
                Status = Ui.Chip(StatusKind.Idle, AudioStrings.Off);
                heading.Add(Status);
                Root.Add(heading);

                Problem = new Label();
                Problem.AddToClassList(ProblemClass);
                Problem.AddToClassList("vv-text-body");
                _problemText = new KeyedText(Problem, text =>
                {
                    Problem.text = text;
                    Problem.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
                });
                Root.Add(Problem);

                Devices = Ui.Select(null);
                Devices.RegisterValueChangedCallback(evt => _monitor.SetOptions(_monitor.Options.Value.WithDevice(evt.newValue)));
                SettingRow device = Ui.SettingRow(AudioStrings.MonitorDevice, AudioStrings.MonitorDeviceDescription);
                device.Add(Devices);
                Root.Add(device);

                Gain = Ui.Slider(null, AudioSourceOptions.MinGainDb, AudioSourceOptions.MaxGainDb, "dB");
                Gain.Step = 1f;
                Gain.Signed = true;
                Gain.RegisterValueChangedCallback(evt => _monitor.SetOptions(_monitor.Options.Value.WithGain(evt.newValue)));
                SettingRow gain = Ui.SettingRow(AudioStrings.MonitorGain);
                gain.Add(Gain);
                Root.Add(gain);

                Mute = new SwitchToggle();
                Mute.RegisterValueChangedCallback(evt => _monitor.SetOptions(_monitor.Options.Value.WithMuted(evt.newValue)));
                SettingRow mute = Ui.SettingRow(AudioStrings.MonitorMute);
                mute.Add(Mute);
                Root.Add(mute);

                Meter = new LevelMeter();
                SettingRow level = Ui.SettingRow(AudioStrings.Level);
                level.Add(Meter);
                Root.Add(level);
                Show();
            }

            public VisualElement Root { get; }

            public StatusChip Status { get; }

            public Label Problem { get; }

            public SelectField Devices { get; }

            public ValueSlider Gain { get; }

            public SwitchToggle Mute { get; }

            public LevelMeter Meter { get; }

            internal IDisposable Attach()
            {
                IVisualElementScheduledItem level = Root.schedule.Execute(() => Meter.Level = LevelOf(_monitor.PeakDb)).Every(LevelMeter.DefaultIntervalMs * 3);
                return new DisposeAll(new List<IDisposable>
                {
                    new DisposeAction(level.Pause),
                    _monitor.State.Subscribe(_ => Show()),
                    _monitor.Problem.Subscribe(_ => Show()),
                    _monitor.Options.Subscribe(_ => Show()),
                });
            }

            internal void ShowDevices(IReadOnlyList<AudioDevice> devices, string missingText)
            {
                Devices.SetChoices(Choices(devices, _monitor.Options.Value.DeviceId, missingText));
                Devices.SetValueWithoutNotify(_monitor.Options.Value.DeviceId ?? string.Empty);
            }

            private void Show()
            {
                AudioMonitorOptions options = _monitor.Options.Value;
                (StatusKind kind, string stateKey, string problemKey) = Describe(_monitor.State.Value, _monitor.Problem.Value, options.Muted);
                Status.Status = kind;
                Status.TextKey = stateKey;
                _problemText.Key = problemKey;
                Devices.SetValueWithoutNotify(options.DeviceId ?? string.Empty);
                Gain.SetValueWithoutNotify(options.GainDb);
                Mute.SetValueWithoutNotify(options.Muted);
            }
        }

        /// <summary>One source's rows.</summary>
        public sealed class SourceGroup
        {
            private readonly IAudioSource _source;
            private readonly KeyedText _problemText;

            public SourceGroup(IAudioSource source, ISystemSettings systemSettings)
            {
                _source = source;
                Root = new VisualElement();
                Root.AddToClassList(GroupClass);

                var heading = new VisualElement();
                heading.AddToClassList(HeadingClass);
                heading.Add(Ui.Text(source.Role == AudioSourceRole.Microphone ? AudioStrings.Microphone : AudioStrings.GameAudio, "vv-text-body"));
                Status = Ui.Chip(StatusKind.Idle, AudioStrings.Off);
                heading.Add(Status);
                Root.Add(heading);

                Problem = new Label();
                Problem.AddToClassList(ProblemClass);
                Problem.AddToClassList("vv-text-body");
                _problemText = new KeyedText(Problem, text =>
                {
                    Problem.text = text;
                    Problem.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
                });
                Root.Add(Problem);

                // Only when Windows denies access: the one problem the user fixes in Windows' own settings.
                PrivacyButton = Ui.Button(AudioStrings.OpenPrivacy).Bind(new UiCommand(() => systemSettings?.OpenMicrophonePrivacy()));
                PrivacyButton.AddToClassList(PrivacyClass);
                Root.Add(PrivacyButton);

                Devices = Ui.Select(null);
                Devices.RegisterValueChangedCallback(evt => _source.SetOptions(_source.Options.Value.WithDevice(evt.newValue)));
                SettingRow device = Ui.SettingRow(AudioStrings.Device, source.Role == AudioSourceRole.Microphone ? AudioStrings.MicrophoneDescription : AudioStrings.GameAudioDescription);
                device.Add(Devices);
                Root.Add(device);

                Gain = Ui.Slider(null, AudioSourceOptions.MinGainDb, AudioSourceOptions.MaxGainDb, "dB");
                Gain.Step = 1f;
                Gain.Signed = true;
                Gain.RegisterValueChangedCallback(evt => _source.SetOptions(_source.Options.Value.WithGain(evt.newValue)));
                SettingRow gain = Ui.SettingRow(AudioStrings.Gain);
                gain.Add(Gain);
                Root.Add(gain);

                Mute = new SwitchToggle();
                Mute.RegisterValueChangedCallback(evt => _source.SetOptions(_source.Options.Value.WithMuted(evt.newValue)));
                SettingRow mute = Ui.SettingRow(AudioStrings.Mute);
                mute.Add(Mute);
                Root.Add(mute);

                Monitored = new SwitchToggle();
                Monitored.RegisterValueChangedCallback(evt => _source.SetOptions(_source.Options.Value.WithMonitored(evt.newValue)));
                SettingRow monitored = Ui.SettingRow(AudioStrings.Monitored, AudioStrings.MonitoredDescription);
                monitored.Add(Monitored);
                Root.Add(monitored);

                Meter = new LevelMeter();
                SettingRow level = Ui.SettingRow(AudioStrings.Level);
                level.Add(Meter);
                Root.Add(level);
                Show();
            }

            public VisualElement Root { get; }

            public StatusChip Status { get; }

            public Label Problem { get; }

            /// <summary>The problem's explanation as shown, for tests.</summary>
            public string ProblemKey => _problemText.Key;

            public ActionButton PrivacyButton { get; }

            public SelectField Devices { get; }

            public ValueSlider Gain { get; }

            public SwitchToggle Mute { get; }

            /// <summary>Whether the user hears this source.</summary>
            public SwitchToggle Monitored { get; }

            public LevelMeter Meter { get; }

            internal IDisposable Attach()
            {
                IVisualElementScheduledItem level = Root.schedule.Execute(() => Meter.Level = LevelOf(_source.PeakDb)).Every(LevelMeter.DefaultIntervalMs * 3);
                return new DisposeAll(new List<IDisposable>
                {
                    new DisposeAction(level.Pause),
                    _source.State.Subscribe(_ => Show()),
                    _source.Problem.Subscribe(_ => Show()),
                    _source.Options.Subscribe(_ => Show()),
                });
            }

            internal void ShowDevices(IReadOnlyList<AudioDevice> devices, string missingText)
            {
                Devices.SetChoices(Choices(devices, _source.Options.Value.DeviceId, missingText));
                Devices.SetValueWithoutNotify(_source.Options.Value.DeviceId ?? string.Empty);
            }

            private void Show()
            {
                AudioSourceOptions options = _source.Options.Value;
                (StatusKind kind, string stateKey, string problemKey) = Describe(_source.State.Value, _source.Problem.Value, options.Muted);
                Status.Status = kind;
                Status.TextKey = stateKey;
                _problemText.Key = problemKey;
                PrivacyButton.style.display = problemKey == AudioStrings.ProblemAccessDenied ? DisplayStyle.Flex : DisplayStyle.None;
                Devices.SetValueWithoutNotify(options.DeviceId ?? string.Empty);
                Gain.SetValueWithoutNotify(options.GainDb);
                Mute.SetValueWithoutNotify(options.Muted);
                Monitored.SetValueWithoutNotify(options.Monitored);
            }
        }
    }
}
