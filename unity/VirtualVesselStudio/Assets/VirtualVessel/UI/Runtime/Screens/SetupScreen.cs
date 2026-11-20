using System;
using UnityEngine.UIElements;
using VirtualVessel.Audio.Clips;
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
            AddToClassList(UssClass);

            Tabs = Ui.Tabs(SetupPageStrings.Video, SetupPageStrings.Audio, SetupPageStrings.Avatar, SetupPageStrings.Output, SetupPageStrings.App);
            Add(Tabs);
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i] = new VisualElement();
                _pages[i].AddToClassList(PageClass);
                Add(_pages[i]);
            }

            FillVideo(_pages[VideoPage], services);
            FillAudio(_pages[AudioPage], services, _pages[VideoPage].Q<GameAudioRow>() != null);
            FillAvatar(_pages[AvatarPage], services);
            FillOutput(_pages[OutputPage], services);
            FillApp(_pages[AppPage], services, appSettings);

            Tabs.SetValueWithoutNotify(PageOf(pageId));
            ShowPage(Tabs.value);
            Tabs.RegisterValueChangedCallback(evt =>
            {
                ShowPage(evt.newValue);
                pageChanged?.Invoke(s_pageIds[evt.newValue]);
            });
        }

        // A Card for a service, or a note that the service is not there.
        private static void AddOr(VisualElement page, bool available, Func<VisualElement> card, string unavailableKey)
        {
            page.Add(available ? card() : Ui.Text(unavailableKey, PlaceholderScreen.NoteClass, "vv-text-body"));
        }

        // The capture sources; the tracking camera is set up in the tracking Card, which uses it.
        private static void FillVideo(VisualElement page, ScreenServices services)
        {
            ICaptureSources captures = services.Captures;
            if (captures == null)
            {
                page.Add(Ui.Text(CaptureStrings.Unavailable, PlaceholderScreen.NoteClass, "vv-text-body"));
                return;
            }

            foreach (IVideoCaptureSource source in captures.Sources)
            {
                if (source is ICaptureDeviceSource device && source.Kind != CaptureSourceKind.Camera)
                {
                    // Audio that did not start (its plugin missing or another version) has no sources: the Card goes without the row.
                    GameAudioRow gameAudio = source.Kind == CaptureSourceKind.GameCapture && GameAudioRow.CanShow(services.Audio) ? new GameAudioRow(services.Audio) : null;
                    page.Add(new CaptureSourceCard(device, TitleKey(source), EmptyChoiceKey(source), gameAudio, source.Kind == CaptureSourceKind.GameCapture));
                }
            }
        }

        // The sources' mixer, then BGM and SE, then the voice conversion, since it converts the microphone (setup screen
        // 11, 14, 13).
        private static void FillAudio(VisualElement page, ScreenServices services, bool gameAudioOnVideo)
        {
            AddOr(page, services.Audio != null, () => new AudioCard(services.Audio, services.SystemSettings, gameAudioOnVideo), AudioStrings.Unavailable);
            if (services.Audio != null && services.AudioClips != null)
            {
                page.Add(new AudioClipCard(AudioClipKind.Bgm, services.AudioClips, services.Bgm, services.SoundEffects, services.FileDialog));
                page.Add(new AudioClipCard(AudioClipKind.SoundEffect, services.AudioClips, services.Bgm, services.SoundEffects, services.FileDialog, services.Shortcuts));
            }

            if (services.Audio != null && services.Voice != null)
            {
                page.Add(new VoiceCard(services.Voice));
            }
        }

        private static void FillAvatar(VisualElement page, ScreenServices services)
        {
            IAvatarService avatars = services.Avatars;
            AddOr(page, avatars != null, () => new AvatarCard(avatars, services.FileDialog), AvatarStrings.Unavailable);
            ICaptureDeviceSource camera = services.Captures?.Find(CaptureService.TrackingCameraId) as ICaptureDeviceSource;
            AddOr(page, services.Tracking != null && camera != null, () => new TrackingCard(services.Tracking, camera), TrackingStrings.Unavailable);
            if (avatars != null)
            {
                page.Add(new FaceTuningCard(avatars, services.StreamPreview, services.Lighting, services.AvatarFrame));
            }
        }

        private static void FillOutput(VisualElement page, ScreenServices services)
        {
            AddOr(page, services.Recording != null, () => new RecordingCard(services.Recording, services.FileDialog, services.Explorer), RecordingCardStrings.Unavailable);
            AddOr(page, services.Streaming != null, () => new StreamingCard(services.Streaming), StreamingCardStrings.Unavailable);
        }

        private static void FillApp(VisualElement page, ScreenServices services, VisualElement appSettings)
        {
            if (appSettings != null)
            {
                page.Add(appSettings);
            }

            if (services.Credits != null)
            {
                foreach (CreditsCard credits in CreditsCard.Build(services.Credits))
                {
                    page.Add(credits);
                }
            }
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
