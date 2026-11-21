using System;
using System.Collections.Generic;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;

namespace VirtualVessel.Capture.SubScreen
{
    /// <summary>
    /// SubScreenCapture: a monitor of this PC duplicated by the native plugin, uploaded to a texture
    /// only when there is a new frame (capture detailed design 8.2). Video only.
    /// </summary>
    internal sealed class SubScreenCaptureSource : CaptureSourceBase, IPointerOption
    {
        public const string SourceId = "subscreen";

        private readonly IDesktopDuplication _duplication;
        private readonly ObservableValue<bool> _drawPointer = new ObservableValue<bool>(true);
        private readonly NativeFrames _frames;
        private IReadOnlyList<MonitorDescription> _monitors = Array.Empty<MonitorDescription>();

        public SubScreenCaptureSource(IDesktopDuplication duplication, CaptureSettings settings, IMonotonicClock clock, IPerformanceMetrics metrics, ILog log)
            : base(SourceId, CaptureSourceKind.SubScreenCapture, clock, metrics, log)
        {
            _duplication = duplication ?? throw new ArgumentNullException(nameof(duplication));
            _frames = new NativeFrames(SourceId, "SubScreenCapture", metrics, log);
            SelectDevice((settings ?? throw new ArgumentNullException(nameof(settings))).SubScreenMonitorId);
            SetFlip(settings.SubScreenFlip);
            _drawPointer.Value = settings.SubScreenDrawPointer;
        }

        public IReadOnlyObservable<bool> DrawPointer => _drawPointer;

        public void SetDrawPointer(bool draw)
        {
            if (!_drawPointer.Set(draw))
            {
                return;
            }

            // The native session takes the setting when it opens, so a running source opens again.
            if (State.Value != CaptureState.Stopped)
            {
                SelectDevice(SelectedDeviceId);
            }
        }

        /// <summary>
        /// The monitor's name in device lists: its device name and size, such as "DISPLAY2 (1920×1080)".
        /// It has no words, so it reads the same in every language; a localized label is left to the
        /// setup screen (capture detailed design 17).
        /// </summary>
        internal static string DisplayNameOf(MonitorDescription monitor)
        {
            string name = monitor.DeviceName.StartsWith(@"\\.\", StringComparison.Ordinal) ? monitor.DeviceName.Substring(4) : monitor.DeviceName;
            return $"{name} ({monitor.Width}×{monitor.Height})";
        }

        /// <summary>The first monitor that is not the main one; the main one when it is alone (capture detailed design 6.3).</summary>
        internal static string DefaultMonitor(IReadOnlyList<MonitorDescription> monitors)
        {
            foreach (MonitorDescription monitor in monitors)
            {
                if (!monitor.IsPrimary)
                {
                    return monitor.DeviceName;
                }
            }

            return monitors.Count > 0 ? monitors[0].DeviceName : null;
        }

        protected override IReadOnlyList<CaptureDevice> ListDevices()
        {
            _monitors = _duplication.ListMonitors();
            var devices = new CaptureDevice[_monitors.Count];
            for (int i = 0; i < devices.Length; i++)
            {
                devices[i] = new CaptureDevice(_monitors[i].DeviceName, DisplayNameOf(_monitors[i]));
            }

            return devices;
        }

        protected override CaptureDevice ChooseDefault(IReadOnlyList<CaptureDevice> devices)
        {
            string id = DefaultMonitor(_monitors);
            foreach (CaptureDevice device in devices)
            {
                if (string.Equals(device.Id, id, StringComparison.Ordinal))
                {
                    return device;
                }
            }

            return devices[0];
        }

        protected override void Open(CaptureDevice device)
        {
            // The native side looks for the monitor and creates the duplication on its own thread, so
            // the main thread does not wait; OnTick reports the outcome.
            _frames.Begin(_duplication.Open(device.Id, _drawPointer.Value), device.Id);
            SetInfo(new CaptureSourceInfo(0, 0, 0, device.DisplayName));
        }

        protected override void Close()
        {
            _frames.Close();
        }

        protected override void OnTick(long now)
        {
            TickNative(_frames, now);
        }
    }
}
