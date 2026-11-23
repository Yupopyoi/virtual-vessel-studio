using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using VirtualVessel.Audio;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Screens
{
    /// <summary>
    /// One row of the audio mixer (components.md 3.25): the name and state, the device, the volume, whether it
    /// is heard, noise removal, mute, and the level, in the columns of <see cref="Columns"/>, with the problem under
    /// them.
    /// </summary>
    public abstract class AudioMixerRow
    {
        private readonly KeyedText _problemText;

        /// <param name="hear">The control of the Hear column, or null for a blank there.</param>
        /// <param name="denoise">The control of the Noise removal column, or null for a blank there.</param>
        /// <param name="hasDevice">False for BGM and SE, which play from a player: the Device column is blank.</param>
        /// <param name="nameHelpKey">An explanation of the whole row, behind a help hint after its name; null for none.</param>
        protected AudioMixerRow(string nameKey, string deviceKey, SwitchToggle hear, SwitchToggle denoise = null, bool hasDevice = true, string nameHelpKey = null)
        {
            Root = new VisualElement();
            Root.AddToClassList(AudioCard.GroupClass);

            var heading = new VisualElement();
            heading.AddToClassList(AudioCard.HeadingClass);
            heading.Add(Ui.Text(nameKey, "vv-text-body"));
            if (nameHelpKey != null)
            {
                heading.Add(new HelpHint(nameHelpKey));
            }
            Status = Ui.Chip(StatusKind.Idle, AudioStrings.Off);
            heading.Add(Status);

            var row = new VisualElement();
            row.AddToClassList(AudioCard.RowClass);
            row.Add(NameCell(heading));
            Root.Add(row);

            Problem = new Label();
            Problem.AddToClassList(AudioCard.ProblemClass);
            Problem.AddToClassList("vv-text-body");
            _problemText = new KeyedText(Problem, text =>
            {
                Problem.text = text;
                Problem.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            });
            Root.Add(Problem);

            Devices = Ui.Select(null);
            Devices.RegisterValueChangedCallback(evt => SetDevice(evt.newValue));
            // A row explained as a whole explains its device too, so the cell has no hint of its own.
            row.Add(hasDevice ? Cell(deviceKey, Devices, AudioCard.DeviceColumn, nameHelpKey == null ? AudioStrings.DeviceHelp : null) : EmptyCell(AudioCard.DeviceColumn));

            Gain = Ui.Slider(null, AudioSourceOptions.MinGainDb, AudioSourceOptions.MaxGainDb, "dB");
            Gain.Step = 1f;
            Gain.Signed = true;
            Gain.RegisterValueChangedCallback(evt => SetGain(evt.newValue));
            row.Add(Cell(AudioStrings.Gain, Gain, AudioCard.GainColumn, AudioStrings.GainHelp));

            row.Add(hear != null ? Cell(AudioStrings.Monitored, hear, AudioCard.ToggleColumn, AudioStrings.MonitoredHelp) : EmptyCell(AudioCard.ToggleColumn));
            row.Add(denoise != null ? Cell(AudioStrings.Denoise, denoise, AudioCard.DenoiseColumn, AudioStrings.DenoiseHelp) : EmptyCell(AudioCard.DenoiseColumn));

            Mute = new SwitchToggle();
            Mute.RegisterValueChangedCallback(evt => SetMuted(evt.newValue));
            row.Add(Cell(AudioStrings.Mute, Mute, AudioCard.MuteColumn, AudioStrings.MuteHelp));

            Meter = new LevelMeter();
            row.Add(Cell(AudioStrings.Level, Meter, AudioCard.LevelColumn, AudioStrings.LevelHelp));
        }

        public VisualElement Root { get; }

        public StatusChip Status { get; }

        public Label Problem { get; }

        /// <summary>The problem's explanation as shown, for tests.</summary>
        public string ProblemKey => _problemText.Key;

        public SelectField Devices { get; }

        public ValueSlider Gain { get; }

        public SwitchToggle Mute { get; }

        public LevelMeter Meter { get; }

        /// <summary>The chosen device.</summary>
        protected internal abstract string DeviceId { get; }

        /// <summary>The level now, in dBFS.</summary>
        protected abstract float PeakDb { get; }

        /// <summary>The headings over the rows' columns.</summary>
        public static VisualElement Columns()
        {
            var columns = new VisualElement();
            columns.AddToClassList(AudioCard.ColumnsClass);
            columns.Add(Heading(AudioStrings.Source, AudioCard.NameColumn));
            columns.Add(Heading(AudioStrings.Device, AudioCard.DeviceColumn, AudioStrings.DeviceHelp));
            columns.Add(Heading(AudioStrings.Gain, AudioCard.GainColumn, AudioStrings.GainHelp));
            columns.Add(Heading(AudioStrings.Monitored, AudioCard.ToggleColumn, AudioStrings.MonitoredHelp));
            columns.Add(Heading(AudioStrings.Denoise, AudioCard.DenoiseColumn, AudioStrings.DenoiseHelp));
            columns.Add(Heading(AudioStrings.Mute, AudioCard.MuteColumn, AudioStrings.MuteHelp));
            columns.Add(Heading(AudioStrings.Level, AudioCard.LevelColumn, AudioStrings.LevelHelp));
            return columns;
        }

        internal IDisposable Attach()
        {
            IVisualElementScheduledItem level = Root.schedule.Execute(() => Meter.Level = AudioCard.LevelOf(PeakDb)).Every(LevelMeter.DefaultIntervalMs * 3);
            var subscriptions = new List<IDisposable> { new DisposeAction(level.Pause) };
            subscriptions.AddRange(FollowState());
            return new DisposeAll(subscriptions);
        }

        internal void ShowDevices(IReadOnlyList<AudioDevice> devices, string missingText)
        {
            Devices.SetChoices(AudioCard.Choices(devices, DeviceId, missingText));
            Devices.SetValueWithoutNotify(DeviceId ?? string.Empty);
        }

        /// <summary>Subscribes <see cref="Show"/> to what this row shows.</summary>
        protected abstract IEnumerable<IDisposable> FollowState();

        protected abstract void SetDevice(string deviceId);

        protected abstract void SetGain(float gainDb);

        protected abstract void SetMuted(bool muted);

        protected abstract void Show();

        /// <summary>Shows what every row has, and returns the explanation of the problem, or null.</summary>
        protected string ShowCommon(AudioSourceState state, AudioProblem problem, bool muted, float gainDb)
        {
            (StatusKind kind, string stateKey, string problemKey) = AudioCard.Describe(state, problem, muted);
            Status.Status = kind;
            Status.TextKey = stateKey;
            _problemText.Key = problemKey;
            Devices.SetValueWithoutNotify(DeviceId ?? string.Empty);
            Gain.SetValueWithoutNotify(gainDb);
            Mute.SetValueWithoutNotify(muted);
            return problemKey;
        }

        private static VisualElement Heading(string key, string column, string helpKey = null)
        {
            if (helpKey == null)
            {
                return Ui.Text(key, AudioCard.CellClass, column, "vv-text-caption");
            }

            VisualElement cell = NewCell(column);
            cell.AddToClassList(AudioCard.HelpCellClass);
            cell.Add(Ui.Text(key, "vv-text-caption"));
            cell.Add(new HelpHint(helpKey));
            return cell;
        }

        private static VisualElement NameCell(VisualElement name)
        {
            VisualElement cell = NewCell(AudioCard.NameColumn);
            cell.Add(name);
            return cell;
        }

        // The name in a cell shows only while the columns' headings are gone (AudioCard.NarrowClass), with the heading's
        // help hint, so the explanation stays reachable then.
        private static VisualElement Cell(string key, VisualElement control, string column, string helpKey = null)
        {
            VisualElement cell = NewCell(column);
            if (helpKey == null)
            {
                cell.Add(Ui.Text(key, AudioCard.CellLabelClass, "vv-text-caption"));
            }
            else
            {
                var label = new VisualElement();
                label.AddToClassList(AudioCard.CellLabelClass);
                label.AddToClassList(AudioCard.HelpCellClass);
                label.Add(Ui.Text(key, "vv-text-caption"));
                label.Add(new HelpHint(helpKey));
                cell.Add(label);
            }

            cell.Add(control);
            return cell;
        }

        // A blank, so a row's later columns stay under their headings.
        private static VisualElement EmptyCell(string column)
        {
            VisualElement cell = NewCell(column);
            cell.AddToClassList(AudioCard.EmptyCellClass);
            return cell;
        }

        private static VisualElement NewCell(string column)
        {
            var cell = new VisualElement();
            cell.AddToClassList(AudioCard.CellClass);
            cell.AddToClassList(column);
            return cell;
        }
    }

    /// <summary>A source's row: the microphone, the game audio, BGM, or SE.</summary>
    public sealed class AudioSourceRow : AudioMixerRow
    {
        private readonly IAudioSource _source;

        /// <param name="deviceElsewhere">The device is chosen on another Card, so this row only shows it.</param>
        public AudioSourceRow(IAudioSource source, ISystemSettings systemSettings, bool deviceElsewhere)
            : this(source, systemSettings, deviceElsewhere, new SwitchToggle(), source.Role == AudioSourceRole.Microphone ? new SwitchToggle() : null)
        {
        }

        // Noise removal is for a voice; the game's own sound is left as it is.
        private AudioSourceRow(IAudioSource source, ISystemSettings systemSettings, bool deviceElsewhere, SwitchToggle monitored, SwitchToggle denoise)
            : base(AudioStrings.NameOf(source.Role), AudioStrings.Device, monitored, denoise, hasDevice: !AudioSettings.IsPlayer(source.Role))
        {
            _source = source;
            Monitored = monitored;
            Monitored.RegisterValueChangedCallback(evt => _source.SetOptions(_source.Options.Value.WithMonitored(evt.newValue)));
            Denoise = denoise;
            Denoise?.RegisterValueChangedCallback(evt => _source.SetOptions(_source.Options.Value.WithDenoise(evt.newValue)));
            if (Denoise != null)
            {
                Denoise.TooltipKey = AudioStrings.DenoiseHelp;
            }

            // Only when Windows denies access: the one problem the user fixes in Windows' own settings.
            PrivacyButton = Ui.Button(AudioStrings.OpenPrivacy).Bind(new UiCommand(() => systemSettings?.OpenMicrophonePrivacy()));
            PrivacyButton.AddToClassList(AudioCard.PrivacyClass);
            Root.Add(PrivacyButton);

            // Disabled, not left out, so the row still says which device the sound comes from; the tooltip says where
            // it is chosen.
            Devices.SetEnabled(!deviceElsewhere);
            Devices.TooltipKey = deviceElsewhere ? AudioStrings.GameDeviceNote : null;
            Show();
        }

        public ActionButton PrivacyButton { get; }

        /// <summary>Whether the user hears this source.</summary>
        public SwitchToggle Monitored { get; }

        /// <summary>Noise removal; null for a source other than the microphone.</summary>
        public SwitchToggle Denoise { get; }

        protected internal override string DeviceId => _source.Options.Value.DeviceId;

        protected override float PeakDb => _source.PeakDb;

        protected override IEnumerable<IDisposable> FollowState()
        {
            yield return _source.State.Subscribe(_ => Show());
            yield return _source.Problem.Subscribe(_ => Show());
            yield return _source.Options.Subscribe(_ => Show());
        }

        protected override void SetDevice(string deviceId)
        {
            _source.SetOptions(_source.Options.Value.WithDevice(deviceId));
        }

        protected override void SetGain(float gainDb)
        {
            _source.SetOptions(_source.Options.Value.WithGain(gainDb));
        }

        protected override void SetMuted(bool muted)
        {
            _source.SetOptions(_source.Options.Value.WithMuted(muted));
        }

        protected override void Show()
        {
            AudioSourceOptions options = _source.Options.Value;
            string problemKey = ShowCommon(_source.State.Value, _source.Problem.Value, options.Muted, options.GainDb);
            PrivacyButton.style.display = problemKey == AudioStrings.ProblemAccessDenied ? DisplayStyle.Flex : DisplayStyle.None;
            Monitored.SetValueWithoutNotify(options.Monitored);
            Denoise?.SetValueWithoutNotify(options.Denoise);
        }
    }

    /// <summary>What the user hears: where it plays, how loud, and its level.</summary>
    public sealed class AudioMonitorRow : AudioMixerRow
    {
        private readonly IAudioMonitor _monitor;

        public AudioMonitorRow(IAudioMonitor monitor)
            : base(AudioStrings.Monitor, AudioStrings.MonitorDevice, hear: null, nameHelpKey: AudioStrings.MonitorHelp)
        {
            _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            Show();
        }

        protected internal override string DeviceId => _monitor.Options.Value.DeviceId;

        protected override float PeakDb => _monitor.PeakDb;

        protected override IEnumerable<IDisposable> FollowState()
        {
            yield return _monitor.State.Subscribe(_ => Show());
            yield return _monitor.Problem.Subscribe(_ => Show());
            yield return _monitor.Options.Subscribe(_ => Show());
        }

        protected override void SetDevice(string deviceId)
        {
            _monitor.SetOptions(_monitor.Options.Value.WithDevice(deviceId));
        }

        protected override void SetGain(float gainDb)
        {
            _monitor.SetOptions(_monitor.Options.Value.WithGain(gainDb));
        }

        protected override void SetMuted(bool muted)
        {
            _monitor.SetOptions(_monitor.Options.Value.WithMuted(muted));
        }

        protected override void Show()
        {
            AudioMonitorOptions options = _monitor.Options.Value;
            ShowCommon(_monitor.State.Value, _monitor.Problem.Value, options.Muted, options.GainDb);
        }
    }
}
