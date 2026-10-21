using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.Avatar;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
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
        public const string Wink = "setup.face.wink";
        public const string Brows = "setup.face.brows";
        public const string Mouth = "setup.face.mouth";
        public const string Gaze = "setup.face.gaze";
        public const string Smile = "setup.face.smile";
        public const string Reset = "setup.face.reset";
        public const string ResetButton = "setup.face.reset.button";
        public const string PictureHeight = "setup.face.picture.height";
        public const string PictureZoom = "setup.face.picture.zoom";
        public const string Light = "setup.face.light";
        public const string LightDescription = "setup.face.light.description";
        public const string Winks = "setup.face.winks";
        public const string WinksDescription = "setup.face.winks.description";
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
        public const string LightNoteClass = UssClass + "__light-note";

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

        private readonly IAvatarService _avatars;
        private readonly IStreamPreviewSource _stream;
        private readonly IStageLighting _lighting;
        private readonly Label _noVideo;
        private readonly ValueSlider[] _sliders = new ValueSlider[FaceTuning.PartCount];
        private readonly ProgressMeter[] _meters = new ProgressMeter[FaceTuning.PartCount];

        /// <param name="stream">The stream video, to watch the avatar; null shows no picture.</param>
        /// <param name="lighting">The stage's light; null leaves its slider out.</param>
        public FaceTuningCard(IAvatarService avatars, IStreamPreviewSource stream, IStageLighting lighting = null)
        {
            _avatars = avatars ?? throw new ArgumentNullException(nameof(avatars));
            _stream = stream;
            AddToClassList(UssClass);
            AddToClassList(CaptureSourceCard.UssClass);
            Card card = Ui.Card(FaceTuningStrings.Title);
            Add(card);
            card.Add(Ui.Text(FaceTuningStrings.Intro, IntroClass, "vv-text-body"));

            // The whole stream, enlarged and shifted inside a frame that hides the rest. Layout, not the
            // image's UV rectangle, does the cropping: a changed UV rectangle was not redrawn.
            Frame = new VisualElement { pickingMode = PickingMode.Ignore };
            Frame.AddToClassList(PreviewClass);
            Preview = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            Preview.style.position = Position.Absolute;
            _noVideo = Ui.Text(CaptureStrings.NoVideo, CaptureSourceCard.PreviewEmptyClass, "vv-text-caption");
            Frame.Add(Preview);
            Frame.Add(_noVideo);
            Place(Preview, Crop(DefaultPictureHeight, DefaultZoom));
            // The stream beside the sliders, so the avatar stays in view while any part is tuned; on a
            // narrow window the sliders wrap below it.
            var layout = new VisualElement();
            layout.AddToClassList(LayoutClass);
            var parts = new VisualElement();
            parts.AddToClassList(PartsClass);
            // The face sits higher or lower, and looks larger or smaller, with the avatar; the user moves
            // and zooms the picture to it. Not saved: it is only for looking while tuning.
            var picture = new VisualElement();
            picture.AddToClassList(PictureClass);
            PictureHeight = Ui.Slider(FaceTuningStrings.PictureHeight, 0f, 100f, "%");
            PictureHeight.Step = 5f;
            PictureHeight.SetValueWithoutNotify(DefaultPictureHeight);
            PictureHeight.RegisterValueChangedCallback(evt => Place(Preview, Crop(evt.newValue, PictureZoom.value)));
            PictureZoom = Ui.Slider(FaceTuningStrings.PictureZoom, MinZoom, MaxZoom, "%");
            PictureZoom.Step = 10f;
            PictureZoom.SetValueWithoutNotify(DefaultZoom);
            PictureZoom.RegisterValueChangedCallback(evt => Place(Preview, Crop(PictureHeight.value, evt.newValue)));
            picture.Add(Frame);
            picture.Add(PictureHeight);
            picture.Add(PictureZoom);

            // The light is the stage's and changes the stream too, so it is saved; it sits by the
            // picture because a washed-out face is seen there.
            _lighting = lighting;
            if (lighting != null)
            {
                Light = Ui.Slider(FaceTuningStrings.Light, StageLightIntensity.Min * PercentPerUnit, StageLightIntensity.Max * PercentPerUnit, "%");
                Light.Step = 5f;
                Light.RegisterValueChangedCallback(evt => _lighting.SetIntensity(evt.newValue / PercentPerUnit));
                picture.Add(Light);
                picture.Add(Ui.Text(FaceTuningStrings.LightDescription, LightNoteClass, "vv-text-caption"));
            }

            layout.Add(picture);
            layout.Add(parts);
            card.Add(layout);

            // Winks are easily misread, so the user chooses whether one eye may close alone; the wink
            // slider only matters while it may.
            WinksToggle = new SwitchToggle();
            WinksToggle.RegisterValueChangedCallback(evt => _avatars.SetFaceTuning(_avatars.FaceTuning.Value.WithWinks(evt.newValue)));
            SettingRow winks = Ui.SettingRow(FaceTuningStrings.Winks, FaceTuningStrings.WinksDescription);
            winks.Add(WinksToggle);
            parts.Add(winks);

            for (int i = 0; i < FaceTuning.PartCount; i++)
            {
                var part = (FaceTuningPart)i;
                var block = new VisualElement();
                block.AddToClassList(PartClass);
                ValueSlider slider = Ui.Slider(s_partKeys[i], FaceTuning.Min * PercentPerUnit, FaceTuning.Max * PercentPerUnit, "%");
                slider.Step = 10f;
                slider.RegisterValueChangedCallback(evt => _avatars.SetFaceTuning(_avatars.FaceTuning.Value.With(part, evt.newValue / PercentPerUnit)));
                ProgressMeter meter = Ui.Progress(null);
                block.Add(slider);
                block.Add(meter);
                parts.Add(block);
                _sliders[i] = slider;
                _meters[i] = meter;
            }

            ResetButton = Ui.Button(FaceTuningStrings.ResetButton);
            ResetButton.clicked += () => _avatars.SetFaceTuning(new FaceTuning());
            SettingRow resetRow = Ui.SettingRow(FaceTuningStrings.Reset);
            resetRow.Add(ResetButton);
            parts.Add(resetRow);

            PanelLifetime.WhileAttached(this, _ => Attach());
        }

        /// <summary>The stream, to watch the avatar while tuning.</summary>
        public Image Preview { get; }

        /// <summary>The 16:9 frame the enlarged stream is seen through.</summary>
        public VisualElement Frame { get; }

        /// <summary>The slider of each <see cref="FaceTuningPart"/>, in percent.</summary>
        public IReadOnlyList<ValueSlider> Sliders => _sliders;

        /// <summary>The avatar's movement of each part now, 0 to 1.</summary>
        public IReadOnlyList<ProgressMeter> Meters => _meters;

        /// <summary>Whether one eye may close alone.</summary>
        public SwitchToggle WinksToggle { get; }

        /// <summary>Puts every part back to the default.</summary>
        public ActionButton ResetButton { get; }

        /// <summary>Which height of the stream the picture shows, 0 (bottom) to 100% (top).</summary>
        public ValueSlider PictureHeight { get; }

        /// <summary>How much the picture enlarges the stream, in percent.</summary>
        public ValueSlider PictureZoom { get; }

        /// <summary>The stage light's scale in percent; null without the stage.</summary>
        public ValueSlider Light { get; }

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
        /// The part of the stream shown, in UV (0 to 1 from the bottom left), at
        /// <paramref name="heightPercent"/>, 0 (bottom) to 100 (top), enlarged by
        /// <paramref name="zoomPercent"/>: the stream's middle, in its own shape, kept within it. Pure.
        /// </summary>
        public static Rect Crop(float heightPercent, float zoomPercent)
        {
            float size = 100f / Mathf.Clamp(zoomPercent, MinZoom, MaxZoom);
            float y = Mathf.Clamp01(heightPercent / 100f) * (1f - size);
            return new Rect((1f - size) / 2f, y, size, size);
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
                new DisposeAction(meters.Pause),
            };
            ShowTuning(_avatars.FaceTuning.Value);
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

        private sealed class DisposeAll : IDisposable
        {
            private readonly List<IDisposable> _items;

            public DisposeAll(List<IDisposable> items)
            {
                _items = items;
            }

            public void Dispose()
            {
                foreach (IDisposable item in _items)
                {
                    item.Dispose();
                }
            }
        }
    }
}
