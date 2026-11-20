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
        public const string Audio = "setup.capture.audio";
        public const string AudioDescription = "setup.capture.audio.description";
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
        public const string ProblemInUse = "setup.capture.problem.inuse";
        public const string NoVideo = "setup.capture.preview.none";
        public const string Input = "setup.capture.input";
        public const string InputDescription = "setup.capture.input.description";
        public const string InputBoard = "setup.capture.input.board";
        public const string InputWindow = "setup.capture.input.window";
        public const string ChooseWindow = "setup.capture.device.window.choose";
        public const string NoWindow = "setup.capture.device.window.none";
        public const string ProblemChooseWindow = "setup.capture.problem.window.choose";
        public const string ProblemWindowNotFound = "setup.capture.problem.window.notfound";
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

        /// <summary>The choices of <see cref="Input"/>.</summary>
        public const string BoardInput = "board";
        public const string WindowInput = "window";

        private readonly ICaptureDeviceSource _source;
        private readonly GameAudioRow _audio;
        private readonly string _emptyChoiceKey;

        // Which devices are offered while none is chosen: capture boards, or windows on this PC (setup.md 3).
        private bool _windows;

        // The device the input row let go of. The next choice follows on from it, so the sound that followed the
        // board moves to the window's (a board's sound stayed when a window was chosen, user's report 2026-11-20).
        private string _letGo;
        private readonly KeyedText _problemText;
        private readonly Label _problem;
        private readonly Label _noVideo;

        /// <param name="emptyChoiceKey">The device choice with no device: "Choose a camera" or "Automatic".</param>
        /// <param name="audio">The sound that comes with this video, or null when none does.</param>
        /// <param name="offersWindows">
        /// Whether the source also captures windows on this PC (the game screen, capture detailed design 8.6): an
        /// input row then chooses between capture boards and windows.
        /// </param>
        public CaptureSourceCard(ICaptureDeviceSource source, string titleKey, string emptyChoiceKey, GameAudioRow audio = null, bool offersWindows = false)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _emptyChoiceKey = emptyChoiceKey;
            _audio = audio;
            AddToClassList(UssClass);
            Card card = Ui.Card(titleKey);
            Add(card);

            Status = Ui.Chip(StatusKind.Idle, CaptureStrings.Stopped);
            card.HeaderEnd.Add(Status);
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
            card.Add(Ui.ControlRow(CaptureStrings.Capture, Capture));

            if (offersWindows)
            {
                // A Select, not a Segmented: the two names did not fit the row's control (user's report, 2026-11-20).
                Input = Ui.Select(null);
                Input.SetChoices(new[] { SelectChoice.FromKey(BoardInput, CaptureStrings.InputBoard), SelectChoice.FromKey(WindowInput, CaptureStrings.InputWindow) });
                Input.RegisterValueChangedCallback(evt => ChooseInput(evt.newValue == WindowInput));
                card.Add(Ui.ControlRow(CaptureStrings.Input, Input, CaptureStrings.InputDescription));
            }

            Devices = Ui.Select(null);
            Devices.RegisterValueChangedCallback(evt => Choose(evt.previousValue, evt.newValue));
            card.Add(Ui.ControlRow(CaptureStrings.Device, Devices));
            if (audio != null)
            {
                card.Add(audio);
            }

            ActionButton refresh = Ui.Button(CaptureStrings.RefreshButton);
            refresh.clicked += RefreshDevices;
            card.Add(Ui.ControlRow(CaptureStrings.Refresh, refresh));

            FlipHorizontal = FlipToggle(card, CaptureStrings.FlipHorizontal, CaptureFlip.Horizontal);
            FlipVertical = FlipToggle(card, CaptureStrings.FlipVertical, CaptureFlip.Vertical);
            if (source is IPointerOption pointer)
            {
                Pointer = new SwitchToggle();
                Pointer.RegisterValueChangedCallback(evt => pointer.SetDrawPointer(evt.newValue));
                card.Add(Ui.ControlRow(CaptureStrings.Pointer, Pointer));
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

        /// <summary>Capture board or window on this PC; null for sources without windows.</summary>
        public SelectField Input { get; }

        public SwitchToggle FlipHorizontal { get; }

        public SwitchToggle FlipVertical { get; }

        /// <summary>Null for sources that cannot draw the cursor.</summary>
        public SwitchToggle Pointer { get; }

        public Label Info { get; }

        /// <summary>
        /// The chip and the explanation for a state: shape, color, and text together (design-system.md 3.4). For a
        /// window on this PC, what to do is about the game rather than a device. Pure.
        /// </summary>
        public static (StatusKind Kind, string StateKey, string ProblemKey) Describe(CaptureState state, CaptureProblem problem, bool window = false)
        {
            return state switch
            {
                CaptureState.Stopped when problem == CaptureProblem.DeviceNotChosen => (StatusKind.Warning, CaptureStrings.Stopped, window ? CaptureStrings.ProblemChooseWindow : CaptureStrings.ProblemChoose),
                CaptureState.Stopped => (StatusKind.Idle, CaptureStrings.Stopped, null),
                CaptureState.Starting => (StatusKind.Idle, CaptureStrings.Starting, null),
                CaptureState.Ready when problem == CaptureProblem.NoSignal => (StatusKind.Warning, CaptureStrings.Ready, CaptureStrings.ProblemSignal),
                CaptureState.Ready => (StatusKind.Idle, CaptureStrings.Ready, null),
                CaptureState.Capturing => (StatusKind.Success, CaptureStrings.Capturing, null),
                CaptureState.Unavailable => (StatusKind.Warning, CaptureStrings.NotFound, window ? CaptureStrings.ProblemWindowNotFound : CaptureStrings.ProblemNotFound),
                CaptureState.Error when problem == CaptureProblem.DeviceInUse => (StatusKind.Error, CaptureStrings.Failed, CaptureStrings.ProblemInUse),

                // A missing plugin is a developer problem; users see a failure (capture detailed design 6.2).
                _ => (StatusKind.Error, CaptureStrings.Failed, CaptureStrings.ProblemFailed),
            };
        }

        /// <summary>The devices of one input: the capture boards, or the windows on this PC. Pure.</summary>
        public static IReadOnlyList<CaptureDevice> DevicesOf(IReadOnlyList<CaptureDevice> devices, bool windows)
        {
            return devices.Where(device => CaptureWindow.IsWindow(device.Id) == windows).ToList();
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

        // A capture board's ID is its name, so one that is not plugged in now is still named; a window is not.
        private string NameOf(string deviceId)
        {
            string listed = _source.Devices.FirstOrDefault(device => device.Id == deviceId)?.DisplayName;
            return listed ?? (CaptureWindow.IsWindow(deviceId) ? null : deviceId);
        }

        private void RefreshDevices()
        {
            _source.RefreshDevices();
            ShowDevices();
        }

        private void ShowDevices()
        {
            IReadOnlyList<CaptureDevice> devices = Input != null ? DevicesOf(_source.Devices, _windows) : _source.Devices;
            string emptyKey = Input != null && _windows ? CaptureStrings.ChooseWindow : _emptyChoiceKey;
            string noneKey = Input != null && _windows ? CaptureStrings.NoWindow : CaptureStrings.NoDevice;
            Devices.SetChoices(devices.Count > 0
                ? new[] { SelectChoice.FromKey(string.Empty, emptyKey) }.Concat(devices.Select(device => SelectChoice.FromText(device.Id, device.DisplayName)))
                : new[] { SelectChoice.FromKey(string.Empty, noneKey) });
            Devices.SetValueWithoutNotify(_source.SelectedDeviceId ?? string.Empty);
        }

        private void Choose(string previousId, string nextId)
        {
            string previous = string.IsNullOrEmpty(previousId) ? _letGo : previousId;
            _letGo = null;
            _audio?.VideoChanged(previous, NameOf(previous), nextId, NameOf(nextId));
            _source.SelectDevice(string.IsNullOrEmpty(nextId) ? null : nextId);
        }

        // Another input: a device of the other kind is let go, so the user chooses one of this kind.
        private void ChooseInput(bool windows)
        {
            _windows = windows;
            string chosen = _source.SelectedDeviceId;
            if (chosen != null && CaptureWindow.IsWindow(chosen) != windows)
            {
                Choose(chosen, null);
                _letGo = chosen;
            }

            ShowDevices();
            Show();
        }

        private void Show()
        {
            // The chosen device tells the input; with none chosen, the input the user last picked stays.
            if (Input != null && _source.SelectedDeviceId != null)
            {
                _windows = CaptureWindow.IsWindow(_source.SelectedDeviceId);
            }

            Input?.SetValueWithoutNotify(_windows ? WindowInput : BoardInput);
            (StatusKind kind, string stateKey, string problemKey) = Describe(_source.State.Value, _source.Problem.Value, Input != null && _windows);
            Status.Status = kind;
            Status.TextKey = stateKey;
            _problemText.Key = problemKey;
            Capture.SetValueWithoutNotify(_source.State.Value != CaptureState.Stopped);
            FlipHorizontal.SetValueWithoutNotify((_source.Flip.Value & CaptureFlip.Horizontal) != 0);
            FlipVertical.SetValueWithoutNotify((_source.Flip.Value & CaptureFlip.Vertical) != 0);

            // A device chosen by default becomes the chosen one when it opens; its input may have changed with it.
            if (Input != null)
            {
                ShowDevices();
            }

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
            card.Add(Ui.ControlRow(nameKey, toggle));
            return toggle;
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
