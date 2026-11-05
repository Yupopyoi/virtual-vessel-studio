using System;
using System.Globalization;
using UnityEngine.UIElements;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Localization;

namespace VirtualVessel.UI.Controls
{
    [StringKeys]
    public static class CandidateTileStrings
    {
        /// <summary>"Candidate {n}".</summary>
        public const string Name = "common.candidate.name";
        public const string Picked = "common.candidate.picked";
        public const string Pick = "common.candidate.pick";
        public const string Play = "common.candidate.play";
        public const string Pitch = "common.candidate.pitch";
        public const string Range = "common.candidate.range";
    }

    /// <summary>
    /// One candidate to listen to, compare by numbers, and pick (components.md 3.27): its pitch, and its range
    /// drawn on a scale every tile shares, so tiles compare at a glance.
    /// </summary>
    public sealed class CandidateTile : VisualElement
    {
        public const string UssClass = "vv-tile";
        public const string PickedClass = UssClass + "--picked";

        /// <summary>The shared scale, in Hz: a voice's usual range and then some.</summary>
        public const float ScaleLowHz = 150f;
        public const float ScaleHighHz = 700f;

        private readonly StatusChip _picked;
        private readonly Label _name;
        private readonly int _number;

        public CandidateTile(int number, int medianHz, int lowHz, int highHz)
        {
            _number = number;
            AddToClassList(UssClass);

            var head = new VisualElement();
            head.AddToClassList(UssClass + "__head");
            PlayButton = Ui.IconButton(IconName.Play).WithTooltip(CandidateTileStrings.Play);
            _name = new Label();
            _name.AddToClassList(UssClass + "__name");
            _name.AddToClassList("vv-bold");
            _picked = Ui.Chip(StatusKind.Success, CandidateTileStrings.Picked);
            head.Add(PlayButton);
            head.Add(_name);
            head.Add(_picked);
            Add(head);

            Add(Stat(CandidateTileStrings.Pitch, HzText(medianHz)));
            Add(Stat(CandidateTileStrings.Range, RangeText(lowHz, highHz)));
            Add(Span(medianHz, lowHz, highHz));

            PickButton = Ui.Button(CandidateTileStrings.Pick);
            PickButton.AddToClassList(UssClass + "__pick");
            Add(PickButton);

            Picked = false;
            RegisterCallback<AttachToPanelEvent>(_ => _name.text = NameText(UiServices.Find(this)?.Localizer, _number));
        }

        public IconButton PlayButton { get; }

        public ActionButton PickButton { get; }

        public bool Picked
        {
            get => ClassListContains(PickedClass);
            set
            {
                EnableInClassList(PickedClass, value);
                _picked.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>Where <paramref name="hz"/> sits on the shared scale, 0 to 100 percent. Pure.</summary>
        public static float ScalePercent(float hz)
        {
            return Math.Max(0f, Math.Min(100f, 100f * (hz - ScaleLowHz) / (ScaleHighHz - ScaleLowHz)));
        }

        /// <summary>"268 Hz", or a dash when the voice had no pitch. Pure.</summary>
        public static string HzText(int hz)
        {
            return hz > 0 ? hz.ToString(CultureInfo.InvariantCulture) + " Hz" : "—";
        }

        /// <summary>"203–338 Hz", or a dash. Pure.</summary>
        public static string RangeText(int lowHz, int highHz)
        {
            return lowHz > 0 && highHz > 0 ? string.Format(CultureInfo.InvariantCulture, "{0}–{1} Hz", lowHz, highHz) : "—";
        }

        /// <summary>"Candidate n" in the language. Pure.</summary>
        public static string NameText(ILocalizer localizer, int number)
        {
            string n = number.ToString(CultureInfo.InvariantCulture);
            return localizer?.Get(CandidateTileStrings.Name, ("n", n)) ?? "#" + n;
        }

        private static VisualElement Stat(string nameKey, string value)
        {
            var row = new VisualElement();
            row.AddToClassList(UssClass + "__stat");
            var name = new Label();
            name.AddToClassList(UssClass + "__stat-name");
            _ = new KeyedText(name, text => name.text = text) { Key = nameKey };
            var number = new Label(value);
            number.AddToClassList("vv-mono");
            row.Add(name);
            row.Add(number);
            return row;
        }

        private static VisualElement Span(int medianHz, int lowHz, int highHz)
        {
            var span = new VisualElement();
            span.AddToClassList(UssClass + "__span");
            var track = new VisualElement();
            track.AddToClassList(UssClass + "__track");
            span.Add(track);
            var line = new VisualElement();
            line.AddToClassList(UssClass + "__line");
            track.Add(line);
            if (lowHz > 0 && highHz > lowHz)
            {
                var band = new VisualElement();
                band.AddToClassList(UssClass + "__band");
                band.style.left = Length.Percent(ScalePercent(lowHz));
                band.style.width = Length.Percent(ScalePercent(highHz) - ScalePercent(lowHz));
                track.Add(band);
            }

            if (medianHz > 0)
            {
                var mid = new VisualElement();
                mid.AddToClassList(UssClass + "__mid");
                mid.style.left = Length.Percent(ScalePercent(medianHz));
                track.Add(mid);
            }

            var scale = new VisualElement();
            scale.AddToClassList(UssClass + "__scale");
            foreach (string mark in new[] { "150", "400", "700 Hz" })
            {
                scale.Add(new Label(mark));
            }

            span.Add(scale);
            return span;
        }
    }
}
