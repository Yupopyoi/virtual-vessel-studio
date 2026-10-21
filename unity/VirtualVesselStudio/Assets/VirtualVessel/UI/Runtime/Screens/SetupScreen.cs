using UnityEngine.UIElements;
using VirtualVessel.Avatar;
using VirtualVessel.Capture;
using VirtualVessel.Tracking;
using VirtualVessel.UI.Credits;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Lighting;
using VirtualVessel.UI.Preview;

namespace VirtualVessel.UI.Screens
{
    /// <summary>
    /// The settings screen (id "setup"): a capture Card per source shown on the stream, the tracking
    /// Card, the avatar Card, face tuning, the app settings, then the credits
    /// (docs/&lt;lang&gt;/ui/screens/setup.md), each closed until opened.
    /// </summary>
    public sealed class SetupScreen : VisualElement
    {
        public const string UssClass = "vv-setup";

        /// <param name="captures">The capture sources; null says capture is unavailable.</param>
        /// <param name="tracking">Tracking; null says it is unavailable.</param>
        /// <param name="avatars">Avatars; null says they are unavailable.</param>
        /// <param name="files">The file-picking screen for registering avatars; null disables registering.</param>
        /// <param name="credits">The shipped license files; null leaves the credits out.</param>
        /// <param name="stream">The stream video, to watch the avatar while tuning the face; null shows no picture.</param>
        /// <param name="lighting">The stage's light, changed beside the face picture; null leaves it out.</param>
        public SetupScreen(ICaptureSources captures, VisualElement appSettings, ITrackingSource tracking = null, IAvatarService avatars = null, IFileDialog files = null, ICreditFiles credits = null, IStreamPreviewSource stream = null, IStageLighting lighting = null)
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

            if (avatars != null)
            {
                Add(new AvatarCard(avatars, files));
                Add(new FaceTuningCard(avatars, stream, lighting));
            }
            else
            {
                Add(Ui.Text(AvatarStrings.Unavailable, PlaceholderScreen.NoteClass, "vv-text-body"));
            }

            if (appSettings != null)
            {
                Add(appSettings);
            }

            if (credits != null)
            {
                Add(new CreditsCard(credits));
            }

            CloseCards(this);
        }

        /// <summary>
        /// Makes every Card under <paramref name="screen"/> collapsible and closes it, so the screen
        /// starts as a short list of titles and states (setup.md 2). Not saved: each start is closed.
        /// </summary>
        private static void CloseCards(VisualElement screen)
        {
            screen.Query<Card>().ForEach(card =>
            {
                card.Collapsible = true;
                card.Expanded = false;
            });
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
