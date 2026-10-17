using System;
using System.Collections.Generic;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;

namespace VirtualVessel.Capture.Game
{
    /// <summary>
    /// GameCapture: a capture board opened as a UVC camera through WebCamTexture, as the prototype
    /// did (capture detailed design 8.1). Video only; the board's audio comes with the Audio module.
    /// </summary>
    internal sealed class GameCaptureSource : CaptureSourceBase
    {
        public const string SourceId = "game";

        /// <summary>Without a frame this long, after opening or after the last frame, the board probably has no input.</summary>
        public static readonly TimeSpan NoSignalAfter = TimeSpan.FromSeconds(5);

        /// <summary>Without a frame this long, the device may have gone away, so the device list is checked.</summary>
        public static readonly TimeSpan QuietBeforeDeviceCheck = TimeSpan.FromSeconds(1);

        // A camera reports a tiny placeholder size until its first real image arrives.
        private const int PlaceholderSize = 16;

        private readonly ICameraDevices _devices;
        private readonly CaptureSettings _settings;
        private readonly long _noSignalTicks;
        private readonly long _quietTicks;
        private readonly long _checkTicks;
        private ICameraStream _stream;
        private long _openedAt;
        private long _lastFrameAt;
        private long _lastDeviceCheck;
        private FrameRateMeter _rate;

        public GameCaptureSource(ICameraDevices devices, CaptureSettings settings, IMonotonicClock clock, IPerformanceMetrics metrics, ILog log)
            : base(SourceId, CaptureSourceKind.GameCapture, clock, metrics, log)
        {
            _devices = devices ?? throw new ArgumentNullException(nameof(devices));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _noSignalTicks = ToTicks(NoSignalAfter, clock.Frequency);
            _quietTicks = ToTicks(QuietBeforeDeviceCheck, clock.Frequency);
            _checkTicks = ToTicks(RetryInterval, clock.Frequency);
            SelectDevice(settings.GameDeviceId);
        }

        /// <summary>
        /// Whether to look for the open device in the device list. Only after a quiet second, since a
        /// device that sends frames is there, and listing devices queries the OS and allocates; and at
        /// most once per <paramref name="checkTicks"/>.
        /// </summary>
        internal static bool ShouldCheckDevice(long quietSince, long lastCheck, long now, long quietTicks, long checkTicks)
        {
            return now - quietSince >= quietTicks && now - lastCheck >= checkTicks;
        }

        internal static bool Contains(IReadOnlyList<CaptureDevice> devices, string id)
        {
            foreach (CaptureDevice device in devices)
            {
                if (string.Equals(device.Id, id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        protected override IReadOnlyList<CaptureDevice> ListDevices()
        {
            return _devices.List();
        }

        protected override void Open(CaptureDevice device)
        {
            _stream = _devices.Open(device.Id, _settings.GameWidth, _settings.GameHeight, _settings.GameFrameRate);
            if (!_stream.IsPlaying)
            {
                // Typically the board is in use by another application.
                throw new InvalidOperationException($"The camera '{device.DisplayName}' did not start playing.");
            }

            long now = Clock.GetTimestamp();
            _openedAt = now;
            _lastFrameAt = 0;
            _lastDeviceCheck = now;
            _rate = default;
            SetInfo(new CaptureSourceInfo(0, 0, 0, device.DisplayName));
            Report(CaptureEvent.Opened);
        }

        protected override void Close()
        {
            _stream?.Dispose();
            _stream = null;
        }

        protected override void OnTick(long now)
        {
            if (_stream == null)
            {
                return;
            }

            if (_stream.DidUpdateThisFrame && _stream.Width > PlaceholderSize)
            {
                OnFrame(now);
                return;
            }

            long quietSince = _lastFrameAt != 0 ? _lastFrameAt : _openedAt;
            if (ShouldCheckDevice(quietSince, _lastDeviceCheck, now, _quietTicks, _checkTicks))
            {
                // WebCamTexture has no event for a device that goes away; its image just stops.
                _lastDeviceCheck = now;
                if (!Contains(_devices.List(), OpenDevice.Id))
                {
                    ReportDeviceLost();
                    return;
                }
            }

            if (now - quietSince >= _noSignalTicks)
            {
                Report(CaptureEvent.NoFrame);
                CaptureSourceInfo info = Info.Value;
                if (info.FrameRate != 0)
                {
                    SetInfo(new CaptureSourceInfo(info.Width, info.Height, 0, info.DeviceName));
                }
            }
        }

        private void OnFrame(long now)
        {
            if (_lastFrameAt == 0)
            {
                SetTexture(_stream.Texture);
            }

            _lastFrameAt = now;
            _rate = _rate.Next(now, Clock.Frequency);
            ReportFrame(now);
            SetInfo(new CaptureSourceInfo(_stream.Width, _stream.Height, _rate.Rate, OpenDevice.DisplayName));
        }

        private static long ToTicks(TimeSpan time, long frequency)
        {
            return (long)(time.TotalSeconds * frequency);
        }
    }
}
