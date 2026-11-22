using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Binding;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Lighting;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Preview;

namespace VirtualVessel.UI.Screens
{
    /// <summary>String table keys of the lighting Card (docs/&lt;lang&gt;/ui/screens/setup.md 15).</summary>
    [StringKeys]
    public static class LightingStrings
    {
        public const string Title = "setup.lighting.title";
        public const string Light = "setup.lighting.light";
        public const string LightHelp = "setup.lighting.light.help";
        public const string Light1 = "setup.lighting.light.1";
        public const string Light2 = "setup.lighting.light.2";
        public const string Light3 = "setup.lighting.light.3";
        public const string Light4 = "setup.lighting.light.4";
        public const string Add = "setup.lighting.add";
        public const string AddFull = "setup.lighting.add.full";
        public const string Remove = "setup.lighting.remove";
        public const string RemoveLast = "setup.lighting.remove.last";
        public const string Kind = "setup.lighting.kind";
        public const string KindHelp = "setup.lighting.kind.help";
        public const string KindDirectional = "setup.lighting.kind.directional";
        public const string KindSpot = "setup.lighting.kind.spot";
        public const string Yaw = "setup.lighting.yaw";
        public const string YawHelp = "setup.lighting.yaw.help";
        public const string Pitch = "setup.lighting.pitch";
        public const string PitchHelp = "setup.lighting.pitch.help";
        public const string Distance = "setup.lighting.distance";
        public const string DistanceHelp = "setup.lighting.distance.help";
        public const string SpotOnly = "setup.lighting.spot-only";
        public const string Intensity = "setup.lighting.intensity";
        public const string IntensityHelp = "setup.lighting.intensity.help";
        public const string Color = "setup.lighting.color";
        public const string ColorHelp = "setup.lighting.color.help";
        public const string Temperature = "setup.lighting.temperature";
        public const string TemperatureHelp = "setup.lighting.temperature.help";
        public const string WhiteOnly = "setup.lighting.white-only";
        public const string Code = "setup.lighting.code";
        public const string CodeHelp = "setup.lighting.code.help";
        public const string CodeInvalid = "setup.lighting.code.invalid";
        public const string RgbHelp = "setup.lighting.rgb.help";
        public const string Red = "setup.lighting.rgb.r";
        public const string Green = "setup.lighting.rgb.g";
        public const string Blue = "setup.lighting.rgb.b";
        public const string CustomOnly = "setup.lighting.custom-only";
        public const string SpotAngle = "setup.lighting.spot-angle";
        public const string SpotAngleHelp = "setup.lighting.spot-angle.help";
        public const string Ambient = "setup.lighting.ambient";
        public const string AmbientHelp = "setup.lighting.ambient.help";
        public const string Reset = "setup.lighting.reset";
        public const string ResetHelp = "setup.lighting.reset.help";
        public const string ResetButton = "setup.lighting.reset.button";

        /// <summary>The string table key of a light color's name.</summary>
        public static string ColorName(string colorId) => "setup.lighting.color." + colorId;
    }

    /// <summary>
    /// The stage's lights on the avatar page (docs/&lt;lang&gt;/ui/screens/setup.md 15): up to four lights,
    /// each a whole-stage light or a spot, placed by angles around the avatar, with its brightness and
    /// color, and the ambient light. Each change applies at once and is saved by Stage. The stream is
    /// on the left and the settings scroll on its right, so the avatar stays in view while any light
    /// moves (user's report, 2026-11-22: below a long list, the picture was out of sight).
    /// </summary>
    public sealed class LightingCard : VisualElement
    {
        public const string UssClass = "vv-lighting-card";
        public const string SwatchClass = UssClass + "__swatch";
        public const string LayoutClass = UssClass + "__layout";
        public const string PictureClass = UssClass + "__picture";
        public const string SettingsClass = UssClass + "__settings";

        private const float PercentPerUnit = 100f;

        private static readonly string[] s_lightKeys = { LightingStrings.Light1, LightingStrings.Light2, LightingStrings.Light3, LightingStrings.Light4 };

        private readonly IStageLighting _lighting;
        private readonly IStreamPreviewSource _stream;
        private readonly Label _noVideo;
        private int _selected;

        /// <param name="stream">The stream video, to watch the light on the avatar; null shows no picture.</param>
        public LightingCard(IStageLighting lighting, IStreamPreviewSource stream)
        {
            _lighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
            _stream = stream;
            AddToClassList(UssClass);
            AddToClassList(CaptureSourceCard.UssClass);
            Card card = Ui.Card(LightingStrings.Title);
            Add(card);

            var layout = new VisualElement();
            layout.AddToClassList(LayoutClass);
            card.Add(layout);
            Preview = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            Preview.AddToClassList(PictureClass);
            _noVideo = Ui.Text(CaptureStrings.NoVideo, CaptureSourceCard.PreviewEmptyClass, "vv-text-caption");
            Preview.Add(_noVideo);
            layout.Add(Preview);
            Settings = new ScrollView(ScrollViewMode.Vertical);
            Settings.AddToClassList(SettingsClass);
            layout.Add(Settings);
            VisualElement rows = Settings.contentContainer;

            // Which light the rows below change; the lights not made yet cannot be chosen.
            LightChoice = Ui.Segmented(s_lightKeys);
            LightChoice.RegisterValueChangedCallback(evt =>
            {
                _selected = evt.newValue;
                Show();
            });
            rows.Add(Ui.ControlRow(LightingStrings.Light, LightChoice, helpKey: LightingStrings.LightHelp));

            // Adding and removing act on the whole list, so they sit in the title band, as the sound effects'
            // "stop all" does.
            AddButton = Ui.Button(LightingStrings.Add);
            AddButton.clicked += AddLight;
            RemoveButton = Ui.Button(LightingStrings.Remove);
            RemoveButton.clicked += RemoveLight;
            var buttons = new VisualElement();
            buttons.AddToClassList(SettingRow.ControlsClass);
            buttons.Add(AddButton);
            buttons.Add(RemoveButton);
            card.HeaderEnd.Add(buttons);

            Kind = Ui.Segmented(LightingStrings.KindDirectional, LightingStrings.KindSpot);
            Kind.RegisterValueChangedCallback(evt => Change(light => light.WithKind(evt.newValue == 1 ? StageLightKind.Spot : StageLightKind.Directional)));
            rows.Add(Ui.ControlRow(LightingStrings.Kind, Kind, helpKey: LightingStrings.KindHelp));

            Yaw = Slider(rows, LightingStrings.Yaw, LightingStrings.YawHelp, StageLight.MinYaw, StageLight.MaxYaw, 5f, "°", value => Change(light => light.WithYaw(value)));
            Yaw.Signed = true;
            Pitch = Slider(rows, LightingStrings.Pitch, LightingStrings.PitchHelp, StageLight.MinPitch, StageLight.MaxPitch, 5f, "°", value => Change(light => light.WithPitch(value)));
            Pitch.Signed = true;
            Distance = Slider(rows, LightingStrings.Distance, LightingStrings.DistanceHelp, StageLight.MinDistance, StageLight.MaxDistance, 0.1f, "m", value => Change(light => light.WithDistance(value)));
            Distance.Decimals = 1;
            SpotAngle = Slider(rows, LightingStrings.SpotAngle, LightingStrings.SpotAngleHelp, StageLight.MinSpotAngle, StageLight.MaxSpotAngle, 5f, "°", value => Change(light => light.WithSpotAngle(value)));
            Intensity = Slider(rows, LightingStrings.Intensity, LightingStrings.IntensityHelp, StageLight.MinIntensity * PercentPerUnit, StageLight.MaxIntensity * PercentPerUnit, 5f, "%", value => Change(light => light.WithIntensity(value / PercentPerUnit)));

            ColorChoice = Ui.Select(null);
            ColorChoice.SetChoices(StageLightColors.Ids.Append(StageLightColors.Custom).Select(id => SelectChoice.FromKey(id, LightingStrings.ColorName(id))));

            // "Custom" starts from the color the light has, so a stage color can be adjusted from where it is.
            ColorChoice.RegisterValueChangedCallback(evt => Change(light => light.WithColor(evt.newValue == StageLightColors.Custom ? StageLightColors.ToCode(StageLightColors.ColorOf(light)) : evt.newValue)));

            // The color itself beside its name; the name is what tells the colors apart (components.md 2).
            Swatch = new VisualElement { pickingMode = PickingMode.Ignore };
            Swatch.AddToClassList(SwatchClass);
            var color = new VisualElement();
            color.AddToClassList(SettingRow.ControlsClass);
            color.Add(Swatch);
            color.Add(ColorChoice);
            rows.Add(Ui.ControlRow(LightingStrings.Color, color, helpKey: LightingStrings.ColorHelp));
            Temperature = Slider(rows, LightingStrings.Temperature, LightingStrings.TemperatureHelp, StageLight.MinTemperature, StageLight.MaxTemperature, 100f, "K", value => Change(light => light.WithTemperature(value)));

            // A color of the user's own, by its code or by its red, green, and blue (user's request, 2026-11-22).
            // The code applies on leaving the field or on Enter, not on each key: half a code is not a color.
            CodeField = Ui.TextField(null);
            CodeField.MaxLength = 7;
            CodeField.RegisterCallback<FocusOutEvent>(_ => ApplyCode());
            CodeField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    ApplyCode();
                }
            });
            rows.Add(Ui.ControlRow(LightingStrings.Code, CodeField, helpKey: LightingStrings.CodeHelp));
            RedSlider = Channel(LightingStrings.Red, 0);
            GreenSlider = Channel(LightingStrings.Green, 1);
            BlueSlider = Channel(LightingStrings.Blue, 2);
            rows.Add(RedSlider);
            rows.Add(GreenSlider);
            rows.Add(BlueSlider);

            Ambient = Slider(rows, LightingStrings.Ambient, LightingStrings.AmbientHelp, StageLighting.MinAmbient * PercentPerUnit, StageLighting.MaxAmbient * PercentPerUnit, 5f, "%", value => _lighting.SetLighting(_lighting.Lighting.Value.WithAmbient(value / PercentPerUnit)));

            ResetButton = Ui.Button(LightingStrings.ResetButton);
            ResetButton.clicked += () =>
            {
                _selected = 0;
                _lighting.SetLighting(StageLighting.Default);
            };
            rows.Add(Ui.ControlRow(LightingStrings.Reset, ResetButton, helpKey: LightingStrings.ResetHelp));

            PanelLifetime.WhileAttached(this, _ => Attach());
        }

        public Image Preview { get; }

        /// <summary>The settings beside the picture, scrolled on their own.</summary>
        public ScrollView Settings { get; }

        public Segmented LightChoice { get; }

        public ActionButton AddButton { get; }

        public ActionButton RemoveButton { get; }

        public Segmented Kind { get; }

        public ValueSlider Yaw { get; }

        public ValueSlider Pitch { get; }

        public ValueSlider Distance { get; }

        public ValueSlider SpotAngle { get; }

        public ValueSlider Intensity { get; }

        public SelectField ColorChoice { get; }

        public VisualElement Swatch { get; }

        public ValueSlider Temperature { get; }

        public TextInput CodeField { get; }

        public ValueSlider RedSlider { get; }

        public ValueSlider GreenSlider { get; }

        public ValueSlider BlueSlider { get; }

        public ValueSlider Ambient { get; }

        public ActionButton ResetButton { get; }

        /// <summary>The light the rows change, 0 to 3.</summary>
        public int Selected => _selected;

        /// <summary>The light at <paramref name="selected"/>, or the last one when it is gone (a light removed). Pure.</summary>
        public static int SelectedIn(StageLighting lighting, int selected)
        {
            return Math.Max(0, Math.Min(selected, lighting.Lights.Count - 1));
        }

        // A slider with its name above it, as in the face tuning Card: a Setting Row each made the list too long
        // to keep beside the picture.
        private static ValueSlider Slider(VisualElement rows, string nameKey, string helpKey, float low, float high, float step, string unit, Action<float> changed)
        {
            ValueSlider slider = Ui.Slider(nameKey, low, high, unit);
            slider.Step = step;
            slider.HelpKey = helpKey;
            slider.RegisterValueChangedCallback(evt => changed(evt.newValue));
            rows.Add(slider);
            return slider;
        }

        /// <summary>
        /// <paramref name="color"/> with one 0–255 channel (0 red, 1 green, 2 blue) set to <paramref name="value"/>,
        /// as a color code. Pure.
        /// </summary>
        public static string WithChannel(Color color, int channel, int value)
        {
            int r = StageLightColors.Byte(color.r);
            int g = StageLightColors.Byte(color.g);
            int b = StageLightColors.Byte(color.b);
            return StageLightColors.ToCode(StageLightColors.FromBytes(channel == 0 ? value : r, channel == 1 ? value : g, channel == 2 ? value : b));
        }

        private ValueSlider Channel(string labelKey, int channel)
        {
            ValueSlider slider = Ui.Slider(labelKey, 0f, 255f);
            slider.Step = 1f;
            slider.HelpKey = LightingStrings.RgbHelp;
            slider.RegisterValueChangedCallback(evt => Change(light => light.WithColor(WithChannel(StageLightColors.ColorOf(light), channel, (int)evt.newValue))));
            return slider;
        }

        private void ApplyCode()
        {
            if (!StageLightColors.TryParse(CodeField.value, out Color color))
            {
                CodeField.ErrorKey = LightingStrings.CodeInvalid;
                return;
            }

            CodeField.ErrorKey = null;
            Change(light => light.WithColor(StageLightColors.ToCode(color)));
        }

        private IDisposable Attach()
        {
            var subscriptions = new List<IDisposable> { _lighting.Lighting.Subscribe(_ => Show()) };
            if (_stream != null)
            {
                subscriptions.Add(_stream.Video.Subscribe(ShowVideo));
            }

            ShowVideo(_stream?.Video.Value);
            Show();
            return new DisposeAll(subscriptions);
        }

        private void ShowVideo(Texture texture)
        {
            Preview.image = texture;
            _noVideo.style.display = texture == null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void Change(Func<StageLight, StageLight> change)
        {
            StageLighting lighting = _lighting.Lighting.Value;
            int index = SelectedIn(lighting, _selected);
            _lighting.SetLighting(lighting.WithLight(index, change(lighting.Lights[index])));
        }

        private void AddLight()
        {
            StageLighting lighting = _lighting.Lighting.Value;
            if (!lighting.CanAdd)
            {
                return;
            }

            _selected = lighting.Lights.Count;
            _lighting.SetLighting(lighting.WithAdded(StageLight.Added));
        }

        private void RemoveLight()
        {
            StageLighting lighting = _lighting.Lighting.Value;
            _lighting.SetLighting(lighting.WithRemoved(SelectedIn(lighting, _selected)));
            _selected = SelectedIn(_lighting.Lighting.Value, _selected);
            Show();
        }

        private void Show()
        {
            StageLighting lighting = _lighting.Lighting.Value;
            _selected = SelectedIn(lighting, _selected);
            StageLight light = lighting.Lights[_selected];
            for (int i = 0; i < s_lightKeys.Length; i++)
            {
                LightChoice.SetItemEnabled(i, i < lighting.Lights.Count);
            }

            LightChoice.SetValueWithoutNotify(_selected);
            AddButton.SetEnabled(lighting.CanAdd);
            AddButton.TooltipKey = lighting.CanAdd ? null : LightingStrings.AddFull;
            RemoveButton.SetEnabled(lighting.CanRemove);
            RemoveButton.TooltipKey = lighting.CanRemove ? null : LightingStrings.RemoveLast;

            bool spot = light.Kind == StageLightKind.Spot;
            Kind.SetValueWithoutNotify(spot ? 1 : 0);
            Yaw.SetValueWithoutNotify(light.Yaw);
            Pitch.SetValueWithoutNotify(light.Pitch);
            Distance.SetValueWithoutNotify(light.Distance);
            SpotAngle.SetValueWithoutNotify(light.SpotAngle);
            Intensity.SetValueWithoutNotify(light.Intensity * PercentPerUnit);
            bool custom = StageLightColors.IsCode(light.ColorId);
            Color color = StageLightColors.ColorOf(light);
            ColorChoice.SetValueWithoutNotify(custom ? StageLightColors.Custom : light.ColorId);
            Swatch.style.backgroundColor = color;
            Temperature.SetValueWithoutNotify(light.Temperature);

            // The code and the channels always show the light's color, so a named color's code can be read too;
            // the field is not rewritten while the user is typing in it.
            if (!(CodeField.focusController?.focusedElement is VisualElement focused && CodeField.Contains(focused)))
            {
                CodeField.SetValueWithoutNotify(StageLightColors.ToCode(color));
                CodeField.ErrorKey = null;
            }

            RedSlider.SetValueWithoutNotify(StageLightColors.Byte(color.r));
            GreenSlider.SetValueWithoutNotify(StageLightColors.Byte(color.g));
            BlueSlider.SetValueWithoutNotify(StageLightColors.Byte(color.b));
            Ambient.SetValueWithoutNotify(lighting.Ambient * PercentPerUnit);

            // A whole-stage light has no place or cone, and a stage color no temperature; they stay in view,
            // disabled with the reason, so the user sees what a spot or white would add.
            Enable(Distance, spot, LightingStrings.SpotOnly);
            Enable(SpotAngle, spot, LightingStrings.SpotOnly);
            Enable(Temperature, light.ColorId == StageLightColors.White, LightingStrings.WhiteOnly);
            CodeField.SetEnabled(custom);
            CodeField.TooltipKey = custom ? null : LightingStrings.CustomOnly;
            Enable(RedSlider, custom, LightingStrings.CustomOnly);
            Enable(GreenSlider, custom, LightingStrings.CustomOnly);
            Enable(BlueSlider, custom, LightingStrings.CustomOnly);
        }

        private static void Enable(ValueSlider slider, bool enabled, string reasonKey)
        {
            slider.SetEnabled(enabled);
            slider.TooltipKey = enabled ? null : reasonKey;
        }
    }
}
