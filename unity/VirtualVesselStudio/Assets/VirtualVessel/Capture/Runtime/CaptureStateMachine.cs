using System;
using System.Collections.Generic;

namespace VirtualVessel.Capture
{
    /// <summary>What happened to a source; <see cref="CaptureStateMachine.Next"/> turns it into a state.</summary>
    public enum CaptureEvent
    {
        StartRequested,
        StopRequested,

        /// <summary>The chosen device or monitor is not there, or went away.</summary>
        DeviceMissing,

        /// <summary>The device opened; frames may follow.</summary>
        Opened,
        FrameArrived,

        /// <summary>Open, but nothing arrived for a while.</summary>
        NoFrame,
        Failed,
        PluginMissing,
    }

    /// <summary>A state and the problem to tell the user.</summary>
    public readonly struct CaptureStatus : IEquatable<CaptureStatus>
    {
        public CaptureStatus(CaptureState state, CaptureProblem problem)
        {
            State = state;
            Problem = problem;
        }

        public static CaptureStatus Stopped => new CaptureStatus(CaptureState.Stopped, CaptureProblem.None);

        public CaptureState State { get; }

        public CaptureProblem Problem { get; }

        public bool Equals(CaptureStatus other)
        {
            return State == other.State && Problem == other.Problem;
        }

        public override bool Equals(object obj)
        {
            return obj is CaptureStatus other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)State * 31) + (int)Problem;
        }

        public override string ToString()
        {
            return Problem == CaptureProblem.None ? State.ToString() : $"{State} ({Problem})";
        }
    }

    /// <summary>
    /// The decisions of a capture source: state transitions and device choice (capture detailed
    /// design 6.1, 6.3). Pure: the sources act on the results.
    /// </summary>
    public static class CaptureStateMachine
    {
        /// <summary>The status after <paramref name="captureEvent"/>. A stopped source only reacts to a start.</summary>
        public static CaptureStatus Next(CaptureStatus current, CaptureEvent captureEvent, CaptureSourceKind kind)
        {
            if (captureEvent == CaptureEvent.StopRequested)
            {
                return CaptureStatus.Stopped;
            }

            if (current.State == CaptureState.Stopped && captureEvent != CaptureEvent.StartRequested)
            {
                return current;
            }

            switch (captureEvent)
            {
                case CaptureEvent.StartRequested:
                    return IsActive(current.State) ? current : new CaptureStatus(CaptureState.Starting, CaptureProblem.None);
                case CaptureEvent.DeviceMissing:
                    return new CaptureStatus(CaptureState.Unavailable, kind == CaptureSourceKind.SubScreenCapture ? CaptureProblem.MonitorNotFound : CaptureProblem.DeviceNotFound);
                case CaptureEvent.Opened:
                    return current.State == CaptureState.Starting || current.State == CaptureState.Unavailable
                        ? new CaptureStatus(CaptureState.Ready, CaptureProblem.None)
                        : current;
                case CaptureEvent.FrameArrived:
                    return current.State == CaptureState.Starting || current.State == CaptureState.Ready || current.State == CaptureState.Capturing
                        ? new CaptureStatus(CaptureState.Capturing, CaptureProblem.None)
                        : current;
                case CaptureEvent.NoFrame:
                    return current.State == CaptureState.Ready ? new CaptureStatus(CaptureState.Ready, CaptureProblem.NoSignal) : current;
                case CaptureEvent.Failed:
                    return new CaptureStatus(CaptureState.Error, CaptureProblem.StartFailed);
                case CaptureEvent.PluginMissing:
                    return new CaptureStatus(CaptureState.Error, CaptureProblem.PluginMissing);
                default:
                    return current;
            }
        }

        /// <summary>Starting, Ready, or Capturing: the device is (being) opened.</summary>
        public static bool IsActive(CaptureState state)
        {
            return state == CaptureState.Starting || state == CaptureState.Ready || state == CaptureState.Capturing;
        }

        /// <summary>Whether an unavailable source should look for its device again.</summary>
        public static bool ShouldCheckAgain(CaptureState state, long lastCheck, long now, long interval)
        {
            return state == CaptureState.Unavailable && now - lastCheck >= interval;
        }

        /// <summary>
        /// The device to open: the chosen one when it is there, or the default when none is chosen.
        /// Null when the chosen device is missing (the source becomes Unavailable) or there is none.
        /// </summary>
        public static CaptureDevice Choose(IReadOnlyList<CaptureDevice> devices, string selectedId, Func<IReadOnlyList<CaptureDevice>, CaptureDevice> chooseDefault)
        {
            if (devices == null || devices.Count == 0)
            {
                return null;
            }

            if (string.IsNullOrEmpty(selectedId))
            {
                return chooseDefault != null ? chooseDefault(devices) : devices[0];
            }

            foreach (CaptureDevice device in devices)
            {
                if (string.Equals(device.Id, selectedId, StringComparison.Ordinal))
                {
                    return device;
                }
            }

            return null;
        }
    }
}
