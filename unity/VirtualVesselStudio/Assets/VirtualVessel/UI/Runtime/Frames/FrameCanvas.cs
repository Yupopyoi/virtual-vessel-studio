using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.Core.Geometry;
using VirtualVessel.UI.Commands;
using VirtualVessel.UI.Controls;

namespace VirtualVessel.UI.Frames
{
    /// <summary>
    /// A transparent layer over the stream preview for editing where videos are placed and which part of
    /// them is shown (UI foundation detailed design 9). It turns pointer and key input into
    /// <see cref="FrameEditMath"/> calls, shows each step through <see cref="IFrameSource.PreviewLayout"/>,
    /// and commits when a drag, key press, or burst of wheel steps ends.
    /// </summary>
    /// <remarks>
    /// Put it inside the element that shows the stream screen; the layer spans that element and reaches
    /// <see cref="HandleRadius"/> beyond it, so handles on the edge can still be grabbed. Only styles
    /// change while dragging; no video is copied (UI foundation detailed design 16).
    /// </remarks>
    [UxmlElement]
    public partial class FrameCanvas : VisualElement
    {
        public const string UssClass = "vv-frame-canvas";

        public const string StreamClass = UssClass + "__stream";

        public const string GuideClass = UssClass + "__guide";

        public const string ReadoutClass = UssClass + "__readout";

        public const string GhostClass = UssClass + "__ghost";

        public const string ModeSwitchClass = UssClass + "__mode";

        /// <summary>A handle can be grabbed this many pixels from its center: a 24px target (components.md 2).</summary>
        public const float HandleRadius = 12f;

        /// <summary>Frames snap to lines this many screen pixels away (UI foundation detailed design 9.3).</summary>
        public const float SnapDistance = 8f;

        /// <summary>How long the readout stays after an edit ends, in milliseconds.</summary>
        private const long ReadoutLinger = 700;

        /// <summary>Wheel steps closer together than this, in milliseconds, are one edit.</summary>
        private const long WheelBurst = 400;

        private readonly VisualElement _stream;
        private readonly VisualElement _videos;
        private readonly List<Image> _videoImages = new List<Image>();
        private bool _showVideos;
        private readonly Image _ghost;
        private readonly VisualElement _guideX;
        private readonly VisualElement _guideY;
        private readonly Label _readout;
        private readonly VisualElement _modeSwitch;
        private readonly Segmented _mode;
        private readonly List<MediaFrame> _frames = new List<MediaFrame>();
        private readonly FrameHistory _history = new FrameHistory();
        private MediaFrame _selected;
        private bool _cropMode;
        private bool _spaceDown;
        private Gesture _gesture;
        private Burst _nudge;
        private Burst _wheel;
        private IVisualElementScheduledItem _hideReadout;
        private PopupMenu _menu;

        // The last left press on a frame, for telling a double-click on the same frame.
        private MediaFrame _lastPressFrame;
        private long _lastPressTime;
        private Vector2 _lastPressPosition;

        public FrameCanvas()
        {
            AddToClassList(UssClass);
            focusable = true;
            tabIndex = 0;

            // The geometry is part of how the canvas works, not of its look, so it is set here rather
            // than in USS: the layer reaches HandleRadius beyond the stream screen on every side.
            Inset(this, -HandleRadius);
            FocusRing.Attach(this);

            _stream = new VisualElement { pickingMode = PickingMode.Ignore };
            _stream.AddToClassList(StreamClass);
            Inset(_stream, HandleRadius);
            Add(_stream);

            // Videos drawn by the canvas itself, for screens without a stream preview (9.2); clipped to
            // the stream screen as the stream would be.
            _videos = new VisualElement { pickingMode = PickingMode.Ignore };
            Inset(_videos, 0);
            _videos.style.overflow = Overflow.Hidden;
            _videos.style.display = DisplayStyle.None;
            _stream.Add(_videos);

            // Behind the frames: the whole video, faint, while cropping. A zoomed video can be many times
            // the screen, so it is clipped to the stream screen instead of covering the rest of the UI.
            var ghostClip = new VisualElement { pickingMode = PickingMode.Ignore };
            Inset(ghostClip, 0);
            ghostClip.style.overflow = Overflow.Hidden;
            _ghost = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            _ghost.AddToClassList(GhostClass);
            _ghost.style.position = Position.Absolute;
            _ghost.style.display = DisplayStyle.None;
            ghostClip.Add(_ghost);
            _stream.Add(ghostClip);

            _guideX = Guide(vertical: true);
            _guideY = Guide(vertical: false);
            _readout = QuietText.Of(new Label { pickingMode = PickingMode.Ignore });
            _readout.AddToClassList(ReadoutClass);
            _readout.AddToClassList("vv-text-mono");
            _readout.style.display = DisplayStyle.None;
            _stream.Add(_readout);

            // The current mode is always visible and switchable, not only by double-click (9.3).
            _modeSwitch = new VisualElement();
            _modeSwitch.AddToClassList(ModeSwitchClass);
            _modeSwitch.style.position = Position.Absolute;
            Label modeLabel = QuietText.Of(new Label { pickingMode = PickingMode.Ignore });
            modeLabel.AddToClassList(ModeSwitchClass + "-label");
            modeLabel.AddToClassList("vv-text-caption");
            new KeyedText(modeLabel, text => modeLabel.text = text) { Key = FrameStrings.Mode };
            _modeSwitch.Add(modeLabel);
            _mode = new Segmented(FrameStrings.ModePlace, FrameStrings.ModeCrop) { TooltipKey = FrameStrings.ModeTip };
            _mode.RegisterValueChangedCallback(evt => SetCropMode(evt.newValue == 1));
            _modeSwitch.Add(_mode);
            _modeSwitch.style.display = DisplayStyle.None;
            _stream.Add(_modeSwitch);

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(evt => EndGesture(evt.pointerId, commit: true));
            RegisterCallback<PointerCaptureOutEvent>(_ => EndGesture(-1, commit: true));
            RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (_gesture == null)
                {
                    CursorService.Reset();
                }
            });
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<KeyUpEvent>(OnKeyUp);
            RegisterCallback<NavigationMoveEvent>(OnNavigationMove);
            RegisterCallback<FocusInEvent>(OnFocusIn);
            RegisterCallback<FocusOutEvent>(_ =>
            {
                _spaceDown = false;
                CommitBursts();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                EndGesture(-1, commit: true);
                CommitBursts();
                _menu?.Close();
                CursorService.Reset();

                // The history lives only while the editor is open (9.3).
                _history.Clear();
            });
        }

        /// <summary>Size of the stream screen in output pixels; aspect ratios, minimum sizes, and nudges use it.</summary>
        public CanvasSize OutputSize { get; set; } = new CanvasSize(1920, 1080);

        public IReadOnlyList<MediaFrame> Frames => _frames;

        public IFrameSource Selected => _selected?.Source;

        /// <summary>Crop mode: handles cut the edges and dragging inside moves the window over the video (9.3).</summary>
        public bool CropMode => _cropMode;

        public FrameHistory History => _history;

        /// <summary>The open frame menu, or null.</summary>
        public PopupMenu Menu => _menu != null && _menu.IsOpen ? _menu : null;

        /// <summary>Whether the readout of position and size is on screen.</summary>
        public bool ReadoutVisible => _readout.style.display == DisplayStyle.Flex;

        public string ReadoutText => _readout.text;

        public event Action<IFrameSource> SelectionChanged;

        public event Action<bool> CropModeChanged;

        /// <summary>Raised after <see cref="SetSources"/> replaces the frames.</summary>
        public event Action FramesChanged;

        /// <summary>
        /// Draws each video itself, in stacking order, for screens without a stream preview behind the
        /// canvas (UI foundation detailed design 9.2). Crops are UVs and flips are scales; no video is
        /// copied.
        /// </summary>
        public bool ShowVideos
        {
            get => _showVideos;
            set
            {
                _showVideos = value;
                _videos.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                RefreshVideos();
            }
        }

        /// <summary>The videos to edit. Frames that are no longer listed are removed with their selection.</summary>
        public void SetSources(IEnumerable<IFrameSource> sources)
        {
            if (sources == null)
            {
                throw new ArgumentNullException(nameof(sources));
            }

            EndGesture(-1, commit: true);
            CommitBursts();
            IFrameSource selected = Selected;
            foreach (MediaFrame frame in _frames)
            {
                frame.RemoveFromHierarchy();
            }

            _frames.Clear();
            _videos.Clear();
            _videoImages.Clear();
            _selected = null;
            foreach (IFrameSource source in sources)
            {
                var frame = new MediaFrame(source);
                frame.ShownChanged += () =>
                {
                    if (frame == _selected)
                    {
                        RefreshSelectionParts();
                    }

                    RefreshVideos();
                };
                frame.VisibleChanged += () =>
                {
                    // A video that leaves the stream cannot stay selected.
                    if (frame == _selected && !frame.IsVisible)
                    {
                        Select(null);
                    }

                    RefreshVideos();
                };
                _frames.Add(frame);
                _stream.Insert(_stream.IndexOf(_guideX), frame);
                var video = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
                video.style.position = Position.Absolute;
                _videoImages.Add(video);
                _videos.Add(video);
            }

            MediaFrame keep = _frames.Find(f => f.Source == selected);
            Select(keep?.Source);
            RefreshVideos();
            FramesChanged?.Invoke();
        }

        public void Select(IFrameSource source)
        {
            MediaFrame frame = source == null ? null : _frames.Find(f => f.Source == source);
            if (frame != null && !frame.IsVisible)
            {
                // A hidden video cannot be selected; asking for it changes nothing.
                return;
            }

            if (frame == _selected)
            {
                return;
            }

            CommitBursts();
            if (_selected != null)
            {
                _selected.Selected = false;
                _selected.CropMode = false;
                _selected.HoverHandle = null;
            }

            _selected = frame;
            if (frame != null)
            {
                frame.Selected = true;

                // Above the other outlines; the readout and guides stay on top.
                frame.PlaceBehind(_guideX);
            }

            // A newly selected frame always starts in placement mode, so a click never lands in a mode
            // that was chosen for another frame.
            SetCropMode(false);

            RefreshSelectionParts();
            SelectionChanged?.Invoke(Selected);
        }

        /// <summary>Switches between placement and crop mode for the selected frame.</summary>
        public void SetCropMode(bool crop)
        {
            crop = crop && _selected != null && !_selected.Shown.Locked;
            if (crop == _cropMode)
            {
                _mode.SetValueWithoutNotify(crop ? 1 : 0);
                return;
            }

            CommitBursts();
            _cropMode = crop;
            _mode.SetValueWithoutNotify(crop ? 1 : 0);
            RefreshSelectionParts();
            CropModeChanged?.Invoke(crop);
        }

        private static void Inset(VisualElement element, float inset)
        {
            element.style.position = Position.Absolute;
            element.style.left = inset;
            element.style.top = inset;
            element.style.right = inset;
            element.style.bottom = inset;
        }

        private VisualElement Guide(bool vertical)
        {
            var guide = new VisualElement { pickingMode = PickingMode.Ignore };
            guide.AddToClassList(GuideClass);
            guide.AddToClassList(GuideClass + (vertical ? "--x" : "--y"));
            guide.style.position = Position.Absolute;
            guide.style.display = DisplayStyle.None;
            _stream.Add(guide);
            return guide;
        }

        /// <summary>The mode switch, the crop look, and the faint whole video follow the selection.</summary>
        private void RefreshSelectionParts()
        {
            bool editable = _selected != null && !_selected.Shown.Locked;
            _modeSwitch.style.display = editable ? DisplayStyle.Flex : DisplayStyle.None;
            if (_selected != null)
            {
                _selected.CropMode = _cropMode;
            }

            if (!_cropMode || _selected == null)
            {
                _ghost.style.display = DisplayStyle.None;
                return;
            }

            FrameLayout shown = _selected.Shown;
            MediaFrame.Place(_ghost, FrameEditMath.VideoPlacement(shown));
            _ghost.image = _selected.Source.Video;
            _ghost.style.scale = new Scale(new Vector3(shown.FlipHorizontal ? -1 : 1, 1, 1));
            _ghost.style.display = DisplayStyle.Flex;
        }

        private List<MediaFrame> VisibleFrames()
        {
            return _frames.FindAll(f => f.IsVisible);
        }

        /// <summary>The videos in stacking order, placed, cropped, and mirrored as the stream shows them.</summary>
        private void RefreshVideos()
        {
            if (!_showVideos)
            {
                return;
            }

            for (int i = 0; i < _frames.Count; i++)
            {
                FrameLayout shown = _frames[i].Shown;
                Image video = _videoImages[i];
                video.image = _frames[i].Source.Video;
                video.style.display = _frames[i].IsVisible ? DisplayStyle.Flex : DisplayStyle.None;
                MediaFrame.Place(video, shown.Placement);
                NormalizedRect c = shown.Crop;

                // Texture UVs start at the bottom left; crops start at the top left.
                video.uv = new Rect((float)c.X, (float)(1 - c.Bottom), (float)c.Width, (float)c.Height);
                video.style.scale = new Scale(new Vector3(shown.FlipHorizontal ? -1 : 1, 1, 1));
            }

            int[] frontToBack = FrameCanvasMath.FrontToBack(ShownLayouts());
            for (int k = frontToBack.Length - 1; k >= 0; k--)
            {
                _videoImages[frontToBack[k]].BringToFront();
            }
        }

        private List<FrameLayout> ShownLayouts()
        {
            var layouts = new List<FrameLayout>(_frames.Count);
            foreach (MediaFrame frame in _frames)
            {
                layouts.Add(frame.Shown);
            }

            return layouts;
        }

        private IEnumerable<FrameLayout> OtherLayouts(MediaFrame except)
        {
            foreach (MediaFrame frame in _frames)
            {
                if (frame != except)
                {
                    yield return frame.Shown;
                }
            }
        }

        private Vector2 StreamPixels(Vector2 panelPosition)
        {
            return _stream.WorldToLocal(panelPosition);
        }

        private Vector2 StreamSize()
        {
            Rect r = _stream.contentRect;
            return new Vector2(Mathf.Max(1, r.width), Mathf.Max(1, r.height));
        }

        // ------------------------------------------------------------------ Commit and display

        private void CommitBursts()
        {
            CommitNudge();
            CommitWheel();
        }

        private void CommitNudge()
        {
            Burst nudge = _nudge;
            _nudge = null;
            if (nudge != null)
            {
                Commit(nudge.Frame, nudge.Before, nudge.Frame.Shown, record: true);
            }
        }

        private void CommitWheel()
        {
            Burst wheel = _wheel;
            _wheel = null;
            if (wheel != null)
            {
                wheel.Timer?.Pause();
                Commit(wheel.Frame, wheel.Before, wheel.Frame.Shown, record: true);
            }
        }

        private void Commit(MediaFrame frame, FrameLayout before, FrameLayout layout, bool record)
        {
            if (record)
            {
                _history.Record(frame.Source, before, layout);
            }

            int version = frame.ShowPending(layout);
            _ = CommitAsync(frame, layout, version);
        }

        private async Task CommitAsync(MediaFrame frame, FrameLayout layout, int version)
        {
            // Failures are reported by CommandRunner; either way the frame then shows what Stage holds.
            await CommandRunner.RunAsync(() => frame.Source.CommitLayoutAsync(layout, CancellationToken.None), UiServices.Find(this));
            frame.EndPending(version);
        }

        private void SetGuides(double? x, double? y)
        {
            _guideX.style.display = x.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            _guideY.style.display = y.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (x.HasValue)
            {
                _guideX.style.left = Length.Percent((float)(x.Value * 100));
            }

            if (y.HasValue)
            {
                _guideY.style.top = Length.Percent((float)(y.Value * 100));
            }
        }

        private void ShowReadout(FrameLayout layout, bool crop)
        {
            _hideReadout?.Pause();
            NormalizedRect placement = layout.Placement;
            bool below = FrameCanvasMath.ReadoutBelow(placement);
            _readout.text = crop ? FrameCanvasMath.CropReadout(layout, OutputSize) : FrameCanvasMath.Readout(placement, OutputSize);
            _readout.style.position = Position.Absolute;
            _readout.style.left = Length.Percent((float)(placement.CenterX * 100));
            _readout.style.top = Length.Percent((float)((below ? placement.Bottom : placement.Top) * 100));
            _readout.style.translate = new Translate(Length.Percent(-50), Length.Percent(below ? 0 : -100));
            _readout.EnableInClassList(ReadoutClass + "--below", below);
            _readout.style.display = DisplayStyle.Flex;
            _readout.BringToFront();
        }

        private void HideReadoutLater()
        {
            _hideReadout?.Pause();
            _hideReadout = schedule.Execute(() => _readout.style.display = DisplayStyle.None).StartingIn(ReadoutLinger);
        }

        /// <summary>Repeated key presses or wheel steps that become one edit when they stop.</summary>
        private sealed class Burst
        {
            public Burst(MediaFrame frame, FrameLayout before)
            {
                Frame = frame;
                Before = before;
            }

            public MediaFrame Frame { get; }

            public FrameLayout Before { get; }

            public IVisualElementScheduledItem Timer { get; set; }
        }
    }
}
