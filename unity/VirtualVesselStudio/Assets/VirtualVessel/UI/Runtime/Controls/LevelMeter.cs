using System;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.Core.State;
using VirtualVessel.UI.Binding;

namespace VirtualVessel.UI.Controls
{
    /// <summary>Which range a meter segment belongs to.</summary>
    public enum MeterSegment
    {
        Normal,
        Warning,

        /// <summary>The very top, where the signal is about to clip.</summary>
        Peak,
    }

    /// <summary>
    /// A segmented level bar, such as microphone input (components.md 3.5). The normal range is Success,
    /// the top quarter is Warning, and the top segments are Error red; the number of lit segments shows
    /// the amount, not only the color.
    /// </summary>
    /// <remarks>
    /// Real-time values arrive far more often than the screen needs, so <see cref="BindLevel"/> reads the
    /// source at a fixed rate (30 Hz by default) instead of on every change. Setting
    /// <see cref="Level"/> does not allocate; the segments are drawn in one pass.
    /// </remarks>
    [UxmlElement]
    public partial class LevelMeter : VisualElement
    {
        public const string UssClass = "vv-level-meter";

        public const long DefaultIntervalMs = 33;

        private const float Gap = 2f;

        private static readonly CustomStyleProperty<Color> s_onColor = new CustomStyleProperty<Color>("--meter-on");
        private static readonly CustomStyleProperty<Color> s_warnColor = new CustomStyleProperty<Color>("--meter-warn");
        private static readonly CustomStyleProperty<Color> s_offColor = new CustomStyleProperty<Color>("--meter-off");
        private static readonly CustomStyleProperty<Color> s_peakColor = new CustomStyleProperty<Color>("--meter-peak");

        private int _segments = 24;
        private float _level;
        private int _lit;
        private Color _on = Color.green;
        private Color _warn = Color.yellow;
        private Color _off = Color.gray;
        private Color _peak = Color.red;
        private IDisposable _binding;

        public LevelMeter()
        {
            AddToClassList(UssClass);
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyle);
        }

        [UxmlAttribute("segments")]
        public int Segments
        {
            get => _segments;
            set
            {
                _segments = Mathf.Max(1, value);
                _lit = LitSegments(_level, _segments);
                MarkDirtyRepaint();
            }
        }

        /// <summary>The level from 0 to 1. Repaints only when the number of lit segments changes.</summary>
        public float Level
        {
            get => _level;
            set
            {
                _level = Mathf.Clamp01(value);
                int lit = LitSegments(_level, _segments);
                if (lit != _lit)
                {
                    _lit = lit;
                    MarkDirtyRepaint();
                }
            }
        }

        public int LitCount => _lit;

        /// <summary>How many segments a level lights. Pure.</summary>
        public static int LitSegments(float level, int segments)
        {
            return Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(level) * segments), 0, segments);
        }

        /// <summary>
        /// The range of segment <paramref name="index"/> (from 0): the top 1/12 (2 of 24) is Peak, the rest
        /// of the top quarter is Warning. Pure.
        /// </summary>
        public static MeterSegment KindOf(int index, int segments)
        {
            int peak = Mathf.Max(1, Mathf.RoundToInt(segments / 12f));
            int warning = Mathf.CeilToInt(segments * 0.25f);
            return index >= segments - peak ? MeterSegment.Peak
                : index >= segments - warning ? MeterSegment.Warning
                : MeterSegment.Normal;
        }

        /// <summary>
        /// Shows <paramref name="source"/>, read every <paramref name="intervalMs"/> while the meter is on
        /// a panel. Reading on a timer keeps the audio thread free of UI work and allocations.
        /// </summary>
        public IDisposable BindLevel(IReadOnlyObservable<float> source, long intervalMs = DefaultIntervalMs)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            _binding?.Dispose();
            _binding = PanelLifetime.WhileAttached(this, _ =>
            {
                Level = source.Value;
                IVisualElementScheduledItem poll = schedule.Execute(() => Level = source.Value).Every(intervalMs);
                return new DisposeAction(poll.Pause);
            });
            return _binding;
        }

        private Color ColorFor(MeterSegment kind)
        {
            return kind == MeterSegment.Peak ? _peak : kind == MeterSegment.Warning ? _warn : _on;
        }

        private void OnCustomStyle(CustomStyleResolvedEvent evt)
        {
            evt.customStyle.TryGetValue(s_onColor, out _on);
            evt.customStyle.TryGetValue(s_warnColor, out _warn);
            evt.customStyle.TryGetValue(s_offColor, out _off);
            evt.customStyle.TryGetValue(s_peakColor, out _peak);
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            float width = (rect.width - (Gap * (_segments - 1))) / _segments;
            Painter2D painter = context.painter2D;
            for (int i = 0; i < _segments; i++)
            {
                painter.fillColor = i >= _lit ? _off : ColorFor(KindOf(i, _segments));
                float x = rect.x + (i * (width + Gap));
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, rect.yMin));
                painter.LineTo(new Vector2(x + width, rect.yMin));
                painter.LineTo(new Vector2(x + width, rect.yMax));
                painter.LineTo(new Vector2(x, rect.yMax));
                painter.ClosePath();
                painter.Fill();
            }
        }
    }
}
