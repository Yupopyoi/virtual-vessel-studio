using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VirtualVessel.Core.Geometry;
using VirtualVessel.Core.State;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Frames;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Preview;

namespace VirtualVessel.UI.Tests
{
    /// <summary>The stream preview on a real runtime panel.</summary>
    public sealed class StreamPreviewPanelTests
    {
        private GameObject _host;
        private PanelSettings _panelSettings;
        private UiServicesScope _scope;
        private StreamPreview _preview;
        private FakePreviewSource _source;
        private Texture2D _texture;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var localizer = new Localizer(
                new[] { new StringTable(new LocaleInfo("ja", "ja", null), new Dictionary<string, string> { { "preview.no-video", "配信映像はまだありません" } }) },
                "ja",
                null);
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _host = new GameObject(nameof(StreamPreviewPanelTests));
            UIDocument document = _host.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            document.rootVisualElement.style.flexGrow = 1;
            _scope = new UiServicesScope(new UiServices(localizer, null, null));
            _scope.AddToClassList("vv-root");
            _scope.style.flexGrow = 1;
            document.rootVisualElement.Add(_scope);

            var box = new VisualElement();
            box.style.position = Position.Absolute;
            box.style.left = 20;
            box.style.top = 20;
            box.style.width = 400;
            box.style.height = 300;
            _scope.Add(box);
            _preview = new StreamPreview();
            _preview.style.flexGrow = 1;
            box.Add(_preview);
            _source = new FakePreviewSource();
            _preview.Bind(_source);
            _texture = new Texture2D(16, 9);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_host);
            Object.Destroy(_panelSettings);
            Object.Destroy(_texture);
        }

        [UnityTest]
        public IEnumerator Preview_KeepsSixteenByNine_AndShowsTheVideoAndNumbers()
        {
            VisualElement screen = _preview.Q(className: StreamPreview.ScreenClass);
            Assert.That(screen.ClassListContains(StreamPreview.EmptyClass), Is.True, "No video yet.");
            Assert.That(_preview.Q<Label>(className: StreamPreview.NoVideoClass).text, Is.EqualTo("配信映像はまだありません"));

            _source.VideoValue.Value = _texture;
            _source.StatsValue.Value = new PreviewStats(59.94, 60, 1920, 1080, 6000);
            yield return null;
            yield return null;

            Assert.That(screen.ClassListContains(StreamPreview.EmptyClass), Is.False);
            Assert.That(_preview.Video, Is.SameAs(_texture));
            Assert.That(_preview.FrameRateText, Is.EqualTo("59.9 fps"));
            Assert.That(_preview.FrameRateStatus, Is.EqualTo(StatusKind.Success));
            Assert.That(_preview.FormatText, Is.EqualTo("1920×1080 · 6,000 kbps"));
            _source.StatsValue.Value = new PreviewStats(41, 60, 1920, 1080, 6000);
            Assert.That(_preview.FrameRateStatus, Is.EqualTo(StatusKind.Warning), "A drop shows as a warning.");
            Assert.That(screen.layout.width, Is.EqualTo(400).Within(0.5f));
            Assert.That(screen.layout.height, Is.EqualTo(225).Within(0.5f), "16:9 in a 400 × 300 room.");
        }

        [UnityTest]
        public IEnumerator Fullscreen_ByButtonFAndEsc_AndBack()
        {
            VisualElement screen = _preview.Q(className: StreamPreview.ScreenClass);
            var button = screen.Q<IconButton>(className: StreamPreview.FullscreenButtonClass);

            Submit(button);
            yield return null;
            Assert.That(_preview.IsFullscreen, Is.True);
            Assert.That(screen.parent.ClassListContains(StreamPreview.FullscreenLayerClass), Is.True, "The screen moves to a layer over the whole UI.");
            Assert.That(screen.parent.parent, Is.SameAs(_scope));
            Submit(button);
            Assert.That(_preview.IsFullscreen, Is.False, "The same button returns.");
            Assert.That(screen.parent, Is.SameAs(_preview));

            Key(screen, KeyCode.F);
            Assert.That(_preview.IsFullscreen, Is.True, "F enters...");
            Key(screen, KeyCode.F);
            Assert.That(_preview.IsFullscreen, Is.False, "...and F returns.");
            Key(screen, KeyCode.F);
            Key(screen, KeyCode.Escape);
            Assert.That(_preview.IsFullscreen, Is.False, "Esc returns too.");
            Assert.That(_scope.Q(className: StreamPreview.FullscreenLayerClass), Is.Null);
        }

        [UnityTest]
        public IEnumerator Esc_FirstClearsTheSelection_ThenLeavesFullscreen()
        {
            var canvas = new FrameCanvas();
            _preview.Overlay.Add(canvas);
            var game = new FakeFrameSource();
            canvas.SetSources(new[] { game });
            canvas.Select(game);
            _preview.SetFullscreen(true);
            yield return null;

            Key(canvas, KeyCode.Escape);
            Assert.That(canvas.Selected, Is.Null, "The first Esc clears the selection...");
            Assert.That(_preview.IsFullscreen, Is.True);
            Key(canvas, KeyCode.Escape);
            Assert.That(_preview.IsFullscreen, Is.False, "...the next one leaves full screen.");
        }

        private static void Submit(VisualElement element)
        {
            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = element;
                element.SendEvent(evt);
            }
        }

        private static void Key(VisualElement target, KeyCode key)
        {
            using (KeyDownEvent evt = KeyDownEvent.GetPooled('\0', key, EventModifiers.None))
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        private sealed class FakePreviewSource : IStreamPreviewSource
        {
            public ObservableValue<Texture> VideoValue { get; } = new ObservableValue<Texture>(null);

            public ObservableValue<PreviewStats> StatsValue { get; } = new ObservableValue<PreviewStats>(default);

            public IReadOnlyObservable<Texture> Video => VideoValue;

            public IReadOnlyObservable<PreviewStats> Stats => StatsValue;
        }

        private sealed class FakeFrameSource : IFrameSource
        {
            public string Id => "game";

            public string NameKey => null;

            public IReadOnlyObservable<string> DisplayName { get; } = new ObservableValue<string>("game");

            public IReadOnlyObservable<bool> Visible { get; } = new ObservableValue<bool>(true);

            public double SourceAspect => 16.0 / 9.0;

            public Texture Video => null;

            public IReadOnlyObservable<FrameLayout> Layout { get; } = new ObservableValue<FrameLayout>(FrameLayout.Create(new NormalizedRect(0.1, 0.1, 0.4, 0.4)));

            public void PreviewLayout(FrameLayout layout)
            {
            }

            public Task CommitLayoutAsync(FrameLayout layout, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }
    }
}
