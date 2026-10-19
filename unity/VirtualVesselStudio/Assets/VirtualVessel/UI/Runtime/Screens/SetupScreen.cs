using UnityEngine.UIElements;
using VirtualVessel.Capture;
using VirtualVessel.Tracking;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.Screens
{
    /// <summary>
    /// The setup screen: a capture Card per source shown on the stream, the tracking Card, then the
    /// app settings (docs/&lt;lang&gt;/ui/screens/setup.md).
    /// </summary>
    public sealed class SetupScreen : VisualElement
    {
        public const string UssClass = "vv-setup";

        /// <param name="captures">The capture sources; null says capture is unavailable.</param>
        /// <param name="tracking">Tracking; null says it is unavailable.</param>
        public SetupScreen(ICaptureSources captures, VisualElement appSettings, ITrackingSource tracking = null)
        {
            AddToClassList(UssClass);
            if (captures == null)
            {
                Add(Ui.Text(CaptureStrings.Unavailable, PlaceholderScreen.NoteClass, "vv-text-body"));
            }
            else
            {
                foreach (IVideoCaptureSource source in captures.Sources)
                {
                    // The tracking camera is set up in the tracking Card, which uses it.
                    if (source is ICaptureDeviceSource device && source.Kind != CaptureSourceKind.Camera)
                    {
                        Add(new CaptureSourceCard(device, TitleKey(source), EmptyChoiceKey(source)));
                    }
                }
            }

            if (tracking != null && captures?.Find(CaptureService.TrackingCameraId) is ICaptureDeviceSource camera)
            {
                Add(new TrackingCard(tracking, camera));
            }
            else
            {
                Add(Ui.Text(TrackingStrings.Unavailable, PlaceholderScreen.NoteClass, "vv-text-body"));
            }

            if (appSettings != null)
            {
                Add(appSettings);
            }
        }

        /// <summary>The Card title of a source. Pure.</summary>
        public static string TitleKey(IVideoCaptureSource source)
        {
            return source.Kind == CaptureSourceKind.SubScreenCapture ? CaptureStrings.SubScreenTitle : CaptureStrings.GameTitle;
        }

        /// <summary>The device choice that means "none chosen": a capture board must be chosen, a monitor may be automatic. Pure.</summary>
        public static string EmptyChoiceKey(IVideoCaptureSource source)
        {
            return source.Kind == CaptureSourceKind.SubScreenCapture ? CaptureStrings.AutomaticMonitor : CaptureStrings.ChooseCamera;
        }
    }
}
