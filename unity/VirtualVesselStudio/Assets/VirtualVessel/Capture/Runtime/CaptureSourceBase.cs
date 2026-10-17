using System;
using System.Collections.Generic;
using UnityEngine;
using VirtualVessel.Core.State;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;

namespace VirtualVessel.Capture
{
    /// <summary>
    /// What every capture source shares: the observable state, choosing and opening a device, looking
    /// for a missing device again, and the frame metrics (capture detailed design 6, 7.3, 11).
    /// </summary>
    /// <remarks>
    /// The transitions come from <see cref="CaptureStateMachine"/>; a source only reports what happened
    /// (opened, a frame, failed). Main thread only.
    /// </remarks>
    internal abstract class CaptureSourceBase : ICaptureDeviceSource, IDisposable
    {
        /// <summary>How often an unavailable source looks for its device again (capture detailed design 12).</summary>
        public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);

        private readonly ObservableValue<CaptureState> _state = new ObservableValue<CaptureState>(CaptureState.Stopped);
        private readonly ObservableValue<CaptureProblem> _problem = new ObservableValue<CaptureProblem>(CaptureProblem.None);
        private readonly ObservableValue<CaptureSourceInfo> _info = new ObservableValue<CaptureSourceInfo>(default);
        private readonly ObservableValue<Texture> _texture = new ObservableValue<Texture>(null);
        private readonly IMonotonicClock _clock;
        private readonly long _retryTicks;
        private readonly PerfCounter _frames;
        private readonly PerfTimer _frameInterval;
        private IReadOnlyList<CaptureDevice> _devices = Array.Empty<CaptureDevice>();
        private CaptureStatus _status = CaptureStatus.Stopped;
        private long _lastCheck;
        private long _lastFrame;

        protected CaptureSourceBase(string id, CaptureSourceKind kind, IMonotonicClock clock, IPerformanceMetrics metrics, ILog log)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("A capture source needs an id.", nameof(id));
            }

            Id = id;
            Kind = kind;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Log = log;
            _retryTicks = (long)(RetryInterval.TotalSeconds * clock.Frequency);
            _frames = metrics?.Counter("Capture", id + ".Frames");
            _frameInterval = metrics?.Timer("Capture", id + ".FrameInterval");
        }

        public string Id { get; }

        public CaptureSourceKind Kind { get; }

        public IReadOnlyObservable<CaptureState> State => _state;

        public IReadOnlyObservable<CaptureProblem> Problem => _problem;

        public IReadOnlyObservable<CaptureSourceInfo> Info => _info;

        public IReadOnlyObservable<Texture> Texture => _texture;

        public IReadOnlyList<CaptureDevice> Devices => _devices;

        public string SelectedDeviceId { get; private set; }

        /// <summary>The device being captured, or null.</summary>
        protected CaptureDevice OpenDevice { get; private set; }

        protected ILog Log { get; }

        protected IMonotonicClock Clock => _clock;

        /// <summary>Lists the devices again. A failure leaves the list empty and the state as it is.</summary>
        public void RefreshDevices()
        {
            TryListDevices(out _);
        }

        public void Start()
        {
            if (CaptureStateMachine.IsActive(_status.State))
            {
                return;
            }

            Apply(CaptureEvent.StartRequested);
            TryOpen();
        }

        public void Stop()
        {
            if (_status.State == CaptureState.Stopped)
            {
                return;
            }

            CloseDevice();
            Apply(CaptureEvent.StopRequested);
        }

        public void SelectDevice(string deviceId)
        {
            SelectedDeviceId = string.IsNullOrEmpty(deviceId) ? null : deviceId;
            if (_status.State == CaptureState.Stopped)
            {
                return;
            }

            // Capture again from the chosen device.
            CloseDevice();
            SetStatus(new CaptureStatus(CaptureState.Starting, CaptureProblem.None));
            TryOpen();
        }

        /// <summary>Called by the service once per frame on the main thread.</summary>
        public void Tick(long now)
        {
            if (CaptureStateMachine.ShouldCheckAgain(_status.State, _lastCheck, now, _retryTicks))
            {
                TryOpen();
                return;
            }

            if (!CaptureStateMachine.IsActive(_status.State))
            {
                return;
            }

            try
            {
                OnTick(now);
            }
            catch (Exception exception)
            {
                Log?.Error($"Capture source '{Id}' failed while capturing.", exception);
                CloseDevice();
                Apply(FailureEvent(exception));
            }
        }

        public void Dispose()
        {
            CloseDevice();
            SetStatus(CaptureStatus.Stopped);
        }

        /// <summary>What a failure means for the state: a missing plugin is a developer problem of its own.</summary>
        internal static CaptureEvent FailureEvent(Exception exception)
        {
            return exception is CapturePluginMissingException ? CaptureEvent.PluginMissing : CaptureEvent.Failed;
        }

        /// <summary>The devices there are now. May be called often while unavailable; keep it light.</summary>
        protected abstract IReadOnlyList<CaptureDevice> ListDevices();

        /// <summary>The device to use when none is chosen (capture detailed design 6.3).</summary>
        protected virtual CaptureDevice ChooseDefault(IReadOnlyList<CaptureDevice> devices)
        {
            return devices[0];
        }

        /// <summary>
        /// Opens <paramref name="device"/>. Reports <see cref="CaptureEvent.Opened"/> here, or from
        /// <see cref="OnTick"/> when opening finishes later; the source is Starting until then.
        /// </summary>
        protected abstract void Open(CaptureDevice device);

        /// <summary>Releases the device and anything made for it. Must be safe to call when nothing is open.</summary>
        protected abstract void Close();

        /// <summary>Per-frame work while starting, ready, or capturing.</summary>
        protected abstract void OnTick(long now);

        protected void Report(CaptureEvent captureEvent)
        {
            Apply(captureEvent);
        }

        /// <summary>A new frame arrived at <paramref name="now"/>: counts it, measures the interval, and marks Capturing.</summary>
        protected void ReportFrame(long now)
        {
            _frames?.Increment();
            if (_lastFrame != 0)
            {
                _frameInterval?.Record(_clock.GetElapsed(_lastFrame, now));
            }

            _lastFrame = now;
            Apply(CaptureEvent.FrameArrived);
        }

        /// <summary>The open device went away: releases it and waits for it to come back (Unavailable).</summary>
        protected void ReportDeviceLost()
        {
            CloseDevice();
            _lastCheck = _clock.GetTimestamp();
            Apply(CaptureEvent.DeviceMissing);
        }

        protected void SetTexture(Texture texture)
        {
            _texture.Value = texture;
        }

        protected void SetInfo(CaptureSourceInfo info)
        {
            _info.Value = info;
        }

        private bool TryListDevices(out Exception failure)
        {
            try
            {
                _devices = ListDevices() ?? Array.Empty<CaptureDevice>();
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                _devices = Array.Empty<CaptureDevice>();
                Log?.Warning($"Capture source '{Id}' could not list its devices.", exception);
                failure = exception;
                return false;
            }
        }

        private void TryOpen()
        {
            _lastCheck = _clock.GetTimestamp();
            if (!TryListDevices(out Exception failure))
            {
                Apply(FailureEvent(failure));
                return;
            }

            CaptureDevice device = CaptureStateMachine.Choose(_devices, SelectedDeviceId, ChooseDefault);
            if (device == null)
            {
                Apply(CaptureEvent.DeviceMissing);
                return;
            }

            // A device chosen by default stays chosen. When it goes away the source waits for it,
            // instead of moving to another device such as the main monitor or a webcam, which could
            // show the user something they did not mean to (capture detailed design 13).
            SelectedDeviceId = device.Id;

            try
            {
                OpenDevice = device;
                Apply(CaptureEvent.Opening);
                Open(device);
            }
            catch (Exception exception)
            {
                Log?.Warning($"Capture source '{Id}' could not open '{device.DisplayName}'.", exception);
                CloseDevice();
                Apply(FailureEvent(exception));
            }
        }

        private void CloseDevice()
        {
            try
            {
                Close();
            }
            catch (Exception exception)
            {
                // Releasing must go on: one failed release must not leak the rest.
                Log?.Warning($"Capture source '{Id}' failed while closing.", exception);
            }
            finally
            {
                OpenDevice = null;
                _lastFrame = 0;
                _texture.Value = null;
                _info.Value = default;
            }
        }

        private void Apply(CaptureEvent captureEvent)
        {
            SetStatus(CaptureStateMachine.Next(_status, captureEvent, Kind));
        }

        private void SetStatus(CaptureStatus next)
        {
            if (next.Equals(_status))
            {
                return;
            }

            CaptureStatus previous = _status;
            _status = next;
            _state.Value = next.State;
            _problem.Value = next.Problem;
            string device = OpenDevice != null ? $" on '{OpenDevice.DisplayName}'" : string.Empty;
            LogLevel level = next.State == CaptureState.Error ? LogLevel.Warning : LogLevel.Information;
            Log?.Write(level, $"Capture source '{Id}'{device}: {previous} -> {next}.");
        }
    }
}
