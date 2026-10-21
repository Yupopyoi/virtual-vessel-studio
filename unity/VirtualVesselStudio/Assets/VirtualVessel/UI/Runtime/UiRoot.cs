using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using VirtualVessel.Core.Threading;
using VirtualVessel.Core.Time;
using VirtualVessel.Diagnostics.Logging;
using VirtualVessel.Diagnostics.Performance;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Dialogs;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Notifications;
using VirtualVessel.UI.Screens;
using VirtualVessel.UI.Settings;
using VirtualVessel.UI.Theme;
using VirtualVessel.UI.Tooltips;

namespace VirtualVessel.UI
{
    /// <summary>What the UI root needs from the application to build the UI.</summary>
    public sealed class UiRootContext
    {
        /// <param name="configDirectory">Folder of ui-settings.json, normally %LOCALAPPDATA%\VirtualVesselStudio\Config.</param>
        /// <param name="log">Optional; the UI's developer log.</param>
        /// <param name="systemPrefersReducedMotion">The OS setting, read once by the caller (<see cref="SystemMotionPreference"/>).</param>
        /// <param name="applicationVersion">Shown under the navigation; optional.</param>
        /// <param name="metrics">Shown on the Diagnostics screen; null when the service is not available.</param>
        /// <param name="developerModeReceivers">Told when Developer Mode changes, such as detailed metrics.</param>
        /// <param name="screens">The services the live and settings screens show; null shows none of them.</param>
        public UiRootContext(
            string configDirectory,
            IMonotonicClock monotonicClock,
            ISystemClock systemClock,
            IMainThreadDispatcher dispatcher,
            ILog log,
            bool systemPrefersReducedMotion,
            string applicationVersion = null,
            IPerformanceMetrics metrics = null,
            IReadOnlyList<Action<bool>> developerModeReceivers = null,
            ScreenServices screens = null)
        {
            if (string.IsNullOrWhiteSpace(configDirectory))
            {
                throw new ArgumentException("The settings folder must not be empty.", nameof(configDirectory));
            }

            ConfigDirectory = configDirectory;
            MonotonicClock = monotonicClock ?? throw new ArgumentNullException(nameof(monotonicClock));
            SystemClock = systemClock ?? throw new ArgumentNullException(nameof(systemClock));
            Dispatcher = dispatcher;
            Log = log;
            SystemPrefersReducedMotion = systemPrefersReducedMotion;
            ApplicationVersion = applicationVersion;
            Metrics = metrics;
            DeveloperModeReceivers = developerModeReceivers ?? Array.Empty<Action<bool>>();
            Screens = screens ?? new ScreenServices();
        }

        public string ConfigDirectory { get; }

        public IMonotonicClock MonotonicClock { get; }

        public ISystemClock SystemClock { get; }

        public IMainThreadDispatcher Dispatcher { get; }

        public ILog Log { get; }

        public bool SystemPrefersReducedMotion { get; }

        public string ApplicationVersion { get; }

        public IPerformanceMetrics Metrics { get; }

        public IReadOnlyList<Action<bool>> DeveloperModeReceivers { get; }

        /// <summary>The services the live and settings screens show; never null.</summary>
        public ScreenServices Screens { get; }
    }

    /// <summary>
    /// The entry point of the UI, placed in the persistent scene next to the <see cref="UIDocument"/>
    /// (UI foundation detailed design 7.9).
    /// </summary>
    /// <remarks>
    /// The application composition calls <see cref="Build"/> through <c>UiApplicationService</c>; the
    /// bootstrap calls <see cref="ShowStartupProblem"/> when the application cannot start. There is no
    /// static instance: screens receive what they need from here or from <see cref="UiServices"/>.
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class UiRoot : MonoBehaviour
    {
        public const string ContentClass = "vv-content";

        // Windows lays out at 96 DPI for 100% display scale; above that the UI grows with the OS setting.
        private const float ReferenceDpi = 96f;

        // One fallback font asset per font for the whole run, never destroyed: TextCore keeps glyph
        // caches that point at a font asset's atlas after its panel is gone, so destroying it breaks
        // the next panel (for example the startup problem screen after a failed build). Unity caches
        // the font assets it makes for TTF fonts the same way.
        private static readonly Dictionary<Font, FontAsset> s_fallbackFonts = new Dictionary<Font, FontAsset>();

        [SerializeField]
        private UiAssets _assets;

        private UIDocument _document;
        private PanelSettings _panel;
        private PanelTextSettings _textSettings;
        private ILog _log;
        private Localizer _localizer;
        private ThemeService _theme;
        private FontService _fonts;
        private TooltipService _tooltips;
        private UiServicesScope _scope;
        private DeveloperModeService _developerMode;

        public UiAssets Assets => _assets;

        /// <summary>True between a successful <see cref="Build"/> and <see cref="Teardown"/>.</summary>
        public bool IsBuilt => Settings != null;

        public UiSettingsService Settings { get; private set; }

        public ILocalizer Localizer => _localizer;

        public NotificationService Notifications { get; private set; }

        public DialogService Dialogs { get; private set; }

        /// <summary>The area under the notifications and dialogs; holds <see cref="Shell"/>.</summary>
        public VisualElement Content { get; private set; }

        /// <summary>The navigation and the screens.</summary>
        public AppShell Shell { get; private set; }

        public DeveloperModeService DeveloperMode => _developerMode;

        /// <summary>The screen shown by <see cref="ShowStartupProblem"/>, otherwise null.</summary>
        public StartupProblemView StartupProblem { get; private set; }

        /// <summary>The PanelSettings copy in use; null before building.</summary>
        internal PanelSettings Panel => _panel;

        internal ThemeService ThemeService => _theme;

        internal FontService FontService => _fonts;

        /// <summary>Builds the UI with the saved settings. On failure, everything built so far is torn down and the exception is rethrown.</summary>
        public void Build(UiRootContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            RequireNotShown();
            _log = context.Log;
            try
            {
                IReadOnlyList<StringTable> tables = _assets.LoadStringTables();
                List<string> languages = tables.Select(table => table.Locale.Code).ToList();
                string initialLanguage = Localization.Localizer.ChooseInitialLocale(UnityEngine.Application.systemLanguage, languages);
                var choices = new UiSettingsChoices(
                    _assets.Theme.PaletteIds,
                    _assets.Theme.FontSets.Where(fontSet => fontSet != null).Select(fontSet => fontSet.Id),
                    languages,
                    initialLanguage,
                    context.SystemPrefersReducedMotion);
                var store = new ConfigFileStore<UiSettings>(
                    context.ConfigDirectory,
                    UiSettings.FileName,
                    UiSettings.SchemaVersion,
                    () => UiSettings.CreateDefault(initialLanguage),
                    context.SystemClock);
                var settings = new UiSettingsService(store, choices, context.MonotonicClock, _log);
                ConfigLoadResult<UiSettings> loaded = settings.Load();
                ResolvedUiSettings current = settings.Current;

                _localizer = new Localizer(tables, current.Language, _log);
                VisualElement root = CreatePanel(current.UiScale);
                _theme = new ThemeService(_assets.Theme, _log);
                _fonts = new FontService(_assets.Theme, _log);
                Notifications = new NotificationService();
                Dialogs = new DialogService();
                _tooltips = new TooltipService();
                _scope = new UiServicesScope(new UiServices(_localizer, context.Dispatcher, _log, Notifications.ShowFailure, Dialogs));
                root.Add(_scope);
                _theme.Attach(_scope);
                _fonts.Attach(_scope);
                _theme.Apply(current.Theme);
                _fonts.Apply(current.FontSet);

                Content = new VisualElement();
                Content.AddToClassList(ContentClass);
                Content.style.flexGrow = 1;
                _scope.Add(Content);
                _developerMode = new DeveloperModeService(settings, context.DeveloperModeReceivers, _log);
                Shell = new AppShell(CreateScreens(settings, context), context.ApplicationVersion, _log);
                Content.Add(Shell);

                // Added after the content so that they draw above every screen.
                Notifications.Attach(_scope);
                Dialogs.Attach(_scope);
                _tooltips.Attach(_scope);

                Settings = settings;
                Settings.Changed += Apply;
                Settings.SaveFailed += OnSaveFailed;

                // Developer Mode first, so a saved Diagnostics screen can open, then the last screen.
                _developerMode.Start();
                _developerMode.Changed += OnDeveloperModeChanged;
                Shell.DeveloperMode = _developerMode.IsEnabled;
                Shell.Open(current.LastScreen);
                Shell.Opened += Settings.SetLastScreen;
                if (loaded.Status == ConfigLoadStatus.Recovered)
                {
                    Notifications.Show(NoticeKind.Warning, UiRootStrings.SettingsRecoveredTitle, UiRootStrings.SettingsRecoveredBody);
                }

                _log?.Information($"UI built: language {current.Language}, palette {current.Theme.Palette}, background {current.Theme.Background}, font set {current.FontSet}, UI scale {current.UiScale}, reduced motion {current.Theme.ReducedMotion}.");
            }
            catch
            {
                Teardown();
                throw;
            }
        }

        /// <summary>
        /// Replaces anything shown with the startup problem screen. Uses the default theme and font set,
        /// so it depends as little as possible on what failed. The language is the one shown until now,
        /// else the saved one (read only, never moved aside or written), else the OS language.
        /// </summary>
        /// <param name="dataRootPath">Shown for <see cref="StartupProblemKind.DataRootUnavailable"/>.</param>
        /// <param name="configDirectory">Folder of ui-settings.json, for the saved language; may be null.</param>
        /// <param name="quit">Runs the normal shutdown.</param>
        public void ShowStartupProblem(StartupProblemKind kind, string dataRootPath, string configDirectory, Action quit)
        {
            string shownLanguage = Settings?.Current.Language;
            Teardown();
            RequireNotShown();
            try
            {
                IReadOnlyList<StringTable> tables = _assets.LoadStringTables();
                List<string> languages = tables.Select(table => table.Locale.Code).ToList();
                string language = UiSettingsResolver.ChooseProblemLanguage(
                    shownLanguage,
                    shownLanguage == null ? UiSettingsPeek.ReadLanguage(configDirectory) : null,
                    Localization.Localizer.ChooseInitialLocale(UnityEngine.Application.systemLanguage, languages),
                    languages);
                _localizer = new Localizer(tables, language, _log);
                VisualElement root = CreatePanel(1f);
                _theme = new ThemeService(_assets.Theme, _log);
                _fonts = new FontService(_assets.Theme, _log);
                _scope = new UiServicesScope(new UiServices(_localizer, null, _log));
                root.Add(_scope);
                _theme.Attach(_scope);
                _fonts.Attach(_scope);
                StartupProblem = new StartupProblemView(kind, dataRootPath, quit);
                _scope.Add(StartupProblem);
            }
            catch
            {
                Teardown();
                throw;
            }
        }

        /// <summary>Saves a waiting settings change and waits for it. Safe to await from shutdown.</summary>
        public Task FlushSettingsAsync()
        {
            return Settings?.FlushAsync() ?? Task.CompletedTask;
        }

        /// <summary>Removes the UI and destroys the run-time copies. Does not save; call <see cref="FlushSettingsAsync"/> first.</summary>
        public void Teardown()
        {
            if (_developerMode != null)
            {
                _developerMode.Changed -= OnDeveloperModeChanged;
                _developerMode.Dispose();
                _developerMode = null;
            }

            Shell?.Host.HideCurrent();
            Shell = null;
            if (Settings != null)
            {
                Settings.Changed -= Apply;
                Settings.SaveFailed -= OnSaveFailed;
                Settings = null;
            }

            _tooltips?.Dispose();
            _tooltips = null;
            _scope?.RemoveFromHierarchy();
            _scope = null;
            Content = null;
            StartupProblem = null;
            Notifications = null;
            Dialogs = null;
            _theme = null;
            _fonts = null;
            _localizer = null;

            if (_document != null && _document.panelSettings == _panel)
            {
                _document.panelSettings = null;
            }

            DestroyCopy(ref _panel);
            DestroyCopy(ref _textSettings);
        }

        /// <summary>For tests, which create the root without a scene.</summary>
        internal void SetAssets(UiAssets assets)
        {
            _assets = assets;
        }

        private void Update()
        {
            Settings?.Tick();
        }

        private void OnDestroy()
        {
            Teardown();
        }

        private void RequireNotShown()
        {
            if (_scope != null)
            {
                throw new InvalidOperationException("The UI is already shown; tear it down first.");
            }

            if (_assets == null)
            {
                throw new InvalidOperationException($"{nameof(UiRoot)} on '{name}' has no {nameof(UiAssets)}.");
            }
        }

        private VisualElement CreatePanel(float uiScale)
        {
            _document = GetComponent<UIDocument>();

            // A copy: changing the asset itself at run time would leave the change in the asset in the Editor.
            _panel = Instantiate(_assets.PanelSettings);
            _panel.name = _assets.PanelSettings.name + " (runtime)";
            _panel.scaleMode = PanelScaleMode.ConstantPhysicalSize;
            _panel.referenceDpi = ReferenceDpi;
            _panel.fallbackDpi = ReferenceDpi;
            _panel.scale = uiScale;
            AddFontFallback();
            _document.panelSettings = _panel;

            VisualElement root = _document.rootVisualElement;
            root.style.flexGrow = 1;
            return root;
        }

        /// <summary>
        /// Falls back to the default font set's Regular font (Noto Sans JP) for glyphs a font lacks, such
        /// as Japanese in the monospace font (system design 14.9).
        /// </summary>
        private void AddFontFallback()
        {
            Font font = _assets.FindFontSet(FontService.DefaultFontSet)?.Regular;
            if (font == null)
            {
                _log?.Warning($"The default font set '{FontService.DefaultFontSet}' has no Regular font; no glyph fallback is set.");
                return;
            }

            FontAsset fallback = GetFallbackFont(font);
            if (fallback == null)
            {
                _log?.Warning($"Could not create a fallback font asset from '{font.name}'; no glyph fallback is set.");
                return;
            }

            _textSettings = _panel.textSettings != null
                ? Instantiate(_panel.textSettings)
                : ScriptableObject.CreateInstance<PanelTextSettings>();
            var fallbacks = new List<FontAsset>(_textSettings.fallbackFontAssets ?? new List<FontAsset>());
            fallbacks.Add(fallback);
            _textSettings.fallbackFontAssets = fallbacks;
            _panel.textSettings = _textSettings;
        }

        private void Apply(ResolvedUiSettings settings)
        {
            _localizer.SetLocale(settings.Language);
            _theme.Apply(settings.Theme);
            _fonts.Apply(settings.FontSet);
            if (!Mathf.Approximately(_panel.scale, settings.UiScale))
            {
                _panel.scale = settings.UiScale;
                _log?.Information($"UI scale changed: {settings.UiScale}.");
            }
        }

        /// <summary>The screens of the navigation, in order (UI foundation detailed design 7.7).</summary>
        private IReadOnlyList<ScreenDefinition> CreateScreens(UiSettingsService settings, UiRootContext context)
        {
            IPerformanceMetrics metrics = context.Metrics;
            var screens = new List<ScreenDefinition>
            {
                new ScreenDefinition("live", IconName.Live, ScreenStrings.Live, ScreenStrings.LiveTip, () => new LiveScreen(context.Screens.StreamPreview, context.Screens.FrameSources)),
                new ScreenDefinition("setup", IconName.Setup, ScreenStrings.Setup, ScreenStrings.SetupTip, () => new SetupScreen(context.Screens, new AppSettingsCard(settings, _localizer.Locales, _assets.Theme))),
                new ScreenDefinition("lab", IconName.Lab, ScreenStrings.Lab, ScreenStrings.LabTip, () => new PlaceholderScreen()),
                new ScreenDefinition("diagnostics", IconName.Diagnostics, ScreenStrings.Diagnostics, ScreenStrings.DiagnosticsTip, () => new DiagnosticsScreen(metrics), developerOnly: true),
            };
#if UNITY_EDITOR
            // A tool for building the UI, not part of the application: only in the Unity Editor.
            screens.Add(new ScreenDefinition("gallery", IconName.Gallery, GalleryStrings.Nav, GalleryStrings.NavTip, () => new ComponentGalleryScreen(Notifications), developerOnly: true));
#endif
            return screens;
        }

        private void OnDeveloperModeChanged(bool enabled)
        {
            Shell.DeveloperMode = enabled;
        }

        private void OnSaveFailed()
        {
            Notifications?.Show(NoticeKind.Warning, UiRootStrings.SaveFailedTitle, UiRootStrings.SaveFailedBody);
        }

        private static FontAsset GetFallbackFont(Font font)
        {
            // A destroyed entry (null by Unity's rules) happens only across Play Mode sessions in the Editor.
            if (s_fallbackFonts.TryGetValue(font, out FontAsset cached) && cached != null)
            {
                return cached;
            }

            FontAsset created = FontAsset.CreateFontAsset(font);
            if (created != null)
            {
                created.name = font.name + " (fallback)";
                s_fallbackFonts[font] = created;
            }

            return created;
        }

        private static void DestroyCopy<T>(ref T copy)
            where T : UnityEngine.Object
        {
            if (copy == null)
            {
                return;
            }

            if (UnityEngine.Application.isPlaying)
            {
                Destroy(copy);
            }
            else
            {
                DestroyImmediate(copy);
            }

            copy = null;
        }
    }
}
