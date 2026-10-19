using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using VirtualVessel.Capture;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.Tracking;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of the tracking Card (docs/&lt;lang&gt;/ui/screens/setup.md).</summary>
    [StringKeys]
    public static class TrackingStrings
    {
        public const string Title = "setup.tracking.title";
        public const string Unavailable = "setup.tracking.unavailable";
        public const string Track = "setup.tracking.track";
        public const string Mirror = "setup.tracking.mirror";
        public const string RemoveBackground = "setup.tracking.background";
        public const string Load = "setup.tracking.load";
        public const string Camera = "setup.tracking.camera";
        public const string Face = "setup.tracking.part.face";
        public const string Body = "setup.tracking.part.body";
        public const string LeftHand = "setup.tracking.part.lefthand";
        public const string RightHand = "setup.tracking.part.righthand";
        public const string Stopped = "setup.tracking.state.stopped";
        public const string Starting = "setup.tracking.state.starting";
        public const string Running = "setup.tracking.state.running";
        public const string NoPerson = "setup.tracking.state.noperson";
        public const string Waiting = "setup.tracking.state.waiting";
        public const string Failed = "setup.tracking.state.failed";
        public const string ProblemChoose = "setup.tracking.problem.choose";
        public const string ProblemNotFound = "setup.tracking.problem.notfound";
        public const string ProblemInUse = "setup.tracking.problem.inuse";
        public const string ProblemFailed = "setup.tracking.problem.failed";
        public const string ProblemNoPerson = "setup.tracking.problem.noperson";
    }

    /// <summary>
    /// Tracking on the setup screen (docs/&lt;lang&gt;/ui/screens/setup.md 4): turning it on and off, the
    /// tracking camera, and the camera image with the tracked points to check it works. Shows the
    /// source and the camera directly; changes go to them, and they save them.
    /// </summary>
    public sealed class TrackingCard : VisualElement
    {
        public const string UssClass = "vv-tracking-card";
        public const string PartsClass = UssClass + "__parts";
        public const string LoadClass = UssClass + "__load";

        /// <summary>How often the load line is sampled: often enough to follow, slow enough to read.</summary>
        public const long LoadIntervalMs = 1000;

        /// <summary>How often the points are redrawn, about the camera's rate.</summary>
        public const long RedrawIntervalMs = 33;

        private readonly ITrackingSource _tracking;
        private readonly ICaptureDeviceSource _camera;
        private readonly KeyedText _problemText;
        private readonly Label _problem;
        private readonly Label _noVideo;
        private readonly Label _parts;
        private readonly Label _load;
        private ILocalizer _localizer;

        public TrackingCard(ITrackingSource tracking, ICaptureDeviceSource camera)
        {
            _tracking = tracking ?? throw new ArgumentNullException(nameof(tracking));
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            AddToClassList(UssClass);
            AddToClassList(CaptureSourceCard.UssClass);
            Card card = Ui.Card(TrackingStrings.Title);
            Add(card);

            Status = Ui.Chip(StatusKind.Idle, TrackingStrings.Stopped);
            card.Add(Status);
            _problem = new Label();
            _problem.AddToClassList(CaptureSourceCard.ProblemClass);
            _problem.AddToClassList("vv-text-body");
            _problemText = new KeyedText(_problem, text =>
            {
                _problem.text = text;
                _problem.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
            });
            card.Add(_problem);

            // The camera image is shown as it is; the points are drawn over it, not into it.
            Preview = new Image { scaleMode = UnityEngine.ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            Preview.AddToClassList(CaptureSourceCard.PreviewClass);
            _noVideo = Ui.Text(CaptureStrings.NoVideo, CaptureSourceCard.PreviewEmptyClass, "vv-text-caption");
            Preview.Add(_noVideo);
            Overlay = new TrackingOverlay();
            Overlay.StretchToParentSize();
            Preview.Add(Overlay);
            card.Add(Preview);

            _parts = new Label();
            _parts.AddToClassList(PartsClass);
            _parts.AddToClassList("vv-text-body");
            card.Add(_parts);

            _load = new Label();
            _load.AddToClassList(LoadClass);
            _load.AddToClassList("vv-text-mono");
            card.Add(_load);

            Track = new SwitchToggle();
            Track.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    _tracking.Start();
                }
                else
                {
                    _tracking.Stop();
                }
            });
            card.Add(Row(TrackingStrings.Track, Track));

            MirrorToggle = new SwitchToggle();
            MirrorToggle.RegisterValueChangedCallback(evt => _tracking.SetOptions(_tracking.Options.Value.WithMirror(evt.newValue)));
            card.Add(Row(TrackingStrings.Mirror, MirrorToggle));

            RemoveBackgroundToggle = new SwitchToggle();
            RemoveBackgroundToggle.RegisterValueChangedCallback(evt => _tracking.SetOptions(_tracking.Options.Value.WithRemoveBackground(evt.newValue)));
            card.Add(Row(TrackingStrings.RemoveBackground, RemoveBackgroundToggle));

            Devices = Ui.Select(null);
            Devices.RegisterValueChangedCallback(evt => _camera.SelectDevice(string.IsNullOrEmpty(evt.newValue) ? null : evt.newValue));
            card.Add(Row(TrackingStrings.Camera, Devices));

            ActionButton refresh = Ui.Button(CaptureStrings.RefreshButton);
            refresh.clicked += RefreshDevices;
            card.Add(Row(CaptureStrings.Refresh, refresh));

            PanelLifetime.WhileAttached(this, services => Attach(services?.Localizer));
        }

        public StatusChip Status { get; }

        /// <summary>The problem's explanation as shown, for tests.</summary>
        public string ProblemKey => _problemText.Key;

        public SwitchToggle Track { get; }

        /// <summary>Shows the camera image as in a mirror; the avatar follows the same setting.</summary>
        public SwitchToggle MirrorToggle { get; }

        /// <summary>Cuts the person out and paints the background over before tracking.</summary>
        public SwitchToggle RemoveBackgroundToggle { get; }

        /// <summary>The load line as shown, for tests.</summary>
        public string LoadText => _load.text;

        public SelectField Devices { get; }

        public Image Preview { get; }

        public TrackingOverlay Overlay { get; }

        /// <summary>The parts line as shown, for tests.</summary>
        public string PartsText => _parts.text;

        /// <summary>The chip and the explanation for a state: shape, color, and text together (design-system.md 3.4). Pure.</summary>
        public static (StatusKind Kind, string StateKey, string ProblemKey) Describe(TrackingState state, TrackingProblem problem)
        {
            return state switch
            {
                TrackingState.Stopped => (StatusKind.Idle, TrackingStrings.Stopped, null),
                TrackingState.Starting => (StatusKind.Idle, TrackingStrings.Starting, null),
                TrackingState.Running when problem == TrackingProblem.NoPerson => (StatusKind.Warning, TrackingStrings.NoPerson, TrackingStrings.ProblemNoPerson),
                TrackingState.Running => (StatusKind.Success, TrackingStrings.Running, null),
                TrackingState.Unavailable when problem == TrackingProblem.CameraNotChosen => (StatusKind.Warning, TrackingStrings.Waiting, TrackingStrings.ProblemChoose),
                TrackingState.Unavailable => (StatusKind.Warning, TrackingStrings.Waiting, TrackingStrings.ProblemNotFound),
                TrackingState.Error when problem == TrackingProblem.CameraInUse => (StatusKind.Error, TrackingStrings.Failed, TrackingStrings.ProblemInUse),
                _ => (StatusKind.Error, TrackingStrings.Failed, TrackingStrings.ProblemFailed),
            };
        }

        /// <summary>
        /// "Face ✓  Body ✓  Left hand –  Right hand ✓": a mark and a word per part, so being tracked is
        /// never told by color alone (design-system.md 9.1). Pure.
        /// </summary>
        public static string PartsLine(TrackingFrame frame, bool running, Func<string, string> text)
        {
            if (!running || frame == null)
            {
                return string.Empty;
            }

            return $"{Part(text(TrackingStrings.Face), frame.Face)}   {Part(text(TrackingStrings.Body), frame.Body)}   " +
                $"{Part(text(TrackingStrings.LeftHand), frame.LeftHand)}   {Part(text(TrackingStrings.RightHand), frame.RightHand)}";
        }

        /// <summary>
        /// The values of the load line: frames per second, images tracked per second, and the CPU and
        /// GPU shares, which say "—" when unknown. Numbers only, so they read the same in every language. Pure.
        /// </summary>
        public static (string Fps, string Rate, string Cpu, string Gpu) LoadValues(AppLoad load, double trackedPerSecond)
        {
            string gpu = double.IsNaN(load.GpuPercent) ? "—" : $"{load.GpuPercent:0}%";
            return ($"{load.FramesPerSecond:0}", $"{trackedPerSecond:0}", $"{load.CpuPercent:0}%", gpu);
        }

        private static string Part(string name, TrackedPart part)
        {
            string mark = part.IsUsable ? "✓" : part.Status == TrackingStatus.Disabled ? "×" : "–";
            return $"{name} {mark}";
        }

        private IDisposable Attach(ILocalizer localizer)
        {
            _localizer = localizer;
            RefreshDevices();
            Show();
            ShowVideo(_camera.Texture.Value);
            IVisualElementScheduledItem redraw = schedule.Execute(Redraw).Every(RedrawIntervalMs);
            var sampler = new AppLoadSampler();
            sampler.Sample();
            IVisualElementScheduledItem load = schedule.Execute(() => ShowLoad(sampler.Sample())).Every(LoadIntervalMs);
            var subscriptions = new List<IDisposable>
            {
                _tracking.State.Subscribe(_ => Show()),
                _tracking.Problem.Subscribe(_ => Show()),
                _camera.Texture.Subscribe(ShowVideo),
                _camera.State.Subscribe(_ => Devices.SetValueWithoutNotify(_camera.SelectedDeviceId ?? string.Empty)),
                _tracking.Options.Subscribe(ShowOptions),
                new DisposeAction(redraw.Pause),
                new DisposeAction(load.Pause),
                sampler,
            };
            return new DisposeAll(subscriptions);
        }

        private void Redraw()
        {
            bool running = _tracking.State.Value == TrackingState.Running;
            UnityEngine.Texture texture = Preview.image;
            UnityEngine.Rect image = texture == null
                ? default
                : TrackingOverlay.FitRect(Preview.contentRect.width, Preview.contentRect.height, texture.width, texture.height);
            Overlay.Show(running ? _tracking.Current : null, image);
            string parts = PartsLine(_tracking.Current, running, key => _localizer?.Get(key) ?? key);
            if (!string.Equals(_parts.text, parts, StringComparison.Ordinal))
            {
                _parts.text = parts;
            }
        }

        private void ShowLoad(AppLoad load)
        {
            (string fps, string rate, string cpu, string gpu) = LoadValues(load, _tracking.ObservationsPerSecond);
            _load.text = _localizer?.Get(TrackingStrings.Load, ("fps", fps), ("rate", rate), ("cpu", cpu), ("gpu", gpu)) ?? $"{fps} fps · {rate} · CPU {cpu} · GPU {gpu}";
        }

        private void ShowOptions(TrackingOptions options)
        {
            RemoveBackgroundToggle.SetValueWithoutNotify(options.RemoveBackground);
            bool mirror = options.Mirror;
            MirrorToggle.SetValueWithoutNotify(mirror);

            // The image and the points over it flip together; the caption inside is flipped back so it reads.
            Preview.style.scale = new Scale(new UnityEngine.Vector3(mirror ? -1f : 1f, 1f, 1f));
            _noVideo.style.scale = new Scale(new UnityEngine.Vector3(mirror ? -1f : 1f, 1f, 1f));
        }

        private void RefreshDevices()
        {
            _camera.RefreshDevices();
            IReadOnlyList<CaptureDevice> devices = _camera.Devices;
            Devices.SetChoices(devices.Count > 0
                ? new[] { SelectChoice.FromKey(string.Empty, CaptureStrings.ChooseCamera) }.Concat(devices.Select(device => SelectChoice.FromText(device.Id, device.DisplayName)))
                : new[] { SelectChoice.FromKey(string.Empty, CaptureStrings.NoDevice) });
            Devices.SetValueWithoutNotify(_camera.SelectedDeviceId ?? string.Empty);
        }

        private void Show()
        {
            (StatusKind kind, string stateKey, string problemKey) = Describe(_tracking.State.Value, _tracking.Problem.Value);
            Status.Status = kind;
            Status.TextKey = stateKey;
            _problemText.Key = problemKey;
            Track.SetValueWithoutNotify(_tracking.State.Value != TrackingState.Stopped);
            ShowOptions(_tracking.Options.Value);
            Devices.SetValueWithoutNotify(_camera.SelectedDeviceId ?? string.Empty);
        }

        private void ShowVideo(UnityEngine.Texture texture)
        {
            Preview.image = texture;
            _noVideo.style.display = texture == null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static SettingRow Row(string nameKey, VisualElement control)
        {
            SettingRow row = Ui.SettingRow(nameKey);
            row.Add(control);
            return row;
        }

        private sealed class DisposeAll : IDisposable
        {
            private readonly List<IDisposable> _items;

            public DisposeAll(List<IDisposable> items)
            {
                _items = items;
            }

            public void Dispose()
            {
                foreach (IDisposable item in _items)
                {
                    item.Dispose();
                }
            }
        }
    }
}
