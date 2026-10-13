using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Icons;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// A project icon drawn with the element's text color, so it follows hover, disabled, and theme
    /// colors without separate images.
    /// </summary>
    [UxmlElement]
    public partial class Icon : VisualElement
    {
        public const string UssClass = "vv-icon";

        private IconName _name;

        public Icon()
            : this(IconName.None)
        {
        }

        public Icon(IconName name)
        {
            _name = name;
            AddToClassList(UssClass);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        [UxmlAttribute("icon")]
        public IconName Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    MarkDirtyRepaint();
                }
            }
        }

        private void Draw(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            IReadOnlyList<IconPart> parts = IconLibrary.Get(_name);
            if (rect.width <= 0 || rect.height <= 0 || parts.Count == 0)
            {
                return;
            }

            float scale = Mathf.Min(rect.width, rect.height) / IconLibrary.GridSize;
            var offset = new Vector2(
                rect.x + ((rect.width - (IconLibrary.GridSize * scale)) / 2f),
                rect.y + ((rect.height - (IconLibrary.GridSize * scale)) / 2f));
            Color color = resolvedStyle.color;
            Painter2D painter = context.painter2D;
            painter.strokeColor = color;
            painter.fillColor = color;
            painter.lineWidth = IconLibrary.StrokeWidth * scale;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;

            foreach (IconPart part in parts)
            {
                foreach (IconPolyline line in part.Lines)
                {
                    painter.BeginPath();
                    painter.MoveTo(offset + (line.Points[0] * scale));
                    for (int i = 1; i < line.Points.Count; i++)
                    {
                        painter.LineTo(offset + (line.Points[i] * scale));
                    }

                    if (line.Closed)
                    {
                        painter.ClosePath();
                    }

                    if (part.Filled)
                    {
                        painter.Fill();
                    }
                    else
                    {
                        painter.Stroke();
                    }
                }
            }
        }
    }
}
