using System;
using System.Collections.Generic;
using UnityEngine;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;

namespace VirtualVessel.Capture.SubScreen
{
    /// <summary>
    /// SubScreenCapture: a monitor of this PC duplicated by the native plugin, uploaded to a texture
    /// only when there is a new frame (capture detailed design 8.2). Video only.
    /// </summary>
    internal sealed class SubScreenCaptureSource : CaptureSourceBase
    {
        public const string SourceId = "subscreen";

        private readonly IDesktopDuplication _duplication;
        private readonly PerfTimer _upload;
        private readonly PerfCounter _skipped;
        private readonly PerfCounter _recovered;
        private IReadOnlyList<MonitorDescription> _monitors = Array.Empty<MonitorDescription>();
        private IDuplicationSession _session;
        private DuplicationStatus _status;
        private Texture2D _texture;
        private ulong _uploaded;
        private FrameRateMeter _rate;

        public SubScreenCaptureSource(IDesktopDuplication duplication, CaptureSettings settings, IMonotonicClock clock, IPerformanceMetrics metrics, ILog log)
            : base(SourceId, CaptureSourceKind.SubScreenCapture, clock, metrics, log)
        {
            _duplication = duplication ?? throw new ArgumentNullException(nameof(duplication));
            _upload = metrics?.Timer("Capture", SourceId + ".Upload");
            _skipped = metrics?.Counter("Capture", SourceId + ".Skipped");
            _recovered = metrics?.Counter("Capture", SourceId + ".Recovered");
            SelectDevice((settings ?? throw new ArgumentNullException(nameof(settings))).SubScreenMonitorId);
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
            _session = _duplication.Open(device.Id);
            _status = default;
            _uploaded = 0;
            _rate = default;
            SetInfo(new CaptureSourceInfo(0, 0, 0, device.DisplayName));
        }

        protected override void Close()
        {
            try
            {
                _session?.Dispose();
            }
            finally
            {
                _session = null;
                DestroyTexture(_texture);
                _texture = null;
            }
        }

        protected override void OnTick(long now)
        {
            if (_session == null)
            {
                return;
            }

            DuplicationStatus status = _session.GetStatus();
            Note(_status, status);
            _status = status;

            switch (status.State)
            {
                case DuplicationState.Starting:
                    return;
                case DuplicationState.MonitorLost:
                    ReportDeviceLost();
                    return;
                case DuplicationState.Failed:
                    throw new InvalidOperationException($"Duplicating '{OpenDevice.Id}' failed with HRESULT 0x{status.LastError:X8}.");
            }

            // Running, or recovering with the last image kept.
            if (State.Value == CaptureState.Starting)
            {
                Report(CaptureEvent.Opened);
            }

            if (status.FrameNumber == _uploaded || !_session.TryTakeFrame(_uploaded, out DuplicationFrame frame))
            {
                // A still screen sends no frames; that is not a failure, but nothing is arriving.
                CaptureSourceInfo info = Info.Value;
                double rate = _rate.RateAt(now, Clock.Frequency);
                if (info.FrameRate != rate)
                {
                    SetInfo(new CaptureSourceInfo(info.Width, info.Height, rate, info.DeviceName));
                }

                return;
            }

            Upload(frame);
            _uploaded = frame.Number;
            _rate = _rate.Next(now, Clock.Frequency);
            ReportFrame(now);
            SetInfo(new CaptureSourceInfo(frame.Width, frame.Height, _rate.Rate, OpenDevice.DisplayName));
        }

        private void Upload(DuplicationFrame frame)
        {
            if (frame.Data == IntPtr.Zero || frame.Width <= 0 || frame.Height <= 0)
            {
                throw new InvalidOperationException($"The duplication gave an empty frame ({frame.Width}×{frame.Height}).");
            }

            if (_texture == null || _texture.width != frame.Width || _texture.height != frame.Height)
            {
                // A different texture for a different size (capture detailed design 5). Desktop
                // images are sRGB, as Unity expects of color textures.
                Texture2D previous = _texture;
                _texture = new Texture2D(frame.Width, frame.Height, TextureFormat.BGRA32, mipChain: false, linear: false)
                {
                    name = "SubScreenCapture",
                    wrapMode = TextureWrapMode.Clamp,
                };
                SetTexture(_texture);
                DestroyTexture(previous);
            }

            // The frame is B8G8R8A8 and BGRA32 matches it, so the bytes are uploaded as they are.
            long token = _upload?.Begin() ?? 0;
            _texture.LoadRawTextureData(frame.Data, frame.ByteCount);
            _texture.Apply(updateMipmaps: false);
            _upload?.End(token);
        }

        /// <summary>Counts what the native side reports and logs its state changes, without logging every frame.</summary>
        private void Note(DuplicationStatus previous, DuplicationStatus current)
        {
            if (current.Overwritten > previous.Overwritten)
            {
                _skipped?.Add((long)(current.Overwritten - previous.Overwritten));
            }

            if (current.Recoveries > previous.Recoveries)
            {
                _recovered?.Add(current.Recoveries - previous.Recoveries);
            }

            if (current.State != previous.State || current.LastError != previous.LastError)
            {
                string error = current.LastError != 0 ? $" (last HRESULT 0x{current.LastError:X8})" : string.Empty;
                Log?.Information($"Capture source '{Id}': the duplication of '{OpenDevice?.Id}' is {current.State}{error}.");
            }
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (UnityEngine.Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
