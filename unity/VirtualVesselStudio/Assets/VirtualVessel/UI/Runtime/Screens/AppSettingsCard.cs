using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine.UIElements;
using VirtualVessel.UI.Controls;
using VirtualVessel.UI.Localization;
using VirtualVessel.UI.Settings;
using VirtualVessel.UI.Theme;

namespace VirtualVessel.UI.Screens
{
    /// <summary>
    /// The "App settings" card: language, palette, background, UI size, reduced motion, and Developer
    /// Mode (UI foundation detailed design 7.7). Temporarily the whole Setup screen.
    /// </summary>
    /// <remarks>
    /// Choices go straight to <see cref="UiSettingsService"/>, which applies and saves them. A change
    /// made elsewhere is mirrored without raising change events, so the controls never echo it back.
    /// </remarks>
    public sealed class AppSettingsCard : VisualElement
    {
        private static readonly string[] s_scaleKeys = { ScreenStrings.Scale100, ScreenStrings.Scale110, ScreenStrings.Scale125, ScreenStrings.Scale150, ScreenStrings.Scale175, ScreenStrings.Scale200 };

        private readonly UiSettingsService _settings;

        public AppSettingsCard(UiSettingsService settings, IEnumerable<LocaleInfo> languages, ThemeAssets theme)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (theme == null)
            {
                throw new ArgumentNullException(nameof(theme));
            }

            Card card = Ui.Card(ScreenStrings.AppSettingsTitle);
            Add(card);

            Language = Ui.Select(null);
            Language.SetChoices(languages.Select(locale => SelectChoice.FromText(locale.Code, locale.DisplayName)));
            Language.RegisterValueChangedCallback(evt => _settings.SetLanguage(evt.newValue));
            card.Add(Row(ScreenStrings.Language, null, Language));

            Palette = Ui.Select(null);
            Palette.SetChoices(theme.PaletteIds.Select(id => SelectChoice.FromText(id, theme.GetPaletteName(id) ?? id)));
            Palette.RegisterValueChangedCallback(evt => _settings.SetPalette(evt.newValue));
            card.Add(Row(ScreenStrings.Palette, null, Palette));

            Background = Ui.Segmented(ScreenStrings.Dark, ScreenStrings.Light);
            Background.RegisterValueChangedCallback(evt => _settings.SetBackground(evt.newValue == 1 ? ThemeBackground.Light : ThemeBackground.Dark));
            card.Add(Row(ScreenStrings.Background, null, Background));

            // Six steps, and a long "Match Windows settings": dropdowns, as components.md 3.18 asks for
            // five or more choices or long labels.
            Scale = Ui.Select(null);
            Scale.SetChoices(s_scaleKeys.Select((key, index) => SelectChoice.FromKey(ScaleId(UiSettingsResolver.UiScales[index]), key)));
            Scale.RegisterValueChangedCallback(evt => _settings.SetUiScale(ParseScaleId(evt.newValue)));
            card.Add(Row(ScreenStrings.Scale, null, Scale));

            Motion = Ui.Select(null);
            Motion.SetChoices(new[]
            {
                SelectChoice.FromKey(UiSettingsResolver.MotionSystem, ScreenStrings.MotionSystem),
                SelectChoice.FromKey(UiSettingsResolver.MotionOn, ScreenStrings.MotionOn),
                SelectChoice.FromKey(UiSettingsResolver.MotionOff, ScreenStrings.MotionOff),
            });
            Motion.RegisterValueChangedCallback(evt => _settings.SetReducedMotion(ParseMotionId(evt.newValue)));
            card.Add(Row(ScreenStrings.Motion, ScreenStrings.MotionDescription, Motion));

            Developer = new SwitchToggle();
            Developer.RegisterValueChangedCallback(evt => _settings.SetDeveloperMode(evt.newValue));
            card.Add(Row(ScreenStrings.Developer, ScreenStrings.DeveloperDescription, Developer));

            Show(settings.Current);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Show(_settings.Current);
                _settings.Changed += Show;
            });
            RegisterCallback<DetachFromPanelEvent>(_ => _settings.Changed -= Show);
        }

        public SelectField Language { get; }

        public SelectField Palette { get; }

        public Segmented Background { get; }

        public SelectField Scale { get; }

        public SelectField Motion { get; }

        public SwitchToggle Developer { get; }

        /// <summary>The choice id of a scale step, such as "1.25". Pure.</summary>
        public static string ScaleId(float scale)
        {
            return scale.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>The scale step of a choice id; 1 for anything else. Pure.</summary>
        public static float ParseScaleId(string id)
        {
            return double.TryParse(id, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? UiSettingsResolver.FindScale(value) ?? 1f
                : 1f;
        }

        /// <summary>The reduce-motion choice of a choice id ("system", "on", "off"). Pure.</summary>
        public static ReducedMotionMode ParseMotionId(string id)
        {
            return id switch
            {
                UiSettingsResolver.MotionOn => ReducedMotionMode.On,
                UiSettingsResolver.MotionOff => ReducedMotionMode.Off,
                _ => ReducedMotionMode.System,
            };
        }

        private void Show(ResolvedUiSettings settings)
        {
            Language.SetValueWithoutNotify(settings.Language);
            Palette.SetValueWithoutNotify(settings.Theme.Palette);
            Background.SetValueWithoutNotify(settings.Theme.Background == ThemeBackground.Light ? 1 : 0);
            Scale.SetValueWithoutNotify(ScaleId(settings.UiScale));
            Motion.SetValueWithoutNotify(UiSettingsResolver.ToText(settings.ReducedMotionMode));
            Developer.SetValueWithoutNotify(settings.DeveloperMode);
        }

        private static SettingRow Row(string nameKey, string descriptionKey, VisualElement control)
        {
            SettingRow row = Ui.SettingRow(nameKey, descriptionKey);
            row.Add(control);
            return row;
        }
    }
}
