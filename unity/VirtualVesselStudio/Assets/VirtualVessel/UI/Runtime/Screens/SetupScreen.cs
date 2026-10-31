using UnityEngine.UIElements;
using VirtualVessel.Avatar;
using VirtualVessel.Capture;
using VirtualVessel.Tracking;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.Screens
{
    /// <summary>
    /// The settings screen (id "setup"): a capture Card per source shown on the stream, the tracking
    /// Card, the audio Card, the avatar Card, face tuning, recording, the app settings, then the credits
    /// (docs/&lt;lang&gt;/ui/screens/setup.md), each closed until opened.
    /// </summary>
    public sealed class SetupScreen : VisualElement
    {
        public const string UssClass = "vv-setup";

        /// <param name="services">What the Cards show; a service that is null is said to be unavailable or left out.</param>
        /// <param name="appSettings">The app settings Card, which the UI root builds from the UI's own settings.</param>
        public SetupScreen(ScreenServices services, VisualElement appSettings)
        {
            services ??= new ScreenServices();
            ICaptureSources captures = services.Captures;
            ITrackingSource tracking = services.Tracking;
            IAvatarService avatars = services.Avatars;
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
                        GameAudioRow audio = source.Kind == CaptureSourceKind.GameCapture && services.Audio != null ? new GameAudioRow(services.Audio) : null;
                        Add(new CaptureSourceCard(device, TitleKey(source), EmptyChoiceKey(source), audio));
                    }
                }
            }

            // The sound right after the video it often comes with (audio detailed design 18).
            if (services.Audio != null)
            {
                Add(new AudioCard(services.Audio, services.SystemSettings));
            }
            else
            {
                Add(Ui.Text(AudioStrings.Unavailable, PlaceholderScreen.NoteClass, "vv-text-body"));
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
                Add(new AvatarCard(avatars, services.FileDialog));
                Add(new FaceTuningCard(avatars, services.StreamPreview, services.Lighting));
            }
            else
            {
                Add(Ui.Text(AvatarStrings.Unavailable, PlaceholderScreen.NoteClass, "vv-text-body"));
            }

            if (services.Recording != null)
            {
                Add(new RecordingCard(services.Recording, services.FileDialog, services.Explorer));
            }
            else
            {
                Add(Ui.Text(RecordingCardStrings.Unavailable, PlaceholderScreen.NoteClass, "vv-text-body"));
            }

            if (appSettings != null)
            {
                Add(appSettings);
            }

            if (services.Credits != null)
            {
                Add(new CreditsCard(services.Credits));
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
