using System;
using System.Collections.Generic;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;

namespace VirtualVessel.Capture.Game
{
    /// <summary>The size and frame rate a camera source asks for; the device may deliver something close.</summary>
    internal readonly struct WebCamRequest
    {
        public WebCamRequest(int width, int height, int frameRate)
        {
            Width = width;
            Height = height;
            FrameRate = frameRate;
        }

        public int Width { get; }

        public int Height { get; }

        public int FrameRate { get; }
    }

    /// <summary>
    /// A UVC camera through WebCamTexture, as the prototype did (capture detailed design 8.1): the
    /// capture board of GameCapture, and the webcam for tracking (tracking detailed design 5.4). Video
    /// only; the board's audio comes with the Audio module.
    /// </summary>
    internal sealed class WebCamCaptureSource : CaptureSourceBase
    {
        public const string GameSourceId = "game";
        public const string CameraSourceId = "camera";

        /// <summary>Without a frame this long, after opening or after the last frame, the board probably has no input.</summary>
        public static readonly TimeSpan NoSignalAfter = TimeSpan.FromSeconds(5);

        /// <summary>Without a frame this long, the device may have gone away, so the device list is checked.</summary>
        public static readonly TimeSpan QuietBeforeDeviceCheck = TimeSpan.FromSeconds(1);

        // A camera reports a tiny placeholder size until its first real image arrives.
        private const int PlaceholderSize = 16;

        private readonly ICameraDevices _devices;
        private readonly WebCamRequest _request;
        private readonly Func<string, bool> _inUseElsewhere;
        private readonly long _noSignalTicks;
        private readonly long _quietTicks;
        private readonly long _checkTicks;
        private ICameraStream _stream;
        private long _openedAt;
        private long _lastFrameAt;
        private long _lastDeviceCheck;
        private FrameRateMeter _rate;

        /// <param name="inUseElsewhere">Whether another source has a device open; null when no other source opens cameras.</param>
        private WebCamCaptureSource(string id, CaptureSourceKind kind, ICameraDevices devices, WebCamRequest request, string deviceId, CaptureFlip flip, Func<string, bool> inUseElsewhere, IMonotonicClock clock, IPerformanceMetrics metrics, ILog log)
            : base(id, kind, clock, metrics, log)
        {
            _devices = devices ?? throw new ArgumentNullException(nameof(devices));
            _request = request;
            _inUseElsewhere = inUseElsewhere;
            _noSignalTicks = ToTicks(NoSignalAfter, clock.Frequency);
            _quietTicks = ToTicks(QuietBeforeDeviceCheck, clock.Frequency);
            _checkTicks = ToTicks(RetryInterval, clock.Frequency);
            SelectDevice(deviceId);
            SetFlip(flip);
        }

        /// <summary>The capture board of GameCapture.</summary>
        public static WebCamCaptureSource ForGame(ICameraDevices devices, CaptureSettings settings, IMonotonicClock clock, IPerformanceMetrics metrics, ILog log, Func<string, bool> inUseElsewhere = null)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var request = new WebCamRequest(settings.GameWidth, settings.GameHeight, settings.GameFrameRate);
            return new WebCamCaptureSource(GameSourceId, CaptureSourceKind.GameCapture, devices, request, settings.GameDeviceId, settings.GameFlip, inUseElsewhere, clock, metrics, log);
        }

        /// <summary>
        /// The webcam for tracking. Never flipped: left and right are decided by Tracking and Avatar
        /// (tracking detailed design 5.4).
        /// </summary>
        public static WebCamCaptureSource ForTrackingCamera(ICameraDevices devices, CaptureSettings settings, IMonotonicClock clock, IPerformanceMetrics metrics, ILog log, Func<string, bool> inUseElsewhere = null)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var request = new WebCamRequest(settings.CameraWidth, settings.CameraHeight, settings.CameraFrameRate);
            return new WebCamCaptureSource(CameraSourceId, CaptureSourceKind.Camera, devices, request, settings.CameraDeviceId, CaptureFlip.None, inUseElsewhere, clock, metrics, log);
        }

        protected override bool RequiresChosenDevice => true;

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
            // WebCamTexture cannot open one device twice; say so instead of failing to play.
            if (_inUseElsewhere != null && _inUseElsewhere(device.Id))
            {
                throw new CaptureDeviceInUseException(device.DisplayName);
            }

            _stream = _devices.Open(device.Id, _request.Width, _request.Height, _request.FrameRate);
            if (!_stream.IsPlaying)
            {
                // Typically the device is in use by another application.
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
