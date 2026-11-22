using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.Avatar;
using VirtualVessel.Core.Geometry;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Frames;
using VirtualVessel.UI.Lighting;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Preview;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of the face tuning Card (docs/&lt;lang&gt;/ui/screens/setup.md 9).</summary>
    [StringKeys]
    public static class FaceTuningStrings
    {
        public const string Title = "setup.face.title";
        public const string Intro = "setup.face.intro";
        public const string Eyes = "setup.face.eyes";
        public const string EyesHelp = "setup.face.eyes.help";
        public const string Wink = "setup.face.wink";
        public const string WinkHelp = "setup.face.wink.help";
        public const string Brows = "setup.face.brows";
        public const string BrowsHelp = "setup.face.brows.help";
        public const string Mouth = "setup.face.mouth";
        public const string MouthHelp = "setup.face.mouth.help";
        public const string Gaze = "setup.face.gaze";
        public const string GazeHelp = "setup.face.gaze.help";
        public const string Smile = "setup.face.smile";
        public const string SmileHelp = "setup.face.smile.help";
        public const string Fingers = "setup.face.fingers";
        public const string Thumb = "setup.face.thumb";
        public const string FingersHelp = "setup.face.fingers.help";
        public const string Reset = "setup.face.reset";
        public const string ResetButton = "setup.face.reset.button";
        public const string PictureHeight = "setup.face.picture.height";
        public const string PictureZoom = "setup.face.picture.zoom";
        public const string PictureHelp = "setup.face.picture.help";
        public const string Light = "setup.face.light";
        public const string LightHelp = "setup.face.light.help";
        public const string Winks = "setup.face.winks";
        public const string WinksHelp = "setup.face.winks.help";
    }

    /// <summary>
    /// Tunes how strongly the user's face moves the avatar while watching it (docs/&lt;lang&gt;/ui/screens/setup.md
    /// 9, avatar detailed design 7.4): the stream with the avatar, a slider per part, and under each the
    /// avatar's movement of that part now, so the user can raise a part until it reaches the full movement.
    /// Beside the picture, the stage's light, which washes out the face when too bright.
    /// </summary>
    public sealed class FaceTuningCard : VisualElement
    {
        public const string UssClass = "vv-face-card";
        public const string IntroClass = UssClass + "__intro";
        public const string LayoutClass = UssClass + "__layout";
        public const string PictureClass = UssClass + "__picture";
        public const string PreviewClass = UssClass + "__preview";
        public const string PartsClass = UssClass + "__parts";
        public const string PartClass = UssClass + "__part";

        /// <summary>The sliders show percent: 100 is the default sensitivity.</summary>
        public const float PercentPerUnit = 100f;

        /// <summary>
        /// How much the picture enlarges the stream at first, in percent, so the expression can be seen.
        /// The picture keeps the stream's 16:9 shape and its middle; the user changes the zoom and the
        /// height (<see cref="Crop"/>), because the face's size and place depend on the avatar.
        /// </summary>
        public const float DefaultZoom = 220f;

        /// <summary>100% shows the whole stream.</summary>
        public const float MinZoom = 100f;

        public const float MaxZoom = 400f;

        /// <summary>
        /// The picture height at first, in percent: the upper part, where the camera's default position
        /// puts the face of an avatar of average height (video detailed design 7.5).
        /// </summary>
        public const float DefaultPictureHeight = 85f;

        // Often enough to follow a blink, rarely enough to stay light.
        private const long MeterIntervalMs = 50;

        private static readonly string[] s_partKeys =
        {
            FaceTuningStrings.Eyes,
            FaceTuningStrings.Wink,
            FaceTuningStrings.Brows,
            FaceTuningStrings.Mouth,
            FaceTuningStrings.Gaze,
            FaceTuningStrings.Smile,
        };

        // In the order of s_partKeys.
        private static readonly string[] s_partHelpKeys =
        {
            FaceTuningStrings.EyesHelp,
            FaceTuningStrings.WinkHelp,
            FaceTuningStrings.BrowsHelp,
            FaceTuningStrings.MouthHelp,
            FaceTuningStrings.GazeHelp,
            FaceTuningStrings.SmileHelp,
        };

        private readonly IAvatarService _avatars;
        private readonly IStreamPreviewSource _stream;
        private readonly IStageLighting _lighting;
        private readonly IFrameSource _avatarFrame;
        private Label _noVideo;
        private readonly ValueSlider[] _sliders = new ValueSlider[FaceTuning.PartCount];
        private readonly ProgressMeter[] _meters = new ProgressMeter[FaceTuning.PartCount];

        /// <param name="stream">The stream video, to watch the avatar; null shows no picture.</param>
        /// <param name="lighting">The stage's light; null leaves its slider out.</param>
        /// <param name="avatarFrame">The avatar's frame (Stage detailed design 7.2), to zoom about the avatar; null zooms about the stream's middle.</param>
        public FaceTuningCard(IAvatarService avatars, IStreamPreviewSource stream, IStageLighting lighting = null, IFrameSource avatarFrame = null)
        {
            _avatars = avatars ?? throw new ArgumentNullException(nameof(avatars));
            _stream = stream;
            _avatarFrame = avatarFrame;
            AddToClassList(UssClass);
            AddToClassList(CaptureSourceCard.UssClass);
            Card card = Ui.Card(FaceTuningStrings.Title);
            Add(card);
            card.Add(Ui.Text(FaceTuningStrings.Intro, IntroClass, "vv-text-body"));

            _lighting = lighting;

            // The stream beside the sliders, so the avatar stays in view while any part is tuned; on a narrow window
            // the sliders wrap below it.
            var layout = new VisualElement();
            layout.AddToClassList(LayoutClass);
            layout.Add(BuildPicture(lighting != null));
            layout.Add(BuildParts());
            card.Add(layout);

            PanelLifetime.WhileAttached(this, _ => Attach());
        }

        // The whole stream, enlarged and shifted inside a frame that hides the rest. Layout, not the image's UV
        // rectangle, does the cropping: a changed UV rectangle was not redrawn. The face sits higher or lower, and
        // looks larger or smaller, with the avatar; the user moves and zooms the picture to it. Not saved: it is only
        // for looking while tuning.
        private VisualElement BuildPicture(bool withLight)
        {
            Frame = new VisualElement { pickingMode = PickingMode.Ignore };
            Frame.AddToClassList(PreviewClass);
            Preview = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            Preview.style.position = Position.Absolute;
            _noVideo = Ui.Text(CaptureStrings.NoVideo, CaptureSourceCard.PreviewEmptyClass, "vv-text-caption");
            Frame.Add(Preview);
            Frame.Add(_noVideo);
            Place(Preview, Crop(DefaultPictureHeight, DefaultZoom, AreaOf(_avatarFrame)));
            var picture = new VisualElement();
            picture.AddToClassList(PictureClass);
            PictureHeight = Percent(FaceTuningStrings.PictureHeight, FaceTuningStrings.PictureHelp, 0f, 100f, 5f, value => ShowPicture());
            PictureHeight.SetValueWithoutNotify(DefaultPictureHeight);
            PictureZoom = Percent(FaceTuningStrings.PictureZoom, FaceTuningStrings.PictureHelp, MinZoom, MaxZoom, 10f, value => ShowPicture());
            PictureZoom.SetValueWithoutNotify(DefaultZoom);
            picture.Add(Frame);
            picture.Add(PictureHeight);
            picture.Add(PictureZoom);

            // The light is the stage's and changes the stream too, so it is saved; it sits by the picture because a
            // washed-out face is seen there.
            if (withLight)
            {
                Light = Percent(FaceTuningStrings.Light, FaceTuningStrings.LightHelp, StageLightIntensity.Min * PercentPerUnit, StageLightIntensity.Max * PercentPerUnit, 5f, value => _lighting.SetIntensity(value / PercentPerUnit));
                picture.Add(Light);
            }

            return picture;
        }

        // The face's parts with their live meters, the fingers, and reset.
        private VisualElement BuildParts()
        {
            var parts = new VisualElement();
            parts.AddToClassList(PartsClass);

            // Winks are easily misread, so the user chooses whether one eye may close alone; the wink slider only
            // matters while it may.
            WinksToggle = new SwitchToggle();
            WinksToggle.RegisterValueChangedCallback(evt => _avatars.SetFaceTuning(_avatars.FaceTuning.Value.WithWinks(evt.newValue)));
            SettingRow winks = Ui.SettingRow(FaceTuningStrings.Winks, helpKey: FaceTuningStrings.WinksHelp);
            winks.Add(WinksToggle);
            parts.Add(winks);

            for (int i = 0; i < FaceTuning.PartCount; i++)
            {
                var part = (FaceTuningPart)i;
                var block = new VisualElement();
                block.AddToClassList(PartClass);
                ValueSlider slider = Percent(s_partKeys[i], s_partHelpKeys[i], FaceTuning.Min * PercentPerUnit, FaceTuning.Max * PercentPerUnit, 10f, value => _avatars.SetFaceTuning(_avatars.FaceTuning.Value.With(part, value / PercentPerUnit)));
                ProgressMeter meter = Ui.Progress(null);
                block.Add(slider);
                block.Add(meter);
                parts.Add(block);
                _sliders[i] = slider;
                _meters[i] = meter;
            }

            // The stream beside them shows the hands too, with the picture's height and zoom.
            Fingers = Percent(FaceTuningStrings.Fingers, FaceTuningStrings.FingersHelp, FingerTuning.Min * PercentPerUnit, FingerTuning.Max * PercentPerUnit, 10f, value => _avatars.SetFingerTuning(_avatars.FingerTuning.Value.WithBend(value / PercentPerUnit)));
            parts.Add(Fingers);
            Thumb = Percent(FaceTuningStrings.Thumb, FaceTuningStrings.FingersHelp, FingerTuning.Min * PercentPerUnit, FingerTuning.Max * PercentPerUnit, 10f, value => _avatars.SetFingerTuning(_avatars.FingerTuning.Value.WithThumb(value / PercentPerUnit)));
            parts.Add(Thumb);

            ResetButton = Ui.Button(FaceTuningStrings.ResetButton);
            ResetButton.clicked += () =>
            {
                _avatars.SetFaceTuning(new FaceTuning());
                _avatars.SetFingerTuning(new FingerTuning());
            };
            SettingRow resetRow = Ui.SettingRow(FaceTuningStrings.Reset);
            resetRow.Add(ResetButton);
            parts.Add(resetRow);
            return parts;
        }

        // A slider in percent, in steps, that calls changed with each new value.
        private static ValueSlider Percent(string labelKey, string helpKey, float low, float high, float step, Action<float> changed)
        {
            ValueSlider slider = Ui.Slider(labelKey, low, high, "%");
            slider.HelpKey = helpKey;
            slider.Step = step;
            slider.RegisterValueChangedCallback(evt => changed(evt.newValue));
            return slider;
        }

        /// <summary>How strongly the four fingers bend, in percent.</summary>
        public ValueSlider Fingers { get; private set; }

        /// <summary>How strongly the thumb bends, in percent.</summary>
        public ValueSlider Thumb { get; private set; }

        /// <summary>The stream, to watch the avatar while tuning.</summary>
        public Image Preview { get; private set; }

        /// <summary>The 16:9 frame the enlarged stream is seen through.</summary>
        public VisualElement Frame { get; private set; }

        /// <summary>The slider of each <see cref="FaceTuningPart"/>, in percent.</summary>
        public IReadOnlyList<ValueSlider> Sliders => _sliders;

        /// <summary>The avatar's movement of each part now, 0 to 1.</summary>
        public IReadOnlyList<ProgressMeter> Meters => _meters;

        /// <summary>Whether one eye may close alone.</summary>
        public SwitchToggle WinksToggle { get; private set; }

        /// <summary>Puts every part back to the default.</summary>
        public ActionButton ResetButton { get; private set; }

        /// <summary>Which height of the stream the picture shows, 0 (bottom) to 100% (top).</summary>
        public ValueSlider PictureHeight { get; private set; }

        /// <summary>How much the picture enlarges the stream, in percent.</summary>
        public ValueSlider PictureZoom { get; private set; }

        /// <summary>The stage light's scale in percent; null without the stage.</summary>
        public ValueSlider Light { get; private set; }

        /// <summary>
        /// Where the whole stream goes, in percent of the frame, so that the frame shows
        /// <paramref name="crop"/> (UV, from the bottom left): larger by 1 / the crop's size, and
        /// shifted so the crop's top left is at the frame's. Pure.
        /// </summary>
        public static (float Left, float Top, float Width, float Height) Layout(Rect crop)
        {
            float width = 100f / crop.width;
            float height = 100f / crop.height;
            return (-crop.x * width, -(1f - crop.yMax) * height, width, height);
        }

        /// <summary>
        /// The part of the stream shown, in UV (0 to 1 from the bottom left), looking at
        /// <paramref name="area"/> (UV; the whole stream by default): at <paramref name="heightPercent"/>
        /// of its height, 0 (bottom) to 100 (top), enlarged by <paramref name="zoomPercent"/> of its
        /// height, about its horizontal middle; in the stream's own shape, kept within the stream. Pure.
        /// </summary>
        public static Rect Crop(float heightPercent, float zoomPercent, Rect? area = null)
        {
            Rect looked = area ?? new Rect(0f, 0f, 1f, 1f);
            float size = Mathf.Min(1f, looked.height * 100f / Mathf.Clamp(zoomPercent, MinZoom, MaxZoom));
            float y = looked.y + Mathf.Clamp01(heightPercent / 100f) * (looked.height - size);
            float x = looked.center.x - size / 2f;
            return new Rect(Mathf.Clamp(x, 0f, 1f - size), Mathf.Clamp(y, 0f, 1f - size), size, size);
        }

        /// <summary>
        /// What the picture looks at, in UV: the avatar frame's place on the stream when the avatar shows, so the
        /// height and zoom follow the avatar wherever and however large it is placed; else the whole stream. Pure.
        /// </summary>
        public static Rect? AreaOf(IFrameSource avatarFrame)
        {
            if (avatarFrame == null || !avatarFrame.Visible.Value)
            {
                return null;
            }

            NormalizedRect placement = avatarFrame.Layout.Value.Placement;
            return new Rect((float)placement.X, (float)(1.0 - placement.Y - placement.Height), (float)placement.Width, (float)placement.Height);
        }

        private void ShowPicture()
        {
            Place(Preview, Crop(PictureHeight.value, PictureZoom.value, AreaOf(_avatarFrame)));
        }

        /// <summary>
        /// How far the avatar moves <paramref name="part"/> in <paramref name="face"/>, 0 to 1: what the
        /// user raises a slider for until doing the movement fully reaches 1. Read from the face parts,
        /// which follow the same tuning as the whole-face expressions. Pure.
        /// </summary>
        public static float Movement(FaceTarget face, FaceTuningPart part)
        {
            if (face == null)
            {
                return 0f;
            }

            float value = part switch
            {
                FaceTuningPart.Eyes => Mathf.Max(face[FacePart.EyeCloseLeft], face[FacePart.EyeCloseRight]),
                FaceTuningPart.Wink => Mathf.Abs(face[FacePart.EyeCloseLeft] - face[FacePart.EyeCloseRight]),
                FaceTuningPart.Brows => Mathf.Max(face[FacePart.BrowUp], Mathf.Max(face[FacePart.BrowDown], face[FacePart.BrowSad])),
                FaceTuningPart.Mouth => face[FacePart.MouthA] + face[FacePart.MouthI] + face[FacePart.MouthU] + face[FacePart.MouthE] + face[FacePart.MouthO] + face[FacePart.MouthOpenSmile],
                FaceTuningPart.Gaze => Mathf.Max(Mathf.Abs(face.LookYaw), Mathf.Abs(face.LookPitch)),
                FaceTuningPart.Smile => Mathf.Max(face[FacePart.MouthSmile], face[FacePart.MouthOpenSmile]),
                _ => 0f,
            };
            return Mathf.Clamp01(value);
        }

        private IDisposable Attach()
        {
            IVisualElementScheduledItem meters = schedule.Execute(ShowMovement).Every(MeterIntervalMs);
            var subscriptions = new List<IDisposable>
            {
                _avatars.FaceTuning.Subscribe(ShowTuning),
                _avatars.FingerTuning.Subscribe(ShowFingers),
                new DisposeAction(meters.Pause),
            };
            ShowTuning(_avatars.FaceTuning.Value);
            ShowFingers(_avatars.FingerTuning.Value);
            if (_stream != null)
            {
                subscriptions.Add(_stream.Video.Subscribe(ShowVideo));
            }

            ShowVideo(_stream?.Video.Value);
            if (_lighting != null)
            {
                subscriptions.Add(_lighting.Intensity.Subscribe(ShowLight));
                ShowLight(_lighting.Intensity.Value);
            }

            // The avatar may have been moved on the live screen since; the picture follows it.
            if (_avatarFrame != null)
            {
                subscriptions.Add(_avatarFrame.Layout.Subscribe(_ => ShowPicture()));
                subscriptions.Add(_avatarFrame.Visible.Subscribe(_ => ShowPicture()));
            }

            ShowPicture();
            return new DisposeAll(subscriptions);
        }

        private void ShowTuning(FaceTuning tuning)
        {
            for (int i = 0; i < _sliders.Length; i++)
            {
                _sliders[i].SetValueWithoutNotify(tuning.Get((FaceTuningPart)i) * PercentPerUnit);
            }

            WinksToggle.SetValueWithoutNotify(tuning.Winks);
            _sliders[(int)FaceTuningPart.Wink].SetEnabled(tuning.Winks);
        }

        private void ShowFingers(FingerTuning tuning)
        {
            Fingers.SetValueWithoutNotify(tuning.Bend * PercentPerUnit);
            Thumb.SetValueWithoutNotify(tuning.Thumb * PercentPerUnit);
        }

        private void ShowLight(float intensity)
        {
            Light.SetValueWithoutNotify(intensity * PercentPerUnit);
        }

        private void ShowMovement()
        {
            // Six reads of the face shown this frame; the meters of a closed Card are not drawn.
            FaceTarget face = _avatars.Face;
            for (int i = 0; i < _meters.Length; i++)
            {
                _meters[i].Progress = Movement(face, (FaceTuningPart)i);
            }
        }

        private static void Place(Image image, Rect crop)
        {
            (float left, float top, float width, float height) = Layout(crop);
            image.style.left = Length.Percent(left);
            image.style.top = Length.Percent(top);
            image.style.width = Length.Percent(width);
            image.style.height = Length.Percent(height);
        }

        private void ShowVideo(Texture texture)
        {
            Preview.image = texture;
            _noVideo.style.display = texture == null ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
