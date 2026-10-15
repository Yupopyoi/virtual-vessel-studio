using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Icons;

namespace VirtualVessel.UI.Preview
{
    /// <summary>
    /// The in-app preview of the stream (components.md 3.17, system design 10.6): the stream render target
    /// kept at 16:9, the frame rate at the top right, resolution and bitrate at the bottom left, and a
    /// full-screen button at the bottom right. Full screen only changes how the preview is shown; the
    /// stream output is not touched (system design 14.23).
    /// </summary>
    /// <remarks>
    /// Put a <see cref="Frames.FrameCanvas"/> in <see cref="Overlay"/> to edit placement on top of the
    /// video. F toggles full screen and Esc leaves it while focus is inside the preview.
    /// </remarks>
    [UxmlElement]
    public partial class StreamPreview : VisualElement
    {
        public const string UssClass = "vv-preview";

        public const string ScreenClass = UssClass + "__screen";

        public const string VideoClass = UssClass + "__video";

        public const string FrameRateClass = UssClass + "__frame-rate";

        public const string FormatClass = UssClass + "__format";

        public const string FullscreenButtonClass = UssClass + "__fullscreen";

        public const string HintClass = UssClass + "__hint";

        public const string NoVideoClass = UssClass + "__no-video";

        /// <summary>The layer over the themed root that holds the preview while in full screen.</summary>
        public const string FullscreenLayerClass = UssClass + "-fullscreen";

        /// <summary>On the screen while there is no video: the note shows and the numbers hide.</summary>
        public const string EmptyClass = ScreenClass + "--empty";

        /// <summary>On the screen in full screen: only the video, the button, and the hint show.</summary>
        public const string FullscreenClass = ScreenClass + "--fullscreen";

        public const float Aspect = 16f / 9f;

        /// <summary>How long the "Esc to return" hint stays after entering full screen, in milliseconds.</summary>
        private const long HintDuration = 2000;

        private readonly VisualElement _screen;
        private readonly Image _video;
        private readonly StatusChip _frameRate;
        private readonly Label _format;
        private readonly Label _noVideo;
        private readonly Label _hint;
        private readonly IconButton _fullscreenButton;
        private VisualElement _fullscreenLayer;

        public StreamPreview()
        {
            AddToClassList(UssClass);

            // The screen is centered and sized in code to keep 16:9 in whatever room it gets.
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
            _screen = new VisualElement();
            _screen.AddToClassList(ScreenClass);
            Add(_screen);

            // The video is clipped to the rounded screen; the overlay is not, so a placement canvas can
            // reach past the screen's edges.
            var clip = new VisualElement { pickingMode = PickingMode.Ignore };
            clip.AddToClassList(UssClass + "__clip");
            Fill(clip);
            clip.style.overflow = Overflow.Hidden;
            _video = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            _video.AddToClassList(VideoClass);
            Fill(_video);
            clip.Add(_video);
            _screen.Add(clip);

            _noVideo = QuietText.Of(new Label { pickingMode = PickingMode.Ignore });
            _noVideo.AddToClassList(NoVideoClass);
            _noVideo.AddToClassList("vv-text-caption");
            new KeyedText(_noVideo, text => _noVideo.text = text) { Key = PreviewStrings.NoVideo };
            _screen.Add(_noVideo);

            Overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            Fill(Overlay);
            _screen.Add(Overlay);

            // The frame rate is a status: falling below the target makes the stream choppy (styleboard).
            _frameRate = new StatusChip(StatusKind.Success, null) { TooltipKey = PreviewStrings.FrameRateTip };
            _frameRate.AddToClassList(FrameRateClass);
            _frameRate.style.position = Position.Absolute;
            _screen.Add(_frameRate);

            var format = new VisualElement { pickingMode = PickingMode.Ignore };
            format.AddToClassList(FormatClass);
            format.style.position = Position.Absolute;
            format.style.flexDirection = FlexDirection.Row;
            Label label = QuietText.Of(new Label { pickingMode = PickingMode.Ignore });
            label.AddToClassList(UssClass + "__format-label");
            new KeyedText(label, text => label.text = text) { Key = PreviewStrings.Label };
            format.Add(label);
            Label separator = QuietText.Of(new Label("·") { pickingMode = PickingMode.Ignore });
            separator.AddToClassList(UssClass + "__format-separator");
            format.Add(separator);
            _format = QuietText.Of(new Label { pickingMode = PickingMode.Ignore });
            _format.AddToClassList(UssClass + "__format-numbers");
            _format.AddToClassList("vv-mono");
            format.Add(_format);
            _screen.Add(format);

            _fullscreenButton = Ui.IconButton(IconName.Fullscreen).WithTooltip(PreviewStrings.Fullscreen, "F");
            _fullscreenButton.AddToClassList(FullscreenButtonClass);
            _fullscreenButton.style.position = Position.Absolute;
            _fullscreenButton.clicked += () => SetFullscreen(!IsFullscreen);
            _screen.Add(_fullscreenButton);

            _hint = QuietText.Of(new Label { pickingMode = PickingMode.Ignore });
            _hint.AddToClassList(HintClass);
            _hint.AddToClassList("vv-text-caption");
            _hint.style.position = Position.Absolute;
            _hint.style.display = DisplayStyle.None;
            new KeyedText(_hint, text => _hint.text = text) { Key = PreviewStrings.FullscreenHint };
            _screen.Add(_hint);

            ShowVideo(null);
            RegisterCallback<GeometryChangedEvent>(_ => FitScreen());

            // On the screen rather than the preview: in full screen the screen moves to another parent.
            _screen.RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<DetachFromPanelEvent>(_ => SetFullscreen(false));
        }

        /// <summary>A layer over the video, the size of the stream screen, for a placement canvas.</summary>
        public VisualElement Overlay { get; }

        public bool IsFullscreen => _fullscreenLayer != null;

        public Texture Video => _video.image;

        public string FrameRateText => _frameRate.Number;

        public StatusKind FrameRateStatus => _frameRate.Status;

        public string FormatText => _format.text;

        public event Action<bool> FullscreenChanged;

        /// <summary>Shows <paramref name="source"/> while the preview is on a panel.</summary>
        public void Bind(IStreamPreviewSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            this.Bind(source.Video, ShowVideo);
            this.Bind(source.Stats, stats =>
            {
                _frameRate.Number = PreviewMath.FrameRateText(stats.FramesPerSecond);
                _frameRate.Status = PreviewMath.FrameRateStatus(stats.FramesPerSecond, stats.TargetFramesPerSecond);
                _format.text = PreviewMath.FormatText(stats);
            });
        }

        /// <summary>Shows only the video, filling the window, or returns to the normal layout.</summary>
        public void SetFullscreen(bool fullscreen)
        {
            if (fullscreen == IsFullscreen)
            {
                return;
            }

            if (fullscreen)
            {
                VisualElement root = ThemedRoot.Of(this);
                _fullscreenLayer = new VisualElement();
                _fullscreenLayer.AddToClassList(FullscreenLayerClass);
                Fill(_fullscreenLayer);
                _fullscreenLayer.style.alignItems = Align.Center;
                _fullscreenLayer.style.justifyContent = Justify.Center;
                _fullscreenLayer.RegisterCallback<GeometryChangedEvent>(_ => FitScreen());
                _fullscreenLayer.Add(_screen);
                root.Add(_fullscreenLayer);
                _hint.style.display = DisplayStyle.Flex;
                _hint.schedule.Execute(() => _hint.style.display = DisplayStyle.None).StartingIn(HintDuration);
            }
            else
            {
                Add(_screen);
                _fullscreenLayer.RemoveFromHierarchy();
                _fullscreenLayer = null;
                _hint.style.display = DisplayStyle.None;
            }

            _screen.EnableInClassList(FullscreenClass, fullscreen);
            _fullscreenButton.IconName = fullscreen ? IconName.ExitFullscreen : IconName.Fullscreen;
            _fullscreenButton.WithTooltip(fullscreen ? PreviewStrings.ExitFullscreen : PreviewStrings.Fullscreen, fullscreen ? "Esc" : "F");

            // Keep focus inside the preview so F and Esc keep working from where the user is.
            _fullscreenButton.Focus();
            FitScreen();
            FullscreenChanged?.Invoke(fullscreen);
        }

        private static void Fill(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0;
            element.style.top = 0;
            element.style.right = 0;
            element.style.bottom = 0;
        }

        private void ShowVideo(Texture texture)
        {
            // Without a video the numbers mean nothing; a short note says why the screen is empty.
            _video.image = texture;
            _screen.EnableInClassList(EmptyClass, texture == null);
        }

        private void FitScreen()
        {
            VisualElement room = _fullscreenLayer ?? this;
            Rect available = room.contentRect;
            if (float.IsNaN(available.width) || available.width <= 0 || available.height <= 0)
            {
                return;
            }

            Vector2 size = PreviewMath.Fit(available.width, available.height, Aspect);
            _screen.style.width = size.x;
            _screen.style.height = size.y;
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            bool modifier = evt.ctrlKey || evt.commandKey || evt.altKey;
            if (evt.keyCode == KeyCode.F && !modifier)
            {
                SetFullscreen(!IsFullscreen);
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Escape && IsFullscreen)
            {
                SetFullscreen(false);
                evt.StopPropagation();
            }
        }
    }

    /// <summary>Layout and text of the stream preview. Pure.</summary>
    public static class PreviewMath
    {
        /// <summary>The largest size of the given aspect ratio that fits in the room.</summary>
        public static Vector2 Fit(float width, float height, float aspect)
        {
            return width / height > aspect ? new Vector2(height * aspect, height) : new Vector2(width, width / aspect);
        }

        /// <summary>Such as <c>60.0 fps</c>; numbers and units only, the same in every language.</summary>
        public static string FrameRateText(double framesPerSecond)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.0} fps", framesPerSecond);
        }

        /// <summary>Healthy at the target frame rate (half a frame of slack), a warning below it.</summary>
        public static StatusKind FrameRateStatus(double framesPerSecond, double targetFramesPerSecond)
        {
            return targetFramesPerSecond <= 0 || framesPerSecond >= targetFramesPerSecond - 0.5 ? StatusKind.Success : StatusKind.Warning;
        }

        /// <summary>Such as <c>1920×1080 · 6,000 kbps</c>, or the resolution alone when nothing is encoded.</summary>
        public static string FormatText(PreviewStats stats)
        {
            string resolution = string.Format(CultureInfo.InvariantCulture, "{0}×{1}", stats.Width, stats.Height);
            return stats.BitrateKbps.HasValue
                ? string.Format(CultureInfo.InvariantCulture, "{0} · {1:N0} kbps", resolution, stats.BitrateKbps.Value)
                : resolution;
        }
    }
}
