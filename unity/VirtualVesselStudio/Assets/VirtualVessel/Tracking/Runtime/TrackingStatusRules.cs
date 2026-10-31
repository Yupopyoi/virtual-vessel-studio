using VirtualVessel.Capture;

namespace VirtualVessel.Tracking
{
    /// <summary>What the tracking source reports to the user, decided from its parts (tracking detailed design 7.1, 11).</summary>
    internal static class TrackingStatusRules
    {
        /// <summary>
        /// The state and problem to show, from the camera, the provider, and whether anyone is seen
        /// (tracking detailed design 7.1, 11). Pure.
        /// </summary>
        internal static (TrackingState State, TrackingProblem Problem) Describe(
            bool wanted,
            bool hasProvider,
            bool needsCamera,
            CaptureState cameraState,
            CaptureProblem cameraProblem,
            TrackingState providerState,
            bool personSeen)
        {
            if (!wanted)
            {
                return (TrackingState.Stopped, TrackingProblem.None);
            }

            if (!hasProvider)
            {
                return (TrackingState.Error, TrackingProblem.StartFailed);
            }

            if (needsCamera)
            {
                switch (cameraState)
                {
                    case CaptureState.Stopped when cameraProblem == CaptureProblem.DeviceNotChosen:
                        return (TrackingState.Unavailable, TrackingProblem.CameraNotChosen);
                    case CaptureState.Unavailable:
                        return (TrackingState.Unavailable, TrackingProblem.CameraNotFound);
                    case CaptureState.Error:
                        return (TrackingState.Error, cameraProblem == CaptureProblem.DeviceInUse ? TrackingProblem.CameraInUse : TrackingProblem.StartFailed);
                    case CaptureState.Stopped:
                    case CaptureState.Starting:
                    case CaptureState.Ready:
                        return (TrackingState.Starting, TrackingProblem.None);
                }
            }

            switch (providerState)
            {
                case TrackingState.Error:
                    return (TrackingState.Error, TrackingProblem.StartFailed);
                case TrackingState.Running:
                    return (TrackingState.Running, personSeen ? TrackingProblem.None : TrackingProblem.NoPerson);
                default:
                    return (TrackingState.Starting, TrackingProblem.None);
            }
        }
    }
}
