using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using VirtualVessel.Capture;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of the capture Cards (docs/&lt;lang&gt;/ui/screens/setup.md).</summary>
    [StringKeys]
    public static class CaptureStrings
    {
        public const string GameTitle = "setup.capture.game.title";
        public const string SubScreenTitle = "setup.capture.subscreen.title";
        public const string Unavailable = "setup.capture.unavailable";
        public const string Capture = "setup.capture.capture";
        public const string Device = "setup.capture.device";
        public const string ChooseCamera = "setup.capture.device.choose";
        public const string AutomaticMonitor = "setup.capture.device.automatic";
        public const string NoDevice = "setup.capture.device.none";
        public const string Refresh = "setup.capture.refresh";
        public const string RefreshButton = "setup.capture.refresh.button";
        public const string FlipHorizontal = "setup.capture.flip.horizontal";
        public const string FlipVertical = "setup.capture.flip.vertical";
        public const string Pointer = "setup.capture.pointer";
        public const string Stopped = "setup.capture.state.stopped";
        public const string Starting = "setup.capture.state.starting";
        public const string Ready = "setup.capture.state.ready";
        public const string Capturing = "setup.capture.state.capturing";
        public const string NotFound = "setup.capture.state.notfound";
        public const string Failed = "setup.capture.state.failed";
        public const string ProblemChoose = "setup.capture.problem.choose";
        public const string ProblemSignal = "setup.capture.problem.signal";
        public const string ProblemNotFound = "setup.capture.problem.notfound";
        public const string ProblemFailed = "setup.capture.problem.failed";
        public const string NoVideo = "setup.capture.preview.none";
    }

    /// <summary>
    /// One capture source on the setup screen: its state, starting and stopping, the device, the
    /// flips, and what it delivers (docs/&lt;lang&gt;/ui/screens/setup.md 3). Shows the source directly;
    /// changes go to it, and Capture saves them.
    /// </summary>
    public sealed class CaptureSourceCard : VisualElement
    {
        public const string UssClass = "vv-capture-card";
        public const string ProblemClass = UssClass + "__problem";
        public const string InfoClass = UssClass + "__info";
        public const string PreviewClass = UssClass + "__preview";
        public const string PreviewEmptyClass = PreviewClass + "-empty";

        private readonly ICaptureDeviceSource _source;
        private readonly string _emptyChoiceKey;
        private readonly KeyedText _problemText;
        private readonly Label _problem;
        private readonly Label _noVideo;

        /// <param name="emptyChoiceKey">The device choice with no device: "Choose a camera" or "Automatic".</param>
        public CaptureSourceCard(ICaptureDeviceSource source, string titleKey, string emptyChoiceKey)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _emptyChoiceKey = emptyChoiceKey;
            AddToClassList(UssClass);
            Card card = Ui.Card(titleKey);
            Add(card);

            Status = Ui.Chip(StatusKind.Idle, CaptureStrings.Stopped);
            card.Add(Status);
            _problem = new Label();
            _problem.AddToClassList(ProblemClass);
            _problem.AddToClassList("vv-text-body");
            _problemText = new KeyedText(_problem, text =>
            {
                _problem.text = text;
                _problem.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            });
            card.Add(_problem);

            // Small, only to check that the chosen device is the right one; the source's texture is
            // shown as it is, already flipped, so nothing is copied.
            Preview = new Image { scaleMode = UnityEngine.ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            Preview.AddToClassList(PreviewClass);
            _noVideo = Ui.Text(CaptureStrings.NoVideo, PreviewEmptyClass, "vv-text-caption");
            Preview.Add(_noVideo);
            card.Add(Preview);

            Capture = new SwitchToggle();
            Capture.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    _source.Start();
                }
                else
                {
                    _source.Stop();
                }
            });
            card.Add(Row(CaptureStrings.Capture, Capture));

            Devices = Ui.Select(null);
            Devices.RegisterValueChangedCallback(evt => _source.SelectDevice(string.IsNullOrEmpty(evt.newValue) ? null : evt.newValue));
            card.Add(Row(CaptureStrings.Device, Devices));

            ActionButton refresh = Ui.Button(CaptureStrings.RefreshButton);
            refresh.clicked += RefreshDevices;
            card.Add(Row(CaptureStrings.Refresh, refresh));

            FlipHorizontal = FlipToggle(card, CaptureStrings.FlipHorizontal, CaptureFlip.Horizontal);
            FlipVertical = FlipToggle(card, CaptureStrings.FlipVertical, CaptureFlip.Vertical);
            if (source is IPointerOption pointer)
            {
                Pointer = new SwitchToggle();
                Pointer.RegisterValueChangedCallback(evt => pointer.SetDrawPointer(evt.newValue));
                card.Add(Row(CaptureStrings.Pointer, Pointer));
            }

            Info = new Label();
            Info.AddToClassList(InfoClass);
            Info.AddToClassList("vv-text-mono");
            card.Add(Info);

            PanelLifetime.WhileAttached(this, _ => Attach());
        }

        public StatusChip Status { get; }

        /// <summary>The problem's explanation as shown, for tests.</summary>
        public string ProblemKey => _problemText.Key;

        public SwitchToggle Capture { get; }

        /// <summary>The source's video, small, to check the device.</summary>
        public Image Preview { get; }

        public SelectField Devices { get; }

        public SwitchToggle FlipHorizontal { get; }

        public SwitchToggle FlipVertical { get; }

        /// <summary>Null for sources that cannot draw the cursor.</summary>
        public SwitchToggle Pointer { get; }

        public Label Info { get; }

        /// <summary>The chip and the explanation for a state: shape, color, and text together (design-system.md 3.4). Pure.</summary>
        public static (StatusKind Kind, string StateKey, string ProblemKey) Describe(CaptureState state, CaptureProblem problem)
        {
            return state switch
            {
                CaptureState.Stopped when problem == CaptureProblem.DeviceNotChosen => (StatusKind.Warning, CaptureStrings.Stopped, CaptureStrings.ProblemChoose),
                CaptureState.Stopped => (StatusKind.Idle, CaptureStrings.Stopped, null),
                CaptureState.Starting => (StatusKind.Idle, CaptureStrings.Starting, null),
                CaptureState.Ready when problem == CaptureProblem.NoSignal => (StatusKind.Warning, CaptureStrings.Ready, CaptureStrings.ProblemSignal),
                CaptureState.Ready => (StatusKind.Idle, CaptureStrings.Ready, null),
                CaptureState.Capturing => (StatusKind.Success, CaptureStrings.Capturing, null),
                CaptureState.Unavailable => (StatusKind.Warning, CaptureStrings.NotFound, CaptureStrings.ProblemNotFound),

                // A missing plugin is a developer problem; users see a failure (capture detailed design 6.2).
                _ => (StatusKind.Error, CaptureStrings.Failed, CaptureStrings.ProblemFailed),
            };
        }

        /// <summary>"1920×1080 · 59.9 fps", or "—" while nothing is delivered. Numbers only, so the same in every language. Pure.</summary>
        public static string InfoText(CaptureSourceInfo info)
        {
            return info.Width > 0 && info.Height > 0 ? $"{info.Width}×{info.Height} · {info.FrameRate:0.0} fps" : "—";
        }

        private IDisposable Attach()
        {
            RefreshDevices();
            Show();
            var subscriptions = new List<IDisposable>
            {
                _source.State.Subscribe(_ => Show()),
                _source.Problem.Subscribe(_ => Show()),
                _source.Flip.Subscribe(_ => Show()),
                _source.Info.Subscribe(info => Info.text = InfoText(info)),
                _source.Texture.Subscribe(ShowVideo),
            };
            ShowVideo(_source.Texture.Value);
            if (_source is IPointerOption pointer)
            {
                subscriptions.Add(pointer.DrawPointer.Subscribe(draw => Pointer.SetValueWithoutNotify(draw)));
            }

            return new Subscriptions(subscriptions);
        }

        private void RefreshDevices()
        {
            _source.RefreshDevices();
            IReadOnlyList<CaptureDevice> devices = _source.Devices;
            Devices.SetChoices(devices.Count > 0
                ? new[] { SelectChoice.FromKey(string.Empty, _emptyChoiceKey) }.Concat(devices.Select(device => SelectChoice.FromText(device.Id, device.DisplayName)))
                : new[] { SelectChoice.FromKey(string.Empty, CaptureStrings.NoDevice) });
            Devices.SetValueWithoutNotify(_source.SelectedDeviceId ?? string.Empty);
        }

        private void Show()
        {
            (StatusKind kind, string stateKey, string problemKey) = Describe(_source.State.Value, _source.Problem.Value);
            Status.Status = kind;
            Status.TextKey = stateKey;
            _problemText.Key = problemKey;
            Capture.SetValueWithoutNotify(_source.State.Value != CaptureState.Stopped);
            FlipHorizontal.SetValueWithoutNotify((_source.Flip.Value & CaptureFlip.Horizontal) != 0);
            FlipVertical.SetValueWithoutNotify((_source.Flip.Value & CaptureFlip.Vertical) != 0);

            // A device chosen by default becomes the chosen one when it opens.
            Devices.SetValueWithoutNotify(_source.SelectedDeviceId ?? string.Empty);
            if (Pointer != null && _source is IPointerOption pointer)
            {
                Pointer.SetValueWithoutNotify(pointer.DrawPointer.Value);
            }

            Info.text = InfoText(_source.Info.Value);
        }

        private void ShowVideo(UnityEngine.Texture texture)
        {
            Preview.image = texture;
            _noVideo.style.display = texture == null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private SwitchToggle FlipToggle(VisualElement card, string nameKey, CaptureFlip part)
        {
            var toggle = new SwitchToggle();
            toggle.RegisterValueChangedCallback(evt => _source.SetFlip(evt.newValue ? _source.Flip.Value | part : _source.Flip.Value & ~part));
            card.Add(Row(nameKey, toggle));
            return toggle;
        }

        private static SettingRow Row(string nameKey, VisualElement control)
        {
            SettingRow row = Ui.SettingRow(nameKey);
            row.Add(control);
            return row;
        }

        private sealed class Subscriptions : IDisposable
        {
            private readonly List<IDisposable> _items;

            public Subscriptions(List<IDisposable> items)
            {
                _items = items;
            }

            public void Dispose()
            {
                foreach (IDisposable item in _items)
                {
                    item.Dispose();
                }

                _items.Clear();
            }
        }
    }
}
