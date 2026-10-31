using VirtualVessel.Capture;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;

namespace VirtualVessel.Tracking.MediaPipe
{
    /// <summary>
    /// Creates the MediaPipe provider for the composition root, which compiles this call only when the
    /// plugin is installed (tracking detailed design 16.2).
    /// </summary>
    public static class MediaPipeTracking
    {
        /// <summary>The model folder under StreamingAssets, where tools/deps/fetch-mediapipe.ps1 puts the models.</summary>
        public const string ModelFolder = "Tracking/MediaPipe";

        /// <summary>
        /// Whether MediaPipe's left and right face values are the image's sides rather than the
        /// person's. To be confirmed with a camera by winking (tracking detailed design 6.3).
        /// </summary>
        public const bool FaceSidesMirrored = false;

        public static ITrackingProvider Create(IVideoCaptureSource camera, string modelDirectory, IMonotonicClock clock, IPerformanceMetrics metrics, ILog log)
        {
            return new MediaPipeTrackingProvider(camera, modelDirectory, FaceSidesMirrored, clock, metrics, log);
        }
    }
}
