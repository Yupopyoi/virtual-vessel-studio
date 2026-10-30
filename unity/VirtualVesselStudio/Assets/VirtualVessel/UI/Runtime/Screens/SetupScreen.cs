using System;
using UnityEngine.UIElements;
using VirtualVessel.Avatar;
using VirtualVessel.Capture;
using VirtualVessel.Tracking;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of the settings screen's pages (docs/&lt;lang&gt;/ui/screens/setup.md 2).</summary>
    [StringKeys]
    public static class SetupPageStrings
    {
        public const string Video = "setup.page.video";
        public const string Audio = "setup.page.audio";
        public const string Avatar = "setup.page.avatar";
        public const string Output = "setup.page.output";
        public const string App = "setup.page.app";
    }

    /// <summary>
    /// The settings screen (id "setup"): pages of Cards under tabs (docs/&lt;lang&gt;/ui/screens/setup.md 2),
    /// so that each subject is found in one place and the screen stays short as features are added.
    /// </summary>
    public sealed class SetupScreen : VisualElement
    {
        public const string UssClass = "vv-setup";
        public const string PageClass = UssClass + "__page";

        public const int VideoPage = 0;
        public const int AudioPage = 1;
        public const int AvatarPage = 2;
        public const int OutputPage = 3;
        public const int AppPage = 4;

        // Stored in lastSetupPage: stable, unlike the order of the tabs.
        private static readonly string[] s_pageIds = { "video", "audio", "avatar", "output", "app" };

        private readonly VisualElement[] _pages = new VisualElement[s_pageIds.Length];

        /// <param name="services">What the Cards show; a service that is null is said to be unavailable or left out.</param>
        /// <param name="appSettings">The app settings Card, which the UI root builds from the UI's own settings.</param>
        /// <param name="pageId">The page to open with: the one saved last time, or null for the first.</param>
        /// <param name="pageChanged">Called with the id of a page the user opens.</param>
        public SetupScreen(ScreenServices services, VisualElement appSettings, string pageId = null, Action<string> pageChanged = null)
        {
            services ??= new ScreenServices();
            ICaptureSources captures = services.Captures;
            ITrackingSource tracking = services.Tracking;
            IAvatarService avatars = services.Avatars;
            AddToClassList(UssClass);

            Tabs = Ui.Tabs(SetupPageStrings.Video, SetupPageStrings.Audio, SetupPageStrings.Avatar, SetupPageStrings.Output, SetupPageStrings.App);
            Add(Tabs);
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i] = new VisualElement();
                _pages[i].AddToClassList(PageClass);
                Add(_pages[i]);
            }

            VisualElement video = _pages[VideoPage];
            VisualElement audio = _pages[AudioPage];
            VisualElement avatar = _pages[AvatarPage];
            VisualElement output = _pages[OutputPage];
            VisualElement app = _pages[AppPage];
            if (captures == null)
            {
                video.Add(Ui.Text(CaptureStrings.Unavailable, PlaceholderScreen.NoteClass, "vv-text-body"));
            }
            else
            {
                foreach (IVideoCaptureSource source in captures.Sources)
                {
                    // The tracking camera is set up in the tracking Card, which uses it.
                    if (source is ICaptureDeviceSource device && source.Kind != CaptureSourceKind.Camera)
                    {
                        GameAudioRow gameAudio = source.Kind == CaptureSourceKind.GameCapture && services.Audio != null ? new GameAudioRow(services.Audio) : null;
                        video.Add(new CaptureSourceCard(device, TitleKey(source), EmptyChoiceKey(source), gameAudio));
                    }
                }
            }

            if (services.Audio != null)
            {
                audio.Add(new AudioCard(services.Audio, services.SystemSettings, video.Q<GameAudioRow>() != null));
            }
            else
            {
                audio.Add(Ui.Text(AudioStrings.Unavailable, PlaceholderScreen.NoteClass, "vv-text-body"));
            }

            if (avatars != null)
            {
                avatar.Add(new AvatarCard(avatars, services.FileDialog));
            }
            else
            {
                avatar.Add(Ui.Text(AvatarStrings.Unavailable, PlaceholderScreen.NoteClass, "vv-text-body"));
            }

            if (tracking != null && captures?.Find(CaptureService.TrackingCameraId) is ICaptureDeviceSource camera)
            {
                avatar.Add(new TrackingCard(tracking, camera));
            }
            else
            {
                avatar.Add(Ui.Text(TrackingStrings.Unavailable, PlaceholderScreen.NoteClass, "vv-text-body"));
            }

            if (avatars != null)
            {
                avatar.Add(new FaceTuningCard(avatars, services.StreamPreview, services.Lighting));
            }

            if (services.Recording != null)
            {
                output.Add(new RecordingCard(services.Recording, services.FileDialog, services.Explorer));
            }
            else
            {
                output.Add(Ui.Text(RecordingCardStrings.Unavailable, PlaceholderScreen.NoteClass, "vv-text-body"));
            }

            if (services.Streaming != null)
            {
                output.Add(new StreamingCard(services.Streaming));
            }
            else
            {
                output.Add(Ui.Text(StreamingCardStrings.Unavailable, PlaceholderScreen.NoteClass, "vv-text-body"));
            }

            if (appSettings != null)
            {
                app.Add(appSettings);
            }

            if (services.Credits != null)
            {
                app.Add(new CreditsCard(services.Credits));
            }

            Tabs.SetValueWithoutNotify(PageOf(pageId));
            ShowPage(Tabs.value);
            Tabs.RegisterValueChangedCallback(evt =>
            {
                ShowPage(evt.newValue);
                pageChanged?.Invoke(s_pageIds[evt.newValue]);
            });
        }

        /// <summary>The tabs; the value is the open page.</summary>
        public TabBar Tabs { get; }

        /// <summary>The page with this id, or the first for an id not known (one saved by another version). Pure.</summary>
        public static int PageOf(string pageId)
        {
            return Math.Max(0, Array.IndexOf(s_pageIds, pageId));
        }

        // Pages stay built and only hide, as screens do (UI foundation detailed design 7.7): a Card keeps
        // its state, and what it follows, while another page is open.
        private void ShowPage(int page)
        {
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i].style.display = i == page ? DisplayStyle.Flex : DisplayStyle.None;
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
