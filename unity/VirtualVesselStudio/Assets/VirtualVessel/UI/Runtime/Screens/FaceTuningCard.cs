using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.Avatar;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
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
    }

    /// <summary>
    /// Tunes how strongly the user's face moves the avatar while watching it (docs/&lt;lang&gt;/ui/screens/setup.md
    /// 9, avatar detailed design 7.4): the stream with the avatar, a slider per part, and under each the
    /// avatar's movement of that part now, so the user can raise a part until it reaches the full movement.
    /// </summary>
    public sealed class FaceTuningCard : VisualElement
    {
        public const string UssClass = "vv-face-card";
        public const string IntroClass = UssClass + "__intro";
        public const string LayoutClass = UssClass + "__layout";
        public const string PreviewClass = UssClass + "__preview";
        public const string PartsClass = UssClass + "__parts";
        public const string PartClass = UssClass + "__part";

        /// <summary>The sliders show percent: 100 is the default sensitivity.</summary>
        public const float PercentPerUnit = 100f;

        /// <summary>
        /// The part of the stream shown, in UV (0 to 1 from the bottom left): the middle, upper part,
        /// where the camera's default position puts the avatar's face and chest (video detailed design
        /// 7.5), enlarged about 2.2 times so the expression can be seen. The same 16:9 shape as the stream.
        /// </summary>
        public static readonly Rect FaceCrop = new Rect(0.275f, 0.48f, 0.45f, 0.45f);

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
        private readonly Label _noVideo;
        private readonly ValueSlider[] _sliders = new ValueSlider[FaceTuning.PartCount];
        private readonly ProgressMeter[] _meters = new ProgressMeter[FaceTuning.PartCount];

        /// <param name="stream">The stream video, to watch the avatar; null shows no picture.</param>
        public FaceTuningCard(IAvatarService avatars, IStreamPreviewSource stream)
        {
            _avatars = avatars ?? throw new ArgumentNullException(nameof(avatars));
            _stream = stream;
            AddToClassList(UssClass);
            AddToClassList(CaptureSourceCard.UssClass);
            Card card = Ui.Card(FaceTuningStrings.Title);
            Add(card);
            card.Add(Ui.Text(FaceTuningStrings.Intro, IntroClass, "vv-text-body"));

            Preview = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore, uv = FaceCrop };
            Preview.AddToClassList(PreviewClass);
            _noVideo = Ui.Text(CaptureStrings.NoVideo, CaptureSourceCard.PreviewEmptyClass, "vv-text-caption");
            Preview.Add(_noVideo);
            // The stream beside the sliders, so the avatar stays in view while any part is tuned; on a
            // narrow window the sliders wrap below it.
            var layout = new VisualElement();
            layout.AddToClassList(LayoutClass);
            var parts = new VisualElement();
            parts.AddToClassList(PartsClass);
            layout.Add(Preview);
            layout.Add(parts);
            card.Add(layout);

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

        /// <summary>The slider of each <see cref="FaceTuningPart"/>, in percent.</summary>
        public IReadOnlyList<ValueSlider> Sliders => _sliders;

        /// <summary>The avatar's movement of each part now, 0 to 1.</summary>
        public IReadOnlyList<ProgressMeter> Meters => _meters;

        /// <summary>Puts every part back to the default.</summary>
        public ActionButton ResetButton { get; }

        /// <summary>
        /// How far the avatar moves <paramref name="part"/> in <paramref name="face"/>, 0 to 1: what the
        /// user raises a slider for until doing the movement fully reaches 1. Pure.
        /// </summary>
        public static float Movement(FaceTarget face, FaceTuningPart part)
        {
            if (face == null)
            {
                return 0f;
            }

            float value = part switch
            {
                FaceTuningPart.Eyes => Mathf.Max(face[AvatarExpression.Blink], Mathf.Max(face[AvatarExpression.BlinkLeft], face[AvatarExpression.BlinkRight])),
                FaceTuningPart.Wink => Mathf.Abs(face[AvatarExpression.BlinkLeft] - face[AvatarExpression.BlinkRight]),
                FaceTuningPart.Brows => Mathf.Max(face[AvatarExpression.Surprised], Mathf.Max(face[AvatarExpression.Angry], face[AvatarExpression.Sad])),
                FaceTuningPart.Mouth => face[AvatarExpression.Aa] + face[AvatarExpression.Ih] + face[AvatarExpression.Ou] + face[AvatarExpression.Ee] + face[AvatarExpression.Oh],
                FaceTuningPart.Gaze => Mathf.Max(Mathf.Abs(face.LookYaw), Mathf.Abs(face.LookPitch)),
                FaceTuningPart.Smile => face[AvatarExpression.Happy],
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
            return new DisposeAll(subscriptions);
        }

        private void ShowTuning(FaceTuning tuning)
        {
            for (int i = 0; i < _sliders.Length; i++)
            {
                _sliders[i].SetValueWithoutNotify(tuning.Get((FaceTuningPart)i) * PercentPerUnit);
            }
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
