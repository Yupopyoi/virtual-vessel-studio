using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace VirtualVessel.UI.Screens
{
    /// <summary>
    /// One plane of the skeleton comparison (UI foundation detailed design 7.7): the tracked bones solid,
    /// the avatar's dashed, so they differ by shape as well as color, over a faint level line through the
    /// hips. Colors come from tokens through USS.
    /// </summary>
    public sealed class SkeletonView : VisualElement
    {
        public const string UssClass = "vv-skeleton-view";

        // The space shown, in meters around the hips: wide enough for arms out sideways or forward.
        private const float HalfWidth = 0.95f;
        private const float Below = 0.35f;
        private const float Above = 1.05f;
        private const float LineWidth = 2.5f;
        private const float Dash = 6f;

        private static readonly CustomStyleProperty<Color> s_trackedColor = new CustomStyleProperty<Color>("--skeleton-tracked");
        private static readonly CustomStyleProperty<Color> s_avatarColor = new CustomStyleProperty<Color>("--skeleton-avatar");
        private static readonly CustomStyleProperty<Color> s_guideColor = new CustomStyleProperty<Color>("--skeleton-guide");

        private readonly SkeletonSegment[] _segments = new SkeletonSegment[SkeletonProjection.MaxSegments];
        private int _count;
        private Color _tracked = Color.white;
        private Color _avatar = Color.cyan;
        private Color _guide = Color.gray;

        public SkeletonView(SkeletonPlane plane)
        {
            Plane = plane;
            AddToClassList(UssClass);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyle);
        }

        public SkeletonPlane Plane { get; }

        /// <summary>How many lines are drawn; for tests.</summary>
        public int SegmentCount => _count;

        /// <summary>Takes a copy of the first <paramref name="count"/> lines to draw.</summary>
        public void Show(SkeletonSegment[] segments, int count)
        {
            _count = Math.Min(count, _segments.Length);
            Array.Copy(segments, _segments, _count);
            MarkDirtyRepaint();
        }

        /// <summary>
        /// Where a point in meters goes in a box of <paramref name="width"/> × <paramref name="height"/>:
        /// the shown space scaled alike on both axes, centered, up on screen being up. Pure.
        /// </summary>
        public static Vector2 ToBox(Vector2 meters, float width, float height)
        {
            float scale = Mathf.Min(width / (2f * HalfWidth), height / (Below + Above));
            float originX = width * 0.5f;
            float originY = (height * 0.5f) + (((Above - Below) * 0.5f) * scale);
            return new Vector2(originX + (meters.x * scale), originY - (meters.y * scale));
        }

        private void OnCustomStyle(CustomStyleResolvedEvent evt)
        {
            evt.customStyle.TryGetValue(s_trackedColor, out _tracked);
            evt.customStyle.TryGetValue(s_avatarColor, out _avatar);
            evt.customStyle.TryGetValue(s_guideColor, out _guide);
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            float width = contentRect.width;
            float height = contentRect.height;
            if (width <= 0f || height <= 0f)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            painter.lineWidth = 1f;
            painter.strokeColor = _guide;
            painter.BeginPath();
            painter.MoveTo(ToBox(new Vector2(-HalfWidth, 0f), width, height));
            painter.LineTo(ToBox(new Vector2(HalfWidth, 0f), width, height));
            painter.Stroke();

            painter.lineWidth = LineWidth;
            painter.lineCap = LineCap.Round;
            for (int i = 0; i < _count; i++)
            {
                SkeletonSegment segment = _segments[i];
                Vector2 from = ToBox(segment.From, width, height);
                Vector2 to = ToBox(segment.To, width, height);
                painter.strokeColor = segment.Avatar ? _avatar : _tracked;
                painter.BeginPath();
                if (segment.Avatar)
                {
                    Dashed(painter, from, to);
                }
                else
                {
                    painter.MoveTo(from);
                    painter.LineTo(to);
                }

                painter.Stroke();
            }
        }

        private static void Dashed(Painter2D painter, Vector2 from, Vector2 to)
        {
            float length = Vector2.Distance(from, to);
            Vector2 step = length > 0f ? (to - from) / length : Vector2.zero;
            for (float at = 0f; at < length; at += Dash * 2f)
            {
                painter.MoveTo(from + (step * at));
                painter.LineTo(from + (step * Mathf.Min(at + Dash, length)));
            }
        }
    }
}
